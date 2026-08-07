using System;
using System.Threading.Tasks;
using WinRemoteSharp.Core;

namespace WinRemoteSharp
{
    public static class HeadlessRunner
    {
        public static async Task<int> RunAsync(string[] args)
        {
            string urlArg = "";
            string tokenArg = "";
            string configPath = "config.json";
            bool installService = false;
            bool uninstallService = false;
            bool startService = false;
            bool stopService = false;
            bool showStatus = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--url" && i + 1 < args.Length) urlArg = args[++i];
                else if (a == "--token" && i + 1 < args.Length) tokenArg = args[++i];
                else if (a == "--config" && i + 1 < args.Length) configPath = args[++i];
                else if (a == "--install-service") installService = true;
                else if (a == "--uninstall-service") uninstallService = true;
                else if (a == "--start-service") startService = true;
                else if (a == "--stop-service") stopService = true;
                else if (a == "--status") showStatus = true;
            }

            var sm = new Core.ServiceManager();

            if (installService) { Console.WriteLine(sm.Install()); return 0; }
            if (uninstallService) { Console.WriteLine(sm.Uninstall()); return 0; }
            if (startService) { Console.WriteLine(sm.Start()); return 0; }
            if (stopService) { Console.WriteLine(sm.Stop()); return 0; }
            if (showStatus)
            {
                var (running, state) = sm.GetServiceState();
                Console.WriteLine($"Status: {state} (running={running})");
                return 0;
            }

            var cfg = Core.ConfigManager.Load(configPath);

            string serverUrl = !string.IsNullOrEmpty(urlArg)
                ? urlArg
                : (!string.IsNullOrEmpty(cfg.ServerUrl) ? cfg.ServerUrl : "ws://127.0.0.1:6190/winremote");
            string token = !string.IsNullOrEmpty(tokenArg) ? tokenArg : cfg.Token;
            string agentId = !string.IsNullOrEmpty(cfg.AgentId) ? cfg.AgentId : Environment.MachineName;

            var agentConfig = new AgentConfig
            {
                ServerUrl = serverUrl,
                Token = token,
                AgentId = agentId,
                HeartbeatIntervalSec = cfg.HeartbeatInterval > 0 ? cfg.HeartbeatInterval : 30,
                CommandTimeoutSec = cfg.ConnectionTimeout > 0 ? cfg.ConnectionTimeout : 30,
                EnableKeyboard = true,
                EnableMouse = true,
                EnableFileWrite = cfg.AllowWrite,
                FileReadWhitelist = cfg.FileReadWhitelist ?? Array.Empty<string>(),
                ReconnectBaseDelaySec = 2,
                ReconnectMaxDelaySec = 60
            };

            var client = new Core.AgentClient(agentConfig);
            client.OnLog += (msg) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
            client.OnConnectionChanged += (connected) =>
                Console.WriteLine($"[Agent] Connection: {(connected ? "connected" : "disconnected")}");

            Console.WriteLine($"[Agent] id={agentId} server={serverUrl}");
            await client.ConnectWithRetryAsync();
            await Task.Delay(-1);
            return 0;
        }
    }
}
