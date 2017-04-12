using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NinjaTurtlesMutation.AppDomainIsolation;
using NinjaTurtlesMutation.AppDomainIsolation.Adaptor;
using NinjaTurtlesMutation.ServiceTestRunnerLib;
using NinjaTurtlesMutation.ServiceTestRunnerLib.Utilities;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NinjaTurtlesMutation.Benchmarker
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 2)
                Environment.Exit(1);

            var factory = new ConnectionFactory() { HostName = "localhost" };
            using (var connection = factory.CreateConnection())
            {
                using (var channel = connection.CreateModel())
                {
                    var channelInId = args[0];
                    var channelOutId = args[1];

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

                    using (new ErrorModeContext(ErrorModes.FailCriticalErrors | ErrorModes.NoGpFaultErrorBox))
                    {
                        try
                        {
                            var consumer = new EventingBasicConsumer(channel);
                            consumer.Received += (model, ea) =>
                            {
                                var body = ea.Body;
                                var message = Encoding.UTF8.GetString(body);
                                var testDescription = TestDescriptionExchanger.ReadATestDescription(message);
                                RunDescribedTests(testDescription);
                                TestDescriptionExchanger.SendATestDescription(channel, channelOutId, testDescription);

                            };
                            channel.BasicConsume(queue: channelInId,
                                                 noAck: true,
                                                 consumer: consumer);
                        }
                        catch (Exception)
                        {
                            Environment.Exit(2);
                        }
                    }

                }
            }

        }

        private static void RunDescribedTests(TestDescription testDescription)
        {
            if (testDescription.TestsToRun.Count == 0)
                RunAllTests(testDescription);
            else
                RunSpecifiedTests(testDescription);
        }

        private static void RunAllTests(TestDescription testDescription)
        {
            int exitCode;
            using (Isolated<NunitManagedTestRunnerAdaptor> runner = new Isolated<NunitManagedTestRunnerAdaptor>())
            {
                var mutantPath = testDescription.AssemblyPath;
                var watch = Stopwatch.StartNew();
                runner.Instance.Start(mutantPath);
                runner.Instance.WaitForExit();
                watch.Stop();
                testDescription.TotalMsBench = watch.ElapsedMilliseconds;
                exitCode = runner.Instance.ExitCode;
            }
            testDescription.TestsPass = (exitCode == 0);
        }

        private static void RunSpecifiedTests(TestDescription testDescription)
        {
            int exitCode;
            using (Isolated<NunitManagedTestRunnerAdaptor> runner = new Isolated<NunitManagedTestRunnerAdaptor>())
            {
                var mutantPath = testDescription.AssemblyPath;
                var watch = Stopwatch.StartNew();
                runner.Instance.Start(mutantPath, testDescription.TestsToRun);
                runner.Instance.WaitForExit();
                watch.Stop();
                testDescription.TotalMsBench = watch.ElapsedMilliseconds;
                exitCode = runner.Instance.ExitCode;
            }
            testDescription.TestsPass = (exitCode == 0);
        }
    }
}
