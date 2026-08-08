using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinRemoteSharp.Core;

namespace WinRemoteSharp
{
    public partial class MainWindow : Window
    {
        private Core.AgentClient _agent;
        private Core.Config _config;
        private Core.ServiceManager _svcMgr;
        private TrayManager _trayMgr;
        internal bool _closingToTray = true;

        public bool IsConnected => _agent?.IsConnected ?? false;

        public MainWindow()
        {
            InitializeComponent();
            try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Resources/App.ico")); } catch { }
            _config = Core.ConfigManager.Load();
            _svcMgr = new Core.ServiceManager();
            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        public void SetTrayManager(TrayManager tm) => _trayMgr = tm;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadSettingsToUI();
            RefreshSystemInfo();
            RefreshServiceStatus();
            AddLog("WinRemote Agent V1.2 已就绪 — 关闭窗口后托盘继续运行");
            SetNavActive(NavDashboard, "📊 主控台");
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_closingToTray)
            {
                e.Cancel = true;
                Hide();
                _trayMgr?.ShowBalloonTip("WinRemote Agent", "程序在后台运行中\n双击托盘图标恢复窗口");
            }
            else
            {
                _agent?.DisconnectAsync().GetAwaiter().GetResult();
                _svcMgr?.Dispose();
            }
        }

        // ===== 侧边栏导航 =====

        private void NavDashboard_Click(object sender, RoutedEventArgs e) => SetNavActive(NavDashboard, "📊 主控台");
        private void NavSettings_Click(object sender, RoutedEventArgs e) => SetNavActive(NavSettings, "⚙️ 设置");
        private void NavService_Click(object sender, RoutedEventArgs e) => SetNavActive(NavService, "🔧 系统服务");
        private void NavLogs_Click(object sender, RoutedEventArgs e) => SetNavActive(NavLogs, "📋 运行日志");
        private void NavToolbox_Click(object sender, RoutedEventArgs e) => SetNavActive(NavToolbox, "🛠️ 工具箱");

        private void SetNavActive(Button activeBtn, string title)
        {
            PageTitle.Text = title;
            var allBtns = new[] { NavDashboard, NavSettings, NavService, NavLogs, NavToolbox };
            foreach (var b in allBtns)
            {
                if (b == activeBtn)
                {
                    b.Background = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#10FFFFFF"));
                    b.Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush");
                }
                else
                {
                    b.Background = Brushes.Transparent;
                    b.Foreground = (SolidColorBrush)FindResource("TextSecondaryBrush");
                }
            }
            PanelDashboard.Visibility = activeBtn == NavDashboard ? Visibility.Visible : Visibility.Collapsed;
            PanelSettings.Visibility  = activeBtn == NavSettings  ? Visibility.Visible : Visibility.Collapsed;
            PanelService.Visibility   = activeBtn == NavService   ? Visibility.Visible : Visibility.Collapsed;
            PanelLogs.Visibility      = activeBtn == NavLogs      ? Visibility.Visible : Visibility.Collapsed;
            PanelToolbox.Visibility   = activeBtn == NavToolbox   ? Visibility.Visible : Visibility.Collapsed;
        }

        // ===== 日志 =====

        public void AddLog(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            Dispatcher.Invoke(() =>
            {
                TxtLog.Text = line;
                TxtFullLog.AppendText(line + Environment.NewLine);
                TxtFullLog.ScrollToEnd();
            });
        }

        private void UpdateStatusDot(bool connected)
        {
            StatusDot.Fill = connected
                ? new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF00B894"))
                : new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF5A6280"));
            StatusText.Text = connected ? "已连接" : "未连接";
            _trayMgr?.UpdateConnectionStatus(connected);
        }

        // ===== 连接 =====

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_agent?.IsConnected == true) { AddLog("已在运行"); return; }
            SyncUIToConfig();
            Core.ConfigManager.Save(_config);
            _agent = new Core.AgentClient(new Core.AgentConfig
            {
                ServerUrl = TxtServerUrl.Text.Trim(),
                Token = TxtToken.Password,
                AgentId = TxtAgentId.Text.Trim(),
                HeartbeatIntervalSec = Int(TxtHeartbeat.Text, 30),
                CommandTimeoutSec = Int(TxtShellTimeout.Text, 30),
                EnableKeyboard = true,
                EnableMouse = true,
                EnableFileWrite = ChkAllowWrite?.IsChecked ?? false,
                FileReadWhitelist = (TxtWhitelist.Text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries),
                ScreenshotFormat = "jpg",
                ReconnectBaseDelaySec = 2,
                ReconnectMaxDelaySec = 60
            });
            _agent.OnLog += AddLog;
            _agent.OnConnectionChanged += c => Dispatcher.Invoke(() => UpdateStatusDot(c));
            BtnConnect.IsEnabled = false;
            try { await Task.Run(() => _agent.ConnectWithRetryAsync()); }
            catch (Exception ex) { AddLog($"连接失败: {ex.Message}"); }
            BtnConnect.IsEnabled = true;
        }

        private async void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            if (_agent != null) { await _agent.DisconnectAsync(); _agent = null; AddLog("已停止"); }
        }

        // ===== 工具箱操作 =====

        private void BtnShell_Click(object sender, RoutedEventArgs e) => AskAndRun("CMD 命令", "cmd");
        private void BtnPowershell_Click(object sender, RoutedEventArgs e) => AskAndRun("PowerShell 命令", "powershell");

        private void AskAndRun(string title, string shell)
        {
            var dlg = new InputDialog(title, "请输入命令:") { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.InputText))
            {
                TxtTestCommand.Text = dlg.InputText;
                _ = RunCmdAsync(dlg.InputText, shell);
            }
        }

        private async Task RunCmdAsync(string cmd, string shell)
        {
            AddLog($"[{shell}] {cmd}");
            TxtTestResult.Text = "执行中...";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = shell == "powershell" ? "powershell.exe" : "cmd.exe",
                    Arguments = shell == "powershell" ? $"-NoProfile -Command \"{cmd}\"" : $"/c {cmd}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };
                using var p = Process.Start(psi);
                var t = Task.WhenAll(p.StandardOutput.ReadToEndAsync(), p.StandardError.ReadToEndAsync());
                if (await Task.WhenAny(t, Task.Delay(30000)) == t)
                    TxtTestResult.Text = string.IsNullOrEmpty(t.Result[1]) ? t.Result[0] : t.Result[0] + "\n[stderr] " + t.Result[1];
                else { try { p.Kill(); } catch { } TxtTestResult.Text = "[超时]"; }
                AddLog("命令执行完成");
            }
            catch (Exception ex) { TxtTestResult.Text = ex.Message; }
        }

        private void BtnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                using var bmp = new System.Drawing.Bitmap(b.Width, b.Height);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                    g.CopyFromScreen(b.Location, System.Drawing.Point.Empty, b.Size);
                var path = Path.Combine(Path.GetTempPath(), $"WinRemote_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                TxtTestResult.Text = path;
                AddLog("截图已保存");
            }
            catch (Exception ex) { AddLog(ex.Message); }
        }

        private void BtnKeypress_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("模拟按键", "组合键如 ^{ESC}:") { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.InputText))
            {
                try { System.Windows.Forms.SendKeys.SendWait(dlg.InputText); AddLog($"已发送: {dlg.InputText}"); }
                catch (Exception ex) { AddLog(ex.Message); }
            }
        }

        private void BtnMouse_Click(object sender, RoutedEventArgs e)
        {
            var p = System.Windows.Forms.Cursor.Position;
            Core.AgentClient.mouse_event(0x02, 0, 0, 0, 0);
            Core.AgentClient.mouse_event(0x04, 0, 0, 0, 0);
            AddLog($"点击 ({p.X},{p.Y})");
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("打开程序/文件", "路径:") { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.InputText))
            {
                try { Process.Start(new ProcessStartInfo(dlg.InputText) { UseShellExecute = true }); }
                catch (Exception ex) { AddLog(ex.Message); }
            }
        }

        private void BtnReadFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("读取文件", "路径:") { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.InputText))
            {
                try { TxtTestResult.Text = File.ReadAllText(dlg.InputText); }
                catch (Exception ex) { TxtTestResult.Text = ex.Message; }
            }
        }

        private void BtnWriteFile_Click(object sender, RoutedEventArgs e) => AddLog("请通过远程连接使用");

        private async void BtnSendTest_Click(object sender, RoutedEventArgs e)
        {
            var url = TxtServerUrl.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                using var ws = new System.Net.WebSockets.ClientWebSocket();
                await ws.ConnectAsync(new Uri(url), new System.Threading.CancellationTokenSource(5000).Token);
                AddLog("✅ 服务器可达");
            }
            catch (Exception ex) { AddLog($"❌ 连接失败: {ex.Message}"); }
        }

        private void BtnRunTestCommand_Click(object sender, RoutedEventArgs e)
        {
            var cmd = TxtTestCommand.Text.Trim();
            if (string.IsNullOrEmpty(cmd)) return;
            _ = RunCmdAsync(cmd, "cmd");
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e) => TxtFullLog.Clear();

        // ===== 设置 =====

        private void LoadSettingsToUI()
        {
            var c = _config;
            TxtServerUrl.Text = c.ServerUrl;
            TxtToken.Password = c.Token;
            TxtAgentId.Text = c.AgentId;
            SafeSet(TxtHeartbeat, c.HeartbeatInterval.ToString());
            SafeSet(TxtShellTimeout, c.ConnectionTimeout.ToString());
            SafeSet(TxtMaxOutput, c.MaxOutputBytes.ToString());
            SafeSet(TxtScreenshotQuality, c.ScreenshotQuality > 0 ? c.ScreenshotQuality.ToString() : "80");
            SafeSet(TxtMaxReadBytes, c.MaxReadBytes > 0 ? c.MaxReadBytes.ToString() : "1048576");
            SafeSet(TxtWhitelist, string.Join("\n", c.FileReadWhitelist ?? new string[0]));
            SafeSet(TxtBlacklist, c.BlockedKeywords ?? "");
            if (CmbScreenshotFmt != null) CmbScreenshotFmt.SelectedIndex = 0;
            if (ChkAllowPowershell != null) ChkAllowPowershell.IsChecked = c.AllowPowerShell;
            if (ChkAllowWrite != null) ChkAllowWrite.IsChecked = c.AllowWrite;
            if (ChkAutoReconnect != null) ChkAutoReconnect.IsChecked = c.AutoReconnect;
            if (ChkStrictWhitelist != null) ChkStrictWhitelist.IsChecked = c.StrictWhitelist;
            if (ChkPasswordGuard != null) ChkPasswordGuard.IsChecked = c.PasswordGuardEnabled;
            SafeSetPassword(TxtPasswordGuard, c.PasswordGuard);
        }

        private void SyncUIToConfig()
        {
            var c = _config;
            c.ServerUrl = TxtServerUrl.Text.Trim();
            c.Token = TxtToken.Password;
            c.AgentId = TxtAgentId.Text.Trim();
            c.HeartbeatInterval = Int(TxtHeartbeat.Text, 30);
            c.ConnectionTimeout = Int(TxtShellTimeout.Text, 30);
            c.MaxOutputBytes = Int(TxtMaxOutput.Text, 65536);
            c.ScreenshotQuality = Int(TxtScreenshotQuality.Text, 80);
            c.AllowPowerShell = ChkAllowPowershell?.IsChecked ?? true;
            c.AllowWrite = ChkAllowWrite?.IsChecked ?? false;
            c.AutoReconnect = ChkAutoReconnect?.IsChecked ?? true;
            c.StrictWhitelist = ChkStrictWhitelist?.IsChecked ?? false;
            c.PasswordGuardEnabled = ChkPasswordGuard?.IsChecked ?? false;
            c.PasswordGuard = TxtPasswordGuard?.Password ?? "";
            c.MaxReadBytes = Int(TxtMaxReadBytes?.Text, 1048576);
            c.BlockedKeywords = TxtBlacklist?.Text ?? "";
            c.FileReadWhitelist = (TxtWhitelist?.Text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            SyncUIToConfig();
            Core.ConfigManager.Save(_config);
            AddLog("✅ 配置已保存");
        }

        // ===== 系统服务 =====

        private void RefreshServiceStatus()
        {
            if (_svcMgr == null) return;
            var (r, s) = _svcMgr.GetServiceState();
            ServiceStatusText.Text = s;
            ServiceDot.Fill = r
                ? new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF00B894"))
                : new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF5A6280"));
            NssmStatusText.Text = _svcMgr.IsNssmAvailable() ? "NSSM：已安装" : "NSSM：未安装";
        }

        private void BtnSvcInstall_Click(object s, RoutedEventArgs e) { AddLog(_svcMgr.Install()); RefreshServiceStatus(); }
        private void BtnSvcUninstall_Click(object s, RoutedEventArgs e) { AddLog(_svcMgr.Uninstall()); RefreshServiceStatus(); }
        private void BtnSvcStart_Click(object s, RoutedEventArgs e) { AddLog(_svcMgr.Start()); RefreshServiceStatus(); }
        private void BtnSvcStop_Click(object s, RoutedEventArgs e) { AddLog(_svcMgr.Stop()); RefreshServiceStatus(); }
        private void BtnSvcRestart_Click(object s, RoutedEventArgs e) { AddLog(_svcMgr.Restart()); RefreshServiceStatus(); }
        private void BtnDownloadNssm_Click(object s, RoutedEventArgs e) { try { Process.Start(new ProcessStartInfo("https://nssm.cc/download") { UseShellExecute = true }); } catch { } }
        private void BtnViewLogs_Click(object s, RoutedEventArgs e) { TxtServiceLog.Text = _svcMgr.ReadServiceLog(); }
        private void BtnClearServiceLog_Click(object s, RoutedEventArgs e) => TxtServiceLog.Clear();
        private void BtnRefreshLog_Click(object s, RoutedEventArgs e) => TxtFullLog.ScrollToEnd();

        // ===== 系统信息 =====

        private void RefreshSystemInfo()
        {
            TxtHostname.Text = Environment.MachineName;
            TxtOS.Text = Environment.OSVersion.ToString();
            TxtUsername.Text = Environment.UserName;
            TxtDotNetVer.Text = Environment.Version.ToString();
        }

        // ===== 托盘接口 =====

        public void TrayConnect() => Dispatcher.Invoke(() => BtnConnect_Click(null, null));
        public void TrayDisconnect() => Dispatcher.Invoke(() => BtnDisconnect_Click(null, null));
        public void TrayInstallService() => Dispatcher.Invoke(() => BtnSvcInstall_Click(null, null));
        public void TrayUninstallService() => Dispatcher.Invoke(() => BtnSvcUninstall_Click(null, null));
        public void TrayStartService() => Dispatcher.Invoke(() => BtnSvcStart_Click(null, null));
        public void TrayStopService() => Dispatcher.Invoke(() => BtnSvcStop_Click(null, null));
        public void TrayServiceStatus() => Dispatcher.Invoke(() => System.Windows.Forms.MessageBox.Show("请查看「系统服务」选项卡", "服务状态"));
        public void TrayCheckUpdate() => Dispatcher.Invoke(() => System.Windows.Forms.MessageBox.Show("WinRemote Agent V1.2\n\n关闭窗口自动后台运行\n双击托盘图标恢复窗口", "关于"));
        public void TrayRefreshLogs() => Dispatcher.Invoke(() => TxtFullLog.ScrollToEnd());
        public void TrayOpenLogDir() => Dispatcher.Invoke(() =>
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinRemote", "logs");
            if (!Directory.Exists(d)) Directory.CreateDirectory(d);
            Process.Start("explorer.exe", d);
        });

        private static int Int(string s, int def) => int.TryParse(s, out int v) ? v : def;
        private static void SafeSet(System.Windows.Controls.TextBox tb, string v) { if (tb != null) tb.Text = v; }
        private static void SafeSetPassword(System.Windows.Controls.PasswordBox pb, string v) { if (pb != null) pb.Password = v ?? ""; }
    }
}
