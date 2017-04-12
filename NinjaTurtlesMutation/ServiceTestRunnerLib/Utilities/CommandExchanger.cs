using System;
using System.IO;
using RabbitMQ.Client;

namespace NinjaTurtlesMutation.ServiceTestRunnerLib.Utilities
{

    public static class CommandExchanger
    {
        private const string TRANSFER_START = "STARTSYNC";
        private const string TRANSFER_STOP = "ENDSYNC";

        public static class Commands
        {
            public const string STOP = "stop";
        }

        public static void SendData(IModel channel, string channelId, string cmd)
		{
			var body = System.Text.Encoding.UTF8.GetBytes(cmd);
			channel.BasicPublish(exchange: "", routingKey: channelId, basicProperties: null, body: body);

            //sw.WriteLine(TRANSFER_START);
            //sw.WriteLine(cmd);
            //sw.WriteLine(TRANSFER_STOP);
            //sw.Flush();
        }

        [ObsoleteAttribute("This method is obsolete.", false)]
		public static string ReadACommand(StreamReader sr)
        {
            string lineBuf = "";
            string cmd = "";
            while (lineBuf != null && lineBuf != TRANSFER_START)
                lineBuf = sr.ReadLine();
            if (lineBuf == null)
                throw new IOException();
            while (true)
            {
                lineBuf = sr.ReadLine();
                if (lineBuf == null || lineBuf == TRANSFER_STOP)
                    break;
                cmd += lineBuf;
            }
            if (lineBuf == null)
                throw new IOException();
            return cmd;
        }
    }
}
