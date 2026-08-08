#nullable enable
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Forms;

namespace WinRemoteSharp
{
    public class TrayManager : IDisposable
    {
        private readonly MainWindow _mainWindow;
        private readonly Thread _trayThread;
        private readonly ManualResetEventSlim _trayReady = new(false);
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private System.Drawing.Bitmap? _trayIconBitmap;
        private System.Drawing.Icon? _iconOnline;
        private System.Drawing.Icon? _iconOffline;
        private bool _disposed;

        public TrayManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _trayThread = new Thread(RunTrayMessageLoop)
            {
                Name = "TrayIconThread",
                IsBackground = true,
            };
            _trayThread.SetApartmentState(ApartmentState.STA);
            _trayThread.Start();
            if (!_trayReady.Wait(TimeSpan.FromSeconds(5)))
                Debug.WriteLine("[TrayManager] Timeout");
        }

        private void RunTrayMessageLoop()
        {
            try
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                CreateTrayOnTrayThread();
                _trayReady.Set();
                System.Windows.Forms.Application.Run();
            }
            catch (Exception ex) { Debug.WriteLine($"[TrayManager] {ex}"); _trayReady.Set(); }
        }

        private void CreateTrayOnTrayThread()
        {
            try
            {
                _iconOnline = LoadTrayIcon("#FF00B894");
                _iconOffline = LoadTrayIcon("#FF5A6280");
                _notifyIcon = new System.Windows.Forms.NotifyIcon
                {
                    Icon = _iconOffline,
                    Text = "WinRemote Agent — 未连接",
                    Visible = true
                };
                _notifyIcon.DoubleClick += (s, e) => ShowWindow();
                _notifyIcon.ContextMenuStrip = BuildContextMenu();
            }
            catch (Exception ex) { Debug.WriteLine($"[TrayManager] Create: {ex}"); }
        }

        private System.Drawing.Icon LoadTrayIcon(string colorHex)
        {
            try
            {
                var c = ColorTranslator.FromHtml(colorHex);
                _trayIconBitmap?.Dispose();
                _trayIconBitmap = new System.Drawing.Bitmap(32, 32);
                using (var g = System.Drawing.Graphics.FromImage(_trayIconBitmap))
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var brush = new System.Drawing.SolidBrush(c))
                    {
                        var pts = new System.Drawing.Point[] { new(4,3), new(28,3), new(28,14), new(16,29), new(4,14) };
                        g.FillPolygon(brush, pts);
                    }
                    using (var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2f))
                    {
                        g.DrawLine(pen, 8, 9, 11, 19);
                        g.DrawLine(pen, 11, 19, 14, 13);
                        g.DrawLine(pen, 14, 13, 16, 21);
                        g.DrawLine(pen, 16, 21, 18, 13);
                        g.DrawLine(pen, 18, 13, 21, 19);
                        g.DrawLine(pen, 21, 19, 24, 9);
                    }
                }
                return System.Drawing.Icon.FromHandle(_trayIconBitmap.GetHicon());
            }
            catch { return System.Drawing.SystemIcons.Application; }
        }

        private System.Windows.Forms.ContextMenuStrip BuildContextMenu()
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Renderer = new DarkMenuRenderer();
            menu.Items.Add("📋 显示窗口", null, (s, e) => ShowWindow());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("🔗 连接服务器", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayConnect()));
            menu.Items.Add("⛓️‍💥 断开连接", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayDisconnect()));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            var svc = new System.Windows.Forms.ToolStripMenuItem("⚙️ 服务管理");
            svc.DropDownItems.Add("安装服务", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayInstallService()));
            svc.DropDownItems.Add("卸载服务", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayUninstallService()));
            svc.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
            svc.DropDownItems.Add("启动服务", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayStartService()));
            svc.DropDownItems.Add("停止服务", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayStopService()));
            menu.Items.Add(svc);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            var autoItem = new System.Windows.Forms.ToolStripMenuItem("🚀 开机自启");
            autoItem.CheckOnClick = true;
            autoItem.Checked = IsAutoStartEnabled();
            autoItem.Click += (s, e) => ToggleAutoStart(autoItem);
            menu.Items.Add(autoItem);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("📁 日志目录", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayOpenLogDir()));
            menu.Items.Add("ℹ️ 关于", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayCheckUpdate()));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("❌ 退出程序", null, (s, e) => ExitApplication());
            return menu;
        }

        private void InvokeOnMain(Action action)
        {
            try { _mainWindow.Dispatcher.Invoke(action); } catch { }
        }

        private void ShowWindow()
        {
            try
            {
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    _mainWindow.Show();
                    _mainWindow.WindowState = WindowState.Normal;
                    _mainWindow.Activate();
                });
            }
            catch { }
        }

        public void ShowBalloonTip(string title, string message, System.Windows.Forms.ToolTipIcon icon = System.Windows.Forms.ToolTipIcon.Info)
        {
            try { _notifyIcon?.ShowBalloonTip(3000, title, message, icon); } catch { }
        }

        public void UpdateConnectionStatus(bool connected)
        {
            try
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Icon = connected ? _iconOnline : _iconOffline;
                    _notifyIcon.Text = connected ? "WinRemote Agent — 已连接" : "WinRemote Agent — 未连接";
                }
            }
            catch { }
        }

        private bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                return key?.GetValue("WinRemoteAgent") != null;
            }
            catch { return false; }
        }

        private void ToggleAutoStart(System.Windows.Forms.ToolStripMenuItem item)
        {
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (item.Checked) key?.SetValue("WinRemoteAgent", $"\"{exePath}\" --hide");
                else key?.DeleteValue("WinRemoteAgent", false);
                InvokeOnMain(() => _mainWindow.AddLog($"开机自启已{(item.Checked ? "启用" : "禁用")}"));
            }
            catch (Exception ex) { item.Checked = !item.Checked; InvokeOnMain(() => _mainWindow.AddLog($"开机自启设置失败: {ex.Message}")); }
        }

        private void ExitApplication()
        {
            try
            {
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    _mainWindow._closingToTray = false;
                    ((App)System.Windows.Application.Current).ShutdownApp();
                });
            }
            catch { Environment.Exit(0); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_notifyIcon != null) { try { _notifyIcon.Visible = false; _notifyIcon.Dispose(); _notifyIcon = null; } catch { } }
            try { System.Windows.Forms.Application.Exit(); } catch { }
            if (_trayThread.IsAlive) _trayThread.Join(TimeSpan.FromSeconds(3));
            _trayIconBitmap?.Dispose();
            _trayReady.Dispose();
        }
    }

    internal class DarkMenuRenderer : System.Windows.Forms.ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }
        private class DarkColorTable : System.Windows.Forms.ProfessionalColorTable
        {
            public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(91, 94, 166);
            public override System.Drawing.Color MenuItemSelectedGradientBegin => System.Drawing.Color.FromArgb(91, 94, 166);
            public override System.Drawing.Color MenuItemSelectedGradientEnd => System.Drawing.Color.FromArgb(72, 75, 138);
            public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(30, 35, 55);
            public override System.Drawing.Color ImageMarginGradientBegin => System.Drawing.Color.FromArgb(30, 35, 55);
            public override System.Drawing.Color ImageMarginGradientMiddle => System.Drawing.Color.FromArgb(30, 35, 55);
            public override System.Drawing.Color ImageMarginGradientEnd => System.Drawing.Color.FromArgb(30, 35, 55);
            public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(45, 50, 70);
            public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.FromArgb(45, 50, 70);
            public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(45, 50, 70);
            public override System.Drawing.Color SeparatorLight => System.Drawing.Color.FromArgb(45, 50, 70);
        }
    }
}
