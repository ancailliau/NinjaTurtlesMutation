using System.IO;
using System;
using RabbitMQ.Client;
using System.Text;

namespace NinjaTurtlesMutation.ServiceTestRunnerLib.Utilities
{

    public static class TestDescriptionExchanger
    {
        private const string TRANSFER_START = "STARTSYNC";
        private const string TRANSFER_STOP = "ENDSYNC";

		public static void SendATestDescription(IModel channel, string channelId, TestDescription testDescription)
		{
			if (channel == null) throw new ArgumentNullException(nameof(channel));

			SendData(channel, channelId, XmlProcessing.SerializeToXml(testDescription));
        }

        public static void SendData(IModel channel, string channelId, string data)
        {
			var body = Encoding.UTF8.GetBytes(data);
			channel.BasicPublish(exchange: "", routingKey: channelId, basicProperties: null, body: body);
        }

        public static TestDescription ReadATestDescription(string data)
        {
            //string lineBuf = "";
            //string data = "";
            //while (lineBuf != null && lineBuf != TRANSFER_START)
            //    lineBuf = sr.ReadLine();
            //if (lineBuf == null)
            //    throw new IOException();
            //while (true)
            //{
            //    lineBuf = sr.ReadLine();
            //    if (lineBuf == null || lineBuf == TRANSFER_STOP)
            //        break;
            //    data += lineBuf;
            //}
            //if (lineBuf == null)
            //    throw new IOException();
            return XmlProcessing.DeserializeFromXml<TestDescription>(data);
        }
    }
}
