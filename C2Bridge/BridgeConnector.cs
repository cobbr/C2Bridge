using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace C2Bridge
{
    public class BridgeConnector
    {
        public class ReadBridgeArgs : EventArgs
        {
            public string Read { get; set; }
            public ReadBridgeArgs(string Read)
            {
                this.Read = Read;
            }
        }
        public event EventHandler<ReadBridgeArgs> OnReadBridge = delegate { };

        private IPAddress BridgeAddress { get; set; }
        private int BridgePort { get; set; }

        private readonly object _clientLock = new object();
        private TcpClient Client { get; set; }
        private NetworkStream BridgeStream { get; set; }

        private const int DISCONNECTED_DELAY_SECONDS = 5;

        public BridgeConnector(IPAddress Address, int Port, CancellationToken token)
        {
            this.BridgeAddress = Address;
            this.BridgePort = Port;
            this.Connect();
            _ = this.ReadBridgeAsync(token);
        }

        private async Task ReadBridgeAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    string read = await this.Read();
                    if (read != null)
                    {
                        this.OnReadBridge(this, new ReadBridgeArgs(read));
                    }
                }
                catch { }
            }
        }

        private void Connect()
        {
            this.Client = new TcpClient();
            this.Client.Connect(this.BridgeAddress, this.BridgePort);
            this.Client.ReceiveTimeout = 0;
            this.Client.SendTimeout = 0;
            this.BridgeStream = this.Client.GetStream();
            this.BridgeStream.ReadTimeout = Timeout.Infinite;
            this.BridgeStream.WriteTimeout = Timeout.Infinite;
        }

        public async Task<string> Read()
        {
            Task<string> t;
            lock (_clientLock)
            {
                t = Utilities.ReadStreamAsync(this.BridgeStream);
            }
            string read = await t;
            if (read == null)
            {
                Thread.Sleep(DISCONNECTED_DELAY_SECONDS * 1000);
                this.Connect();
            }
            return read;
        }

        public async Task Write(string data)
        {
            Task t;
            lock (_clientLock)
            {
                t = Utilities.WriteStreamAsync(this.BridgeStream, data);
            }
            await t;
        }
    }
}
