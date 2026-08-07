#nullable enable
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
    /// 系统托盘管理器 - 纯 Win32 Shell_NotifyIcon 实现，彻底绕过 WPF/WinForms 互操作问题。
    /// </summary>
    public class TrayManager : IDisposable
    {
        private readonly MainWindow _mainWindow;
        private System.Drawing.Bitmap _trayIconBitmap;
        private IntPtr _hicon = IntPtr.Zero;
        private uint _taskbarRestartMessage;
        private bool _added;
        private bool _disposed;

        // Win32 常量
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_USER = 0x0400;
        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;
        private const int NIM_SETVERSION = 0x00000004;
        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;
        private const int NIF_INFO = 0x00000010;
        private const int NIIF_INFO = 0x00000001;
        private const int NOTIFYICON_VERSION_4 = 4;

        // 自定义托盘回调消息 ID
        private const int WM_TRAYICON = WM_USER + 0x100;

        // 右键菜单命令 ID
        private const int CMD_SHOW = 1001;
        private const int CMD_CONNECT = 1002;
        private const int CMD_DISCONNECT = 1003;
        private const int CMD_SVC_INSTALL = 1004;
        private const int CMD_SVC_UNINSTALL = 1005;
        private const int CMD_SVC_START = 1006;
        private const int CMD_SVC_STOP = 1007;
        private const int CMD_SVC_STATUS = 1008;
        private const int CMD_AUTOSTART = 1009;
        private const int CMD_ABOUT = 1010;
        private const int CMD_LOGS = 1011;
        private const int CMD_LOGDIR = 1012;
        private const int CMD_EXIT = 1013;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr CreateWindowEx(
            uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetSubMenu(IntPtr hMenu, int nPos);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool InsertMenu(IntPtr hMenu, uint uPosition, uint uFlags, uint uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern bool SetMenuDefaultItem(IntPtr hMenu, uint uItem, uint fByPos);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private const uint MF_STRING = 0x00000000;
        private const uint MF_SEPARATOR = 0x00000800;
        private const uint MF_DEFAULT = 0x00001000;
        private const uint MF_CHECKED = 0x00000008;
        private const uint MF_UNCHECKED = 0x00000000;
        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const uint TPM_BOTTOMALIGN = 0x0020;
        private const uint TPM_LEFTALIGN = 0x0000;

        private IntPtr _msgWindow = IntPtr.Zero;
        private System.Windows.Interop.HwndSource _hwndSource;
        private bool _autoStartEnabled;

        public TrayManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _autoStartEnabled = IsAutoStartEnabled();

            _mainWindow.Dispatcher.Invoke(() =>
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(_mainWindow);
                helper.EnsureHandle();
                _hwndSource = System.Windows.Interop.HwndSource.FromHwnd(helper.Handle);
                if (_hwndSource != null)
                {
                    _hwndSource.AddHook(WndProc);
                }

                _hicon = CreateTrayIconHandle();
                AddTrayIcon();
            });
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_TRAYICON)
            {
                int lParamLow = (int)lParam & 0xFFFF;
                if (lParamLow == WM_LBUTTONDBLCLK)
                {
                    ToggleWindow();
                }
                else if (lParamLow == WM_RBUTTONUP)
                {
                    ShowContextMenu();
                }
                handled = true;
            }
            else if (msg == WM_USER + 0x101)
            {
                if (_added)
                {
                    RemoveTrayIcon();
                }
                AddTrayIcon();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private IntPtr CreateTrayIconHandle()
        {
            Stream? stream = null;
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                stream = assembly.GetManifestResourceStream("WinRemoteSharp.Resources.DialogIcon.png");
            }
            catch { }

            if (stream == null)
            {
                try
                {
                    var uri = new Uri("pack://application:,,,/Resources/DialogIcon.png");
                    var si = System.Windows.Application.GetResourceStream(uri);
                    stream = si?.Stream;
                }
                catch { }
            }

            if (stream != null)
            {
                try
                {
                    _trayIconBitmap = new System.Drawing.Bitmap(stream);
                    return _trayIconBitmap.GetHicon();
                }
                catch { }
                finally { stream.Dispose(); }
            }

            try
            {
                _trayIconBitmap = new System.Drawing.Bitmap(32, 32);
                using (var g = System.Drawing.Graphics.FromImage(_trayIconBitmap))
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(46, 139, 87)))
                    using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(34, 139, 34), 2))
                    {
                        var pts = new System.Drawing.Point[] {
                            new(4, 4), new(27, 4), new(27, 14),
                            new(16, 28), new(4, 14)
                        };
                        g.FillPolygon(brush, pts);
                        g.DrawPolygon(pen, pts);
                    }
                }
                return _trayIconBitmap.GetHicon();
            }
            catch { }

            return SystemIcons.Application.Handle;
        }

        private void AddTrayIcon()
        {
            if (_hwndSource == null) return;
            var hwnd = _hwndSource.Handle;
            if (hwnd == IntPtr.Zero) return;

            var nid = new NOTIFYICONDATA();
            nid.cbSize = Marshal.SizeOf(nid);
            nid.hWnd = hwnd;
            nid.uID = 1;
            nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            nid.uCallbackMessage = WM_TRAYICON;
            nid.hIcon = _hicon;
            nid.szTip = "WinRemote Agent";

            Shell_NotifyIcon(NIM_ADD, ref nid);

            nid.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
            Shell_NotifyIcon(NIM_SETVERSION, ref nid);

            _added = true;
            Debug.WriteLine("[TrayManager] Shell_NotifyIcon NIM_ADD succeeded");
        }

        private void RemoveTrayIcon()
        {
            if (_hwndSource == null || !_added) return;
            var hwnd = _hwndSource.Handle;
            if (hwnd == IntPtr.Zero) return;

            var nid = new NOTIFYICONDATA();
            nid.cbSize = Marshal.SizeOf(nid);
            nid.hWnd = hwnd;
            nid.uID = 1;
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _added = false;
        }

        private void ShowContextMenu()
        {
            var hMenu = CreatePopupMenu();

            AppendMenu(hMenu, MF_STRING | MF_DEFAULT, CMD_SHOW, "显示窗口");
            AppendMenu(hMenu, MF_SEPARATOR, 0, "");
            AppendMenu(hMenu, MF_STRING, CMD_CONNECT, "连接服务器");
            AppendMenu(hMenu, MF_STRING, CMD_DISCONNECT, "断开连接");
            AppendMenu(hMenu, MF_SEPARATOR, 0, "");

            var hSvcMenu = CreatePopupMenu();
            AppendMenu(hSvcMenu, MF_STRING, CMD_SVC_INSTALL, "安装服务");
            AppendMenu(hSvcMenu, MF_STRING, CMD_SVC_UNINSTALL, "卸载服务");
            AppendMenu(hSvcMenu, MF_SEPARATOR, 0, "");
            AppendMenu(hSvcMenu, MF_STRING, CMD_SVC_START, "启动服务");
            AppendMenu(hSvcMenu, MF_STRING, CMD_SVC_STOP, "停止服务");
            AppendMenu(hSvcMenu, MF_STRING, CMD_SVC_STATUS, "查看状态");
            AppendMenu(hMenu, MF_STRING, (uint)hSvcMenu, "服务管理");

            AppendMenu(hMenu, MF_SEPARATOR, 0, "");
            AppendMenu(hMenu, MF_STRING | (_autoStartEnabled ? MF_CHECKED : MF_UNCHECKED), CMD_AUTOSTART, "开机自启");
            AppendMenu(hMenu, MF_SEPARATOR, 0, "");
            AppendMenu(hMenu, MF_STRING, CMD_ABOUT, "关于");
            AppendMenu(hMenu, MF_STRING, CMD_LOGS, "刷新日志");
            AppendMenu(hMenu, MF_STRING, CMD_LOGDIR, "打开日志目录");
            AppendMenu(hMenu, MF_SEPARATOR, 0, "");
            AppendMenu(hMenu, MF_STRING, CMD_EXIT, "退出");

            GetCursorPos(out POINT pt);
            SetForegroundWindow(_hwndSource!.Handle);

            int cmd = TrackPopupMenu(hMenu, TPM_RIGHTBUTTON | TPM_BOTTOMALIGN | TPM_LEFTALIGN,
                pt.X, pt.Y, 0, _hwndSource!.Handle, IntPtr.Zero);

            DestroyMenu(hMenu);
            DestroyMenu(hSvcMenu);

            if (cmd > 0) HandleMenuCommand(cmd);
        }

        private void HandleMenuCommand(int cmd)
        {
            switch (cmd)
            {
                case CMD_SHOW: ToggleWindow(); break;
                case CMD_CONNECT: _mainWindow.TrayConnect(); break;
                case CMD_DISCONNECT: _mainWindow.TrayDisconnect(); break;
                case CMD_SVC_INSTALL: _mainWindow.TrayInstallService(); break;
                case CMD_SVC_UNINSTALL: _mainWindow.TrayUninstallService(); break;
                case CMD_SVC_START: _mainWindow.TrayStartService(); break;
                case CMD_SVC_STOP: _mainWindow.TrayStopService(); break;
                case CMD_SVC_STATUS: _mainWindow.TrayServiceStatus(); break;
                case CMD_AUTOSTART:
                    _autoStartEnabled = !_autoStartEnabled;
                    ToggleAutoStart(_autoStartEnabled);
                    break;
                case CMD_ABOUT: _mainWindow.TrayCheckUpdate(); break;
                case CMD_LOGS: _mainWindow.TrayRefreshLogs(); break;
                case CMD_LOGDIR: _mainWindow.TrayOpenLogDir(); break;
                case CMD_EXIT: ExitApplication(); break;
            }
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

        public void ShowBalloonTip(string title, string message)
        {
            if (_hwndSource == null || !_added) return;
            var nid = new NOTIFYICONDATA();
            nid.cbSize = Marshal.SizeOf(nid);
            nid.hWnd = _hwndSource.Handle;
            nid.uID = 1;
            nid.uFlags = NIF_INFO;
            nid.szInfoTitle = title;
            nid.szInfo = message;
            nid.dwInfoFlags = NIIF_INFO;
            nid.uTimeoutOrVersion = 3000;
            Shell_NotifyIcon(NIM_MODIFY, ref nid);
        }

        public void UpdateConnectionStatus(bool connected)
        {
            if (_hwndSource == null || !_added) return;
            var nid = new NOTIFYICONDATA();
            nid.cbSize = Marshal.SizeOf(nid);
            nid.hWnd = _hwndSource.Handle;
            nid.uID = 1;
            nid.uFlags = NIF_TIP;
            nid.szTip = connected ? "WinRemote Agent - 已连接" : "WinRemote Agent - 未连接";
            Shell_NotifyIcon(NIM_MODIFY, ref nid);
        }

        private bool IsAutoStartEnabled()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    return key?.GetValue("WinRemoteAgent") != null;
                }
            }
            catch { return false; }
        }

        private void ToggleAutoStart(bool enable)
        {
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;

                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (enable)
                        key?.SetValue("WinRemoteAgent", $"\"{exePath}\" --hide");
                    else
                        key?.DeleteValue("WinRemoteAgent", false);
                }
                _mainWindow.Dispatcher.Invoke(() =>
                    _mainWindow.AddLog($"开机自启已{(enable ? "启用" : "禁用")}"));
            }
            catch (Exception ex)
            {
                _autoStartEnabled = !enable;
                _mainWindow.Dispatcher.Invoke(() =>
                    _mainWindow.AddLog($"设置开机自启失败: {ex.Message}"));
            }
        }

        private void ExitApplication()
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
            _disposed = true;

            RemoveTrayIcon();

            if (_trayIconBitmap != null)
            {
                _trayIconBitmap.Dispose();
                _trayIconBitmap = null!;
            }

            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null!;
            }
        }
    }
}
