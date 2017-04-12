using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NinjaTurtlesMutation.ServiceTestRunnerLib;
using NinjaTurtlesMutation.ServiceTestRunnerLib.Utilities;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NinjaTurtlesMutation.Dispatcher
{
    class Program
    {
        #region CooldownTimes

        private const int DISPATCH_JOBCHECK_COOLDOWN_MS = 100;
        private const int DISPATCH_RUNNER_ACQUISITION_COOLDOWN_MS = 100;
        private const int SENDING_JOBCHECK_COOLDOWN_MS = 100;

        #endregion

        private static readonly List<TestRunnerHandler> _testRunners = new List<TestRunnerHandler>();

        #region BackboneTasksProperties

        private static Task _senderTask;
        private static Task _receiverTask;
        private static Task _cmdTask;

        #endregion

        #region PipeStringHandlers

        private static string _pipeInStringHandler;
        private static string _pipeOutStringHandler;
        private static string _pipeCmdStringHandler;

        #endregion

        #region TasksCommunicationProperties

        private static readonly ConcurrentQueue<TestDescription> _unassignedJobs = new ConcurrentQueue<TestDescription>();
        private static readonly ConcurrentDictionary<int, TestDescription> _dispatchedJobs = new ConcurrentDictionary<int, TestDescription>();
        private static readonly ConcurrentQueue<TestDescription> _completedJobs = new ConcurrentQueue<TestDescription>();

        private static bool _shouldStop;
        private static int _busyRunnersCount;

        #endregion

        #region RunnersTweaks

        private static bool _oneTimeRunners;
        private static int _maxSimultaneousBusyRunners;
        private static float _killTimeFactor;

        #endregion

        static void Main(string[] args)
        {
            if (args.Length != 7)
                return;
            _pipeInStringHandler = args[0];
            _pipeOutStringHandler = args[1];
            _pipeCmdStringHandler = args[2];
            var numRunners = int.Parse(args[3]);
            _maxSimultaneousBusyRunners = int.Parse(args[4]);
            _oneTimeRunners = args[5] == true.ToString();
            _killTimeFactor = float.Parse(args[6]);
            InstantiateTestRunners(numRunners);
            InitSender();
            InitReceiver();
            InitCmdReceiver();
            Dispatch();
        }

        #region BackboneTasksMethods

        private static void Dispatch()
        {
            while (true)
            {
                while (_unassignedJobs.IsEmpty && !_shouldStop)
                    Thread.Sleep(DISPATCH_JOBCHECK_COOLDOWN_MS);
                if (_shouldStop && _unassignedJobs.IsEmpty)
                    break;
                TestDescription testToDispatch = null;
                if (!_unassignedJobs.TryDequeue(out testToDispatch))
                    continue;

                while (testToDispatch != null)
                {
                    int assignedRunnerIndex;
                    while (_busyRunnersCount >= _maxSimultaneousBusyRunners)
                        Thread.Sleep(DISPATCH_RUNNER_ACQUISITION_COOLDOWN_MS);
                    while ((assignedRunnerIndex = _testRunners.FindIndex(r => !r.isBusy)) == -1)
                        Thread.Sleep(DISPATCH_RUNNER_ACQUISITION_COOLDOWN_MS);
                    TestRunnerHandler assignedRunner = _testRunners[assignedRunnerIndex];
                    try
                    {
                        assignedRunner.SendJob(testToDispatch);
                    }
                    catch (IOException)
                    {
                        RunnerRestart(assignedRunner, assignedRunnerIndex);
                        continue;
                    }
                    _dispatchedJobs.TryAdd(assignedRunnerIndex, testToDispatch);
                    assignedRunner.isBusy = true;
                    Interlocked.Increment(ref _busyRunnersCount);
                    Task.Factory.StartNew(() => BusyRunnerHandler(assignedRunner, assignedRunnerIndex));
                    testToDispatch = null;
                }
            }
        }

        private static void ReceivingLoop()
        {
            var factory = new ConnectionFactory() { HostName = "localhost" };
            using (var connection = factory.CreateConnection())
            {
                using (var channel = connection.CreateModel())
                {
                    channel.QueueDeclare(queue: _pipeInStringHandler,
                                 durable: false,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null);

                    var consumer = new EventingBasicConsumer(channel);
                    consumer.Received += (model, ea) =>
                    {
                        var body = ea.Body;
                        var message = Encoding.UTF8.GetString(body);
                        var testDescription = TestDescriptionExchanger.ReadATestDescription(message);
                        _unassignedJobs.Enqueue(testDescription);
                    };
                    channel.BasicConsume(queue: _pipeInStringHandler,
                                         noAck: true,
                                         consumer: consumer);

                    while (!_shouldStop) { }
                }
            }
        }

        private static void SendingLoop()
        {
            var factory = new ConnectionFactory() { HostName = "localhost" };
            using (var connection = factory.CreateConnection())
            {
                using (var channel = connection.CreateModel())
                {
                    channel.QueueDeclare(queue: _pipeOutStringHandler,
                                 durable: false,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null);

                    while (true)
                    {
                        while (_completedJobs.IsEmpty && !_shouldStop)
                            Thread.Sleep(SENDING_JOBCHECK_COOLDOWN_MS);
                        if (_shouldStop && _completedJobs.IsEmpty)
                            break;
                        TestDescription testDescriptionToSend;
                        if (!_completedJobs.TryDequeue(out testDescriptionToSend))
                            continue;
                        TestDescriptionExchanger.SendATestDescription(channel, _pipeOutStringHandler, testDescriptionToSend);
                    }
                }
            }
        }

        private static void CmdLoop()
        {
            Dictionary<string, Func<bool>> cmdActions = new Dictionary<string, Func<bool>>();
            cmdActions[CommandExchanger.Commands.STOP] = Stop;

            var factory = new ConnectionFactory() { HostName = "localhost" };
            using (var connection = factory.CreateConnection())
            {
                using (var channel = connection.CreateModel())
                {
                    channel.QueueDeclare(queue: _pipeCmdStringHandler,
                                 durable: false,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null);

                    var consumer = new EventingBasicConsumer(channel);
                    consumer.Received += (model, ea) =>
                    {
                        var body = ea.Body;
                        var message = Encoding.UTF8.GetString(body);
                        cmdActions[message]();
                    };
                    channel.BasicConsume(queue: _pipeCmdStringHandler,
                                         noAck: true,
                                         consumer: consumer);

                    while (!_shouldStop) { }

                }
            }
        }

        #endregion

        #region RunnersHandlingMethods

        private static void BusyRunnerHandler(TestRunnerHandler busyRunner, int busyRunnerIndex)
        {
            TestDescription sink;
			var testResult = RetrieveTestResult(busyRunner, busyRunnerIndex);
            _completedJobs.Enqueue(testResult);
            _dispatchedJobs.TryRemove(busyRunnerIndex, out sink);
            Interlocked.Decrement(ref _busyRunnersCount);
            busyRunner.isBusy = false;
        }

        private static TestDescription RetrieveTestResult(TestRunnerHandler busyRunner, int busyRunnerIndex)
        {
            try
            {
                return (busyRunner.GetTestResult());
            }
            catch (IOException)
            {
                var testResult = _dispatchedJobs[busyRunnerIndex];
                testResult.TestsPass = false;
                return (testResult);
            }
        }

        private static void RunnerRestart(TestRunnerHandler busyRunner, int busyRunnerIndex)
        {
            busyRunner.KillTestRunner();
            _testRunners[busyRunnerIndex] = new TestRunnerHandler(_oneTimeRunners, _killTimeFactor);
        }

        #endregion

        #region CommandsMethods

        private static bool Stop()
        {
            foreach (var tr in _testRunners)
                tr.KillTestRunner();

            _shouldStop = true;
            return true;
        }

        #endregion

        #region BackboneTasksInit

        private static void InitReceiver()
        {
            _receiverTask = new Task(ReceivingLoop);
            _receiverTask.Start();
        }

        private static void InitSender()
        {
            _senderTask = new Task(SendingLoop);
            _senderTask.Start();
        }

        private static void InitCmdReceiver()
        {
            _cmdTask = new Task(CmdLoop);
            _cmdTask.Start();
        }

        private static void InstantiateTestRunners(int numRunners)
        {
            for (int i = 0; i < numRunners; i++)
                _testRunners.Add(new TestRunnerHandler(_oneTimeRunners, _killTimeFactor));
        }

        #endregion
    }
}
