using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;

namespace WinRemoteSharp
{
    public class TrayManager : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly MainWindow _mainWindow;
        private Bitmap _trayIconBitmap;
        private bool _disposed;

        public TrayManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _notifyIcon = new NotifyIcon { Visible = true, Text = "WinRemote Agent", Icon = CreateTrayIcon() };
            _contextMenu = new ContextMenuStrip();
            BuildMenu();
            _notifyIcon.ContextMenuStrip = _contextMenu;
            _notifyIcon.DoubleClick += (s, e) => ToggleWindow();
            _mainWindow.StateChanged += (s, e) => UpdateTooltip();
        }

        private Icon CreateTrayIcon()
        {
            try
            {
                var si = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Resources/DialogIcon.png"));
                if (si?.Stream != null)
                {
                    _trayIconBitmap = new Bitmap(si.Stream);
                    return Icon.FromHandle(_trayIconBitmap.GetHicon());
                }
            }
            catch { }

            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinRemoteSharp.Resources.DialogIcon.png"))
                {
                    if (s != null)
                    {
                        _trayIconBitmap = new Bitmap(s);
                        return Icon.FromHandle(_trayIconBitmap.GetHicon());
                    }
                }
            }
            catch { }

            return SystemIcons.Application;
        }

        private void BuildMenu()
        {
            _contextMenu.Items.Clear();

            var show = new ToolStripMenuItem("显示窗口");
            show.Click += (s, e) => ToggleWindow();
            _contextMenu.Items.Add(show);
            _contextMenu.Items.Add(new ToolStripSeparator());

            var conn = new ToolStripMenuItem("连接服务器");
            conn.Click += (s, e) => _mainWindow.TrayConnect();
            _contextMenu.Items.Add(conn);

            var disc = new ToolStripMenuItem("断开连接");
            disc.Click += (s, e) => _mainWindow.TrayDisconnect();
            _contextMenu.Items.Add(disc);
            _contextMenu.Items.Add(new ToolStripSeparator());

            var svc = new ToolStripMenuItem("服务管理");
            svc.DropDownItems.Add("安装服务", null, (s, e) => _mainWindow.TrayInstallService());
            svc.DropDownItems.Add("卸载服务", null, (s, e) => _mainWindow.TrayUninstallService());
            svc.DropDownItems.Add(new ToolStripSeparator());
            svc.DropDownItems.Add("启动服务", null, (s, e) => _mainWindow.TrayStartService());
            svc.DropDownItems.Add("停止服务", null, (s, e) => _mainWindow.TrayStopService());
            svc.DropDownItems.Add("查看状态", null, (s, e) => _mainWindow.TrayServiceStatus());
            _contextMenu.Items.Add(svc);
            _contextMenu.Items.Add(new ToolStripSeparator());

            var asItem = new ToolStripMenuItem("开机自启") { CheckOnClick = true, Checked = IsAutoStart() };
            asItem.Click += (s, e) => ToggleAutoStart(asItem);
            _contextMenu.Items.Add(asItem);
            _contextMenu.Items.Add(new ToolStripSeparator());

            var about = new ToolStripMenuItem("关于");
            about.Click += (s, e) => _mainWindow.TrayCheckUpdate();
            _contextMenu.Items.Add(about);

            var rlog = new ToolStripMenuItem("刷新日志");
            rlog.Click += (s, e) => _mainWindow.TrayRefreshLogs();
            _contextMenu.Items.Add(rlog);

            var odir = new ToolStripMenuItem("打开日志目录");
            odir.Click += (s, e) => _mainWindow.TrayOpenLogDir();
            _contextMenu.Items.Add(odir);
            _contextMenu.Items.Add(new ToolStripSeparator());

            var exit = new ToolStripMenuItem("退出");
            exit.Click += (s, e) => ExitApp();
            _contextMenu.Items.Add(exit);
        }

        private void ToggleWindow()
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                if (_mainWindow.Visibility == Visibility.Visible)
                    _mainWindow.Hide();
                else
                {
                    _mainWindow.Show();
                    _mainWindow.WindowState = WindowState.Normal;
                    _mainWindow.Activate();
                }
            });
        }

        private void UpdateTooltip()
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                _notifyIcon.Text = $"WinRemote Agent - {(_mainWindow.IsConnected ? "已连接" : "未连接")}";
            });
        }

        public void ShowBalloonTip(string title, string msg, ToolTipIcon icon = ToolTipIcon.Info)
        {
            _notifyIcon.ShowBalloonTip(3000, title, msg, icon);
        }

        public void UpdateConnectionStatus(bool c) => UpdateTooltip();

        private bool IsAutoStart()
        {
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                return k?.GetValue("WinRemoteAgent") != null;
            }
            catch { return false; }
        }

        private void ToggleAutoStart(ToolStripMenuItem item)
        {
            try
            {
                var exe = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exe)) return;
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (item.Checked)
                    k?.SetValue("WinRemoteAgent", $"\"{exe}\" --show");
                else
                    k?.DeleteValue("WinRemoteAgent", false);
            }
            catch { item.Checked = !item.Checked; }
        }

        private void ExitApp()
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                _mainWindow._closingToTray = false;
                System.Windows.Application.Current.Shutdown();
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _notifyIcon?.Dispose();
            _contextMenu?.Dispose();
            _trayIconBitmap?.Dispose();
            _disposed = true;
        }
    }
}
