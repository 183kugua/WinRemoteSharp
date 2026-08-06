using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;

namespace WinRemoteSharp
{
    /// <summary>
    /// 系统托盘管理器 - 支持最小化到托盘、右键菜单、开机自启
    /// </summary>
    public class TrayManager : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly MainWindow _mainWindow;
        private bool _disposed = false;

        // Win32 API for setting startup
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHGetFolderPath(IntPtr hwndOwner, int nFolder, IntPtr hToken, uint dwFlags, System.Text.StringBuilder lpszPath);

        private const int CSIDL_STARTUP = 7;

        public TrayManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;

            // 创建托盘图标
            _notifyIcon = new NotifyIcon
            {
                Visible = true,
                Text = "WinRemote Agent",
                Icon = CreateTrayIcon()
            };

            // 创建右键菜单
            _contextMenu = new ContextMenuStrip();
            BuildContextMenu();
            _notifyIcon.ContextMenuStrip = _contextMenu;

            // 双击显示/隐藏窗口
            _notifyIcon.DoubleClick += (s, e) => ToggleWindow();

            // 窗口状态改变时同步托盘提示
            _mainWindow.StateChanged += (s, e) => UpdateTrayTooltip();
        }

        private Icon CreateTrayIcon()
        {
            try
            {
                // 尝试从资源加载图标 - 使用 DialogIcon.png
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("WinRemoteSharp.Resources.DialogIcon.png"))
                {
                    if (stream != null)
                    {
                        // 将 PNG 转换为 Icon
                        using (var bitmap = new System.Drawing.Bitmap(stream))
                        {
                            return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
                        }
                    }
                }
            }
            catch { }

            // 回退：使用系统默认图标
            return SystemIcons.Application;
        }

        private void BuildContextMenu()
        {
            _contextMenu.Items.Clear();

            // 显示/隐藏主窗口
            var showHideItem = new ToolStripMenuItem("显示窗口");
            showHideItem.Click += (s, e) => ToggleWindow();
            _contextMenu.Items.Add(showHideItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // 连接/断开
            var connectItem = new ToolStripMenuItem("连接服务器");
            connectItem.Click += (s, e) => _mainWindow.TrayConnect();
            _contextMenu.Items.Add(connectItem);

            var disconnectItem = new ToolStripMenuItem("断开连接");
            disconnectItem.Click += (s, e) => _mainWindow.TrayDisconnect();
            _contextMenu.Items.Add(disconnectItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // 服务管理
            var serviceMenu = new ToolStripMenuItem("服务管理");
            serviceMenu.DropDownItems.Add("安装服务", null, (s, e) => _mainWindow.TrayInstallService());
            serviceMenu.DropDownItems.Add("卸载服务", null, (s, e) => _mainWindow.TrayUninstallService());
            serviceMenu.DropDownItems.Add(new ToolStripSeparator());
            serviceMenu.DropDownItems.Add("启动服务", null, (s, e) => _mainWindow.TrayStartService());
            serviceMenu.DropDownItems.Add("停止服务", null, (s, e) => _mainWindow.TrayStopService());
            serviceMenu.DropDownItems.Add("查看状态", null, (s, e) => _mainWindow.TrayServiceStatus());
            _contextMenu.Items.Add(serviceMenu);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // 开机自启
            var autoStartItem = new ToolStripMenuItem("开机自启");
            autoStartItem.CheckOnClick = true;
            autoStartItem.Checked = IsAutoStartEnabled();
            autoStartItem.Click += (s, e) => ToggleAutoStart(autoStartItem);
            _contextMenu.Items.Add(autoStartItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // 关于
            var aboutItem = new ToolStripMenuItem("关于");
            aboutItem.Click += (s, e) => _mainWindow.TrayCheckUpdate();
            _contextMenu.Items.Add(aboutItem);

            // 刷新日志
            var refreshLogsItem = new ToolStripMenuItem("刷新日志");
            refreshLogsItem.Click += (s, e) => _mainWindow.TrayRefreshLogs();
            _contextMenu.Items.Add(refreshLogsItem);

            // 打开日志目录
            var openLogDirItem = new ToolStripMenuItem("打开日志目录");
            openLogDirItem.Click += (s, e) => _mainWindow.TrayOpenLogDir();
            _contextMenu.Items.Add(openLogDirItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // 退出
            var exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += (s, e) => ExitApplication();
            _contextMenu.Items.Add(exitItem);
        }

        private void ToggleWindow()
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                if (_mainWindow.Visibility == Visibility.Visible)
                {
                    _mainWindow.Hide();
                }
                else
                {
                    _mainWindow.Show();
                    _mainWindow.WindowState = WindowState.Normal;
                    _mainWindow.Activate();
                }
            });
        }

        private void UpdateTrayTooltip()
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                string status = _mainWindow.IsConnected ? "已连接" : "未连接";
                _notifyIcon.Text = $"WinRemote Agent - {status}";
            });
        }

        public void ShowBalloonTip(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            _notifyIcon.ShowBalloonTip(3000, title, message, icon);
        }

        public void UpdateConnectionStatus(bool connected)
        {
            UpdateTrayTooltip();
        }

        private bool IsAutoStartEnabled()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    return key?.GetValue("WinRemoteAgent") != null;
                }
            }
            catch { return false; }
        }

        private void ToggleAutoStart(ToolStripMenuItem menuItem)
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (menuItem.Checked)
                    {
                        key?.SetValue("WinRemoteAgent", $"\"{exePath}\" --minimized");
                    }
                    else
                    {
                        key?.DeleteValue("WinRemoteAgent", false);
                    }
                }
                _mainWindow.Dispatcher.Invoke(() => _mainWindow.AddLog($"开机自启已{(menuItem.Checked ? "启用" : "禁用")}"));
            }
            catch (Exception ex)
            {
                menuItem.Checked = !menuItem.Checked; // 恢复原状
                _mainWindow.Dispatcher.Invoke(() => _mainWindow.AddLog($"设置开机自启失败: {ex.Message}"));
            }
        }

        private void ExitApplication()
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                _mainWindow._closingToTray = false; // 标记为真正退出
                System.Windows.Application.Current.Shutdown();
            });
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _notifyIcon?.Dispose();
                _contextMenu?.Dispose();
                _disposed = true;
            }
        }
    }
}