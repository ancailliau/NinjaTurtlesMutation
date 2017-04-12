using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using NinjaTurtlesMutation.ServiceTestRunnerLib;
using NinjaTurtlesMutation.ServiceTestRunnerLib.Utilities;
using RabbitMQ.Client;
using System.Collections.Generic;
using RabbitMQ.Client.Events;
using System.Text;
using System.Collections.Concurrent;
using NLog;

namespace NinjaTurtlesMutation.Dispatcher
{
    internal class TestRunnerHandler
	{
		#region Logging

		private static readonly Logger _log = LogManager.GetCurrentClassLogger();

		#endregion
		
        private readonly Process _runnerProcess;

        public bool isBusy;

		IConnection connection;
		IModel channel;

		string channelInId;
		string channelOutId;

        BlockingCollection<TestDescription> Messages = new BlockingCollection<TestDescription>();

        public TestRunnerHandler(bool oneTimeRunner, float killTimeFactor)
        {
            var factory = new ConnectionFactory() { HostName = "localhost" };

            connection = factory.CreateConnection();
            channel = connection.CreateModel();

            channelInId = "ntmrunner-in";
            channelOutId = "ntmrunner-out";

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
                Messages.Add(TestDescriptionExchanger.ReadATestDescription(message));
			};
			channel.BasicConsume(queue: channelInId,
								 noAck: true,
								 consumer: consumer);
            _runnerProcess = new Process();
            _runnerProcess.StartInfo.FileName = "NTMRunner.exe";
            _runnerProcess.StartInfo.UseShellExecute = false;
            _runnerProcess.StartInfo.Arguments = channelOutId + " " +
                                                 channelInId + " " +
                                                 oneTimeRunner + " " +
                                                 killTimeFactor;
            _runnerProcess.Start();
            isBusy = false;

            _log.Info("New runner started.");
        }

        public void KillTestRunner()
        {
            try
            {
                _runnerProcess.Kill();
                channel.Dispose();
                connection.Dispose();
            }
            catch { }
        }

        public void SendJob(TestDescription job)
        {
            TestDescriptionExchanger.SendATestDescription(channel, channelOutId, job);
        }

        public bool IsAlive()
        {
            try
            {
                return (true);
            }
            catch (IOException)
            {
                return (false);
            }
        }

        public TestDescription GetTestResult()
        {
            return Messages.Take();
        }
    }
}
