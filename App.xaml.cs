using System;
using System.Windows;

namespace WinRemoteSharp
{
    public partial class App : System.Windows.Application
    {
        private TrayManager _trayManager;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 检查命令行参数
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
                // 无头模式：不创建 UI，直接运行 HeadlessRunner
                return;
            }

            // 创建主窗口
            var mainWindow = new MainWindow();

            // 创建托盘管理器
            _trayManager = new TrayManager(mainWindow);
            mainWindow.SetTrayManager(_trayManager);

            // 如果指定了最小化启动，不显示窗口
            if (!startMinimized)
            {
                mainWindow.Show();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayManager?.Dispose();
            base.OnExit(e);
        }
    }
}