using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace WinRemoteSharp
{
    public partial class App : System.Windows.Application
    {
        private TrayManager _trayManager;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 全局未捕获异常 -> 落地日志
            AppDomain.CurrentDomain.UnhandledException += (s, args) => LogException(args.ExceptionObject as Exception, "AppDomain");
            DispatcherUnhandledException += (s, args) => { LogException(args.Exception, "Dispatcher"); args.Handled = true; };
            TaskScheduler.UnobservedTaskException += (s, args) => { LogException(args.Exception, "TaskScheduler"); args.SetObserved(); };

            // 检查命令行参数（必须在 base.OnStartup 之前，因为 XAML StartupUri 可能自动建窗）
            bool startMinimized = false;
            bool headless = false;

            foreach (string arg in e.Args)
            {
                string a = arg.ToLowerInvariant();
                if (a == "--minimized" || a == "-m")
                    startMinimized = true;
                else if (a == "--headless")
                    headless = true;
            }

            if (headless)
            {
                // 无头模式：不调用 base.OnStartup（绕过 XAML StartupUri 自动建窗），
                // 设置 OnExplicitShutdown 防止 WPF 在没有窗口时自动退出。
                this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                HeadlessRunner.RunAsync(e.Args).GetAwaiter().GetResult();
                this.Shutdown();
                return;
            }

            // GUI 模式：先让 base.OnStartup 按 XAML StartupUri 创建 MainWindow
            base.OnStartup(e);

            // 获取 XAML 自动创建的 MainWindow（不要手动 new，否则会创建两个）
            var mainWindow = (MainWindow)this.MainWindow;

            // 创建托盘管理器
            _trayManager = new TrayManager(mainWindow);
            mainWindow.SetTrayManager(_trayManager);

            // 如果指定了最小化启动，隐藏窗口
            if (startMinimized)
            {
                mainWindow.Hide();
            }
        }

        private void LogException(Exception ex, string source)
        {
            if (ex == null) return;
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_crash.log");
                string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string line = "[" + time + "] [" + source + "]\n" + ex.ToString() + "\n\n";
                File.AppendAllText(path, line);
            }
            catch { /* ignore */ }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayManager?.Dispose();
            base.OnExit(e);
        }
    }
}
