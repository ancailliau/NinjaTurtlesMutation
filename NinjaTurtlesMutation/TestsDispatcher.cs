using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using NinjaTurtlesMutation.ServiceTestRunnerLib;
using NinjaTurtlesMutation.ServiceTestRunnerLib.Utilities;
using System.Threading.Tasks;
using System.Threading;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;

namespace NinjaTurtlesMutation
{
    public class TestsDispatcher : IDisposable
    {
        private const string DISPATCHER_NAME = "NTMDispatcher.exe";
        private const int EXIT_MAXWAIT = 1000;

        #region Properties

        private readonly Process _coreProcess;

        #region Tests Exchange

        private long _testsSended;
        private long _testsReceived;

        public bool TestsPending {
            get { return _testsSended > _testsReceived; }
        }

        #endregion

        #endregion

        BlockingCollection<string> Messages = new BlockingCollection<string>();
        string channelInId = "ntmdispatcher-in";
        string channelOutId = "ntmdispatcher-out";
        string channelCmdId = "ntmdispatcher-cmd";
        IConnection connection;
        IModel channel;

        public TestsDispatcher(int parallelLevel, int maxBusyRunners, bool oneTimeRunners, float killTimeFactor)
        {
            var factory = new ConnectionFactory() { HostName = "localhost" };

            connection = factory.CreateConnection();
            channel = connection.CreateModel();

            channel.QueueDeclare(queue: channelInId,
                             durable: false,
                             exclusive: false,
                             autoDelete: false,
                             arguments: null);

            channel.QueueDeclare(queue: channelOutId,
                     durable: false,
                     exclusive: false,
                     autoDelete: false,
                     arguments: null);

            channel.QueueDeclare(queue: channelCmdId,
                     durable: false,
                     exclusive: false,
                     autoDelete: false,
                     arguments: null);

            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += (model, ea) =>
            {
                var body = ea.Body;
                var message = System.Text.Encoding.UTF8.GetString(body);
                Messages.Add(message);
            };
            channel.BasicConsume(queue: channelInId,
                                 noAck: true,
                                 consumer: consumer);


            _coreProcess = new Process
            {
                StartInfo =
                {
                    FileName = DISPATCHER_NAME,
                    UseShellExecute = false,
                    Arguments = channelOutId + " " +
                                channelInId + " " +
                                channelCmdId + " " +
                                parallelLevel + " " +
                                maxBusyRunners + " " +
                                oneTimeRunners + " " +
                                killTimeFactor
                }
            };
            _coreProcess.Start();
        }

        #region Tests Exchange Methods

        public void SendTest(TestDescription test)
        {
            TestDescriptionExchanger.SendATestDescription(channel, channelOutId, test);
            _testsSended++;
        }

        public TestDescription ReadATest()
        {
            string message = Messages.Take();
            var test = TestDescriptionExchanger.ReadATestDescription(message);
            _testsReceived++;
            return test;
        }

        #endregion

        #region IDisposable method

        public void Dispose()
        {
            CommandExchanger.SendData(channel, channelCmdId, CommandExchanger.Commands.STOP);
            connection.Dispose();
            channel.Dispose();
            _coreProcess.WaitForExit(EXIT_MAXWAIT);
        }

        #endregion
    }
}
