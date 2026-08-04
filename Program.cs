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
            string serverUrl = "ws://127.0.0.1:8000/ws/winremote";
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

            var config = ConfigManager.Load(configPath);
            if (!string.IsNullOrEmpty(token)) config.Token = token;
            if (!string.IsNullOrEmpty(serverUrl)) config.ServerUrl = serverUrl;

            if (installService)
            {
                var sm = new ServiceManager(configPath);
                return sm.Install() ? 0 : 1;
            }
            if (uninstallService)
            {
                var sm = new ServiceManager(configPath);
                return sm.Uninstall() ? 0 : 1;
            }
            if (startService)
            {
                var sm = new ServiceManager(configPath);
                return sm.Start() ? 0 : 1;
            }
            if (stopService)
            {
                var sm = new ServiceManager(configPath);
                return sm.Stop() ? 0 : 1;
            }
            if (showStatus)
            {
                var sm = new ServiceManager(configPath);
                var status = sm.GetStatus();
                Console.WriteLine($"Status: {status}");
                return 0;
            }

            // Default: run agent headless
            var client = new AgentClient(config);
            await client.ConnectAsync(serverUrl, token);
            await Task.Delay(-1); // run forever
            return 0;
        }
    }
}
