using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using WinRemoteSharp;
using WinRemoteSharp.Core;

namespace WinRemoteSharp
{
    public class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            var mode = "gui";
            string? popupTitle = null, popupMsg = null;

            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i].ToLowerInvariant();
                if (a == "--mode" && i + 1 < args.Length) mode = args[++i].ToLower();
                else if (a == "--title" && i + 1 < args.Length) popupTitle = args[++i];
                else if (a == "--msg" && i + 1 < args.Length) popupMsg = args[++i];
                else if (a == "--help" || a == "-h") { PrintHelp(); return 0; }
            }

            switch (mode)
            {
                case "gui":
                    return RunGui();
                case "agent":
                    return RunAgent().GetAwaiter().GetResult();
                case "popup":
                    return ShowPopup(popupTitle ?? "WinRemote", popupMsg ?? "Agent 运行正常");
                default:
                    Console.WriteLine($"未知模式: {mode}"); PrintHelp(); return 1;
            }
        }

        private static int RunGui()
        {
            var app = new App();
            app.InitializeComponent();
            return app.Run();
        }

        private static async Task<int> RunAgent()
        {
            Console.WriteLine("WinRemote Agent 模式 V1.2");
            var cfgMgr = new ConfigManager();
            var agent = new AgentClient(cfgMgr);
            agent.OnLog += (lvl, msg) => Console.WriteLine($"[{lvl.ToUpper()}] {msg}");
            await agent.StartAsync();
            Console.WriteLine("Agent 已启动，按 Ctrl+C 停止...");
            var tcs = new TaskCompletionSource();
            Console.CancelKeyPress += (_, __) => { tcs.SetResult(); };
            await tcs.Task;
            await agent.StopAsync();
            return 0;
        }

        private static int ShowPopup(string title, string msg)
        {
            try
            {
                var ps = $"Add-Type -AssemblyName System.Windows.Forms; " +
                         $"[System.Windows.Forms.MessageBox]::Show('{msg.Replace("'", "''")}','{title.Replace("'", "''")}','OK','Information') | Out-Null";
                var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -Command \"{ps}\"")
                { UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                p?.WaitForExit();
                return 0;
            }
            catch { return 1; }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("WinRemote Agent V1.2");
            Console.WriteLine("用法: WinRemoteAgent.exe [--mode gui|agent|popup] [--title T] [--msg M]");
        }
    }
}
