using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using NinjaTurtlesMutation.AppDomainIsolation;
using NinjaTurtlesMutation.AppDomainIsolation.Adaptor;
using NinjaTurtlesMutation.ServiceTestRunnerLib;
using NinjaTurtlesMutation.ServiceTestRunnerLib.Utilities;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NinjaTurtlesMutation.Runner
{
    class Program
    {
        private static float _killTimeFactor;

        static void Main(string[] args)
        {
            if (args.Length != 4)
                return;
            bool oneRunOnly = args[2] == true.ToString();
            _killTimeFactor = float.Parse(args[3]);
            AppDomain.CurrentDomain.UnhandledException += UnexpectedExceptionHandler;
            var cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;
            var channelInId = args[0];
            var channelOutId = args[1];

            try
            {
                var factory = new ConnectionFactory() { HostName = "localhost" };
                using (var connection = factory.CreateConnection())
                {
                    using (var channel = connection.CreateModel())
                    {
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

                        var consumer = new EventingBasicConsumer(channel);
                        consumer.Received += (model, ea) =>
                        {
                            var body = ea.Body;
                            var message = Encoding.UTF8.GetString(body);
                            var testDescription = TestDescriptionExchanger.ReadATestDescription(message);

                            RunDescribedTests(testDescription);

                            TestDescriptionExchanger.SendATestDescription(channel, channelOutId, testDescription);

                            channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);

                            if (oneRunOnly)
                                cancellationTokenSource.Cancel();
                        };

                        channel.BasicConsume(queue: channelInId,
                                             noAck: false,
                                             consumer: consumer);

                        while (!cancellationToken.IsCancellationRequested)
                        {
                        }
                    }
                }
            }
            catch (IOException)
            {
                Environment.ExitCode = 1;
            }
            catch
            {
                Environment.ExitCode = 2;
            }
        }

        private static void UnexpectedExceptionHandler(object sender, UnhandledExceptionEventArgs args)
        {
            Environment.Exit(3);
        }

        private static void RunDescribedTests(TestDescription testDescription)
        {
            bool exitedInTime;
            int exitCode;

            using (Isolated<NunitManagedTestRunnerAdaptor> runner = new Isolated<NunitManagedTestRunnerAdaptor>())
            {
                var mutantPath = testDescription.AssemblyPath;
                runner.Instance.Start(mutantPath, testDescription.TestsToRun);
                exitedInTime = runner.Instance.WaitForExit((int)(_killTimeFactor * testDescription.TotalMsBench));
                exitCode = runner.Instance.ExitCode;
            }
            testDescription.ExitedInTime = exitedInTime;
            testDescription.TestsPass = (exitCode == 0);
        }
    }
}
