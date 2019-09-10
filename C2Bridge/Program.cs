using System;
using System.Net;
using System.Linq;
using System.Threading;
using System.Collections.Generic;

namespace C2Bridge
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                BridgeProfile profile = GetProfile(ref args);
                if (profile == null)
                {
                    PrintUsage();
                    return;
                }
                if (args.Length < 2 || !IPAddress.TryParse(args[0], out IPAddress BridgeAddress) || !int.TryParse(args[1], out int BridgePort))
                {
                    PrintUsage();
                    return;
                }
                args = args.Skip(2).ToArray();
                using (CancellationTokenSource source = new CancellationTokenSource())
                {
                    BridgeConnector connector = new BridgeConnector(BridgeAddress, BridgePort, source.Token);
                    IC2Bridge bridge = new TcpC2Bridge(connector, profile, args);
                    bridge.RunAsync(source.Token).Wait(source.Token);
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"C2Bridge Exception: {e.Message}{Environment.NewLine}{e.StackTrace}");
            }
        }

        private static void PrintUsage()
        {
            Console.Error.Write("Usage: C2Bridge <bridge_address> <bridge_port> [ --profile <bridge_profile> ] <c2_bridge_args>");
        }

        private static BridgeProfile GetProfile(ref string[] args)
        {
            BridgeProfile profile = new BridgeProfile();
            if (args.Contains("--profile"))
            {
                List<string> argList = args.ToList();
                int profIndex = argList.IndexOf("--profile");
                if (args.Length < profIndex)
                {
                    return null;
                }
                try
                {
                    profile = BridgeProfile.Create(args[profIndex + 1]);
                }
                catch
                {
                    return null;
                }
                argList.Remove(args[profIndex]);
                argList.Remove(args[profIndex + 1]);
                args = argList.ToArray();
            }
            return profile;
        }
    }
}
