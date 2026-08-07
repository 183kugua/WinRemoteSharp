using System;
using System.Threading.Tasks;
using WinRemoteSharp.Core;

namespace WinRemoteSharp
{
    // NOTE: No Main() here! App.xaml is the WPF entry point (StartupUri="MainWindow.xaml").
    // This class is ONLY used when launched with --headless or --agent mode.
    // WPF will NOT generate a Main() conflict because we don't define one here.

    public static class HeadlessRunner
    {
        public static async Task<int> RunAsync(string[] args)
        {
            string serverUrl = "ws://127.0.0.1:6190/winremote";
            string token = "";
            string configPath = "config.json";
            bool installService = false;
            bool uninstallService = false;
            bool startService = false;
            bool stopService = false;
            bool showStatus = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--url" && i + 1 < args.Length) serverUrl = args[++i];
                else if (a == "--token" && i + 1 < args.Length) token = args[++i];
                else if (a == "--config" && i + 1 < args.Length) configPath = args[++i];
                else if (a == "--install-service") installService = true;
                else if (a == "--uninstall-service") uninstallService = true;
                else if (a == "--start-service") startService = true;
                else if (a == "--stop-service") stopService = true;
                else if (a == "--status") showStatus = true;
            }

            var sm = new Core.ServiceManager();

            if (installService)
            {
                Console.WriteLine(sm.Install());
                return 0;
            }
            if (uninstallService)
            {
                Console.WriteLine(sm.Uninstall());
                return 0;
            }
            if (startService)
            {
                Console.WriteLine(sm.Start());
                return 0;
            }
            if (stopService)
            {
                Console.WriteLine(sm.Stop());
                return 0;
            }
            if (showStatus)
            {
                var (running, state) = sm.GetServiceState();
                Console.WriteLine($"Status: {state} (running={running})");
                return 0;
            }

            // Default: run agent headless
            var agentConfig = new AgentConfig
            {
                ServerUrl = serverUrl,
                Token = token,
                AgentId = Environment.MachineName,
                HeartbeatIntervalSec = 30,
                CommandTimeoutSec = 30,
                EnableKeyboard = true,
                EnableMouse = true
            };

            var client = new Core.AgentClient(agentConfig);
            client.OnLog += (msg) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
            client.OnConnectionChanged += (connected) =>
                Console.WriteLine($"[Agent] Connection: {(connected ? "connected" : "disconnected")}");

            await client.ConnectWithRetryAsync();
            await Task.Delay(-1); // run forever
            return 0;
        }
    }
}