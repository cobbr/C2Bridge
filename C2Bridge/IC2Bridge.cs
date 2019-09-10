using System;
using System.Threading;
using System.Threading.Tasks;

namespace C2Bridge
{
    public interface IC2Bridge
    {
        Task RunAsync(CancellationToken token);
    }

    public abstract class C2Bridge : IC2Bridge
    {
        protected BridgeConnector BridgeConnector { get; set; }
        protected BridgeProfile BridgeProfile { get; set; }

        protected C2Bridge(BridgeConnector connector, BridgeProfile profile)
        {
            this.BridgeConnector = connector;
            this.BridgeProfile = profile;
        }

        public abstract Task RunAsync(CancellationToken token);
        protected abstract Task<string> Read(dynamic readobj);
        protected abstract Task Write(dynamic readobj, string data);
        protected abstract string GetBridgeMessengerCode();
    }
}
