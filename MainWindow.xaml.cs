using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinRemoteSharp.Core;

namespace WinRemoteSharp
{
    public partial class MainWindow : Window
    {
        private AgentClient _agent;
        private Config _config;
        private bool _isConnected = false;
        private TrayManager _trayManager;
        public bool _closingToTray = true; // true = 最小化到托盘，false = 真正退出

        public bool IsConnected => _isConnected;

        public MainWindow()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                LogCrash(ex, "MainWindow.InitializeComponent");
                throw;
            }
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void LogCrash(Exception ex, string where)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_crash.log");
                string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string line = "[" + time + "] [" + where + "]" + "
" + ex.ToString() + "

";
                File.AppendAllText(path, line);
            }
            catch { /* ignore */ }
        }

        public void SetTrayManager(TrayManager trayManager)
        {
            _trayManager = trayManager;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _config = ConfigManager.Load("config.json");
                ApplyConfigToUI();
                UpdateFooterTime();
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                timer.Tick += (s, ev) => UpdateFooterTime();
                timer.Start();

                // 如果配置了开机自启且当前是最小化启动，检查是否需要自动连接
                if (_config.AutoStart && !IsVisible)
                {
                    AddLog("AutoStart enabled, attempting to connect...");
                    // 这里可以添加自动连接逻辑
                }
            }
            catch (Exception ex)
            {
                LogCrash(ex, "MainWindow_Loaded");
                throw;
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_closingToTray)
            {
                // 最小化到托盘而不是退出
                e.Cancel = true;
                Hide();
                _trayManager?.ShowBalloonTip("WinRemote Agent", "已最小化到系统托盘，双击图标可显示窗口", System.Windows.Forms.ToolTipIcon.Info);
            }
            else
            {
                // 真正退出：断开连接并清理
                if (_agent != null && _agent.IsConnected())
                {
                    _agent.Disconnect();
                }
            }
        }

        private void ApplyConfigToUI()
        {
            TxtServerUrl.Text = _config.ServerUrl;
            TxtShellTimeout.Text = _config.ConnectionTimeout.ToString();
            TxtHeartbeat.Text = _config.HeartbeatInterval.ToString();
            TxtScreenshotQuality.Text = _config.ScreenshotQuality.ToString();
            TxtWhitelist.Text = _config.AllowedIPs;
            TxtToken.Password = _config.Token ?? "";
            TxtMaxOutput.Text = _config.MaxOutputBytes.ToString();
            TxtMaxReadBytes.Text = _config.MaxReadBytes.ToString();
            TxtBlacklist.Text = _config.BlockedKeywords;
            ChkAllowPowershell.IsChecked = _config.AllowPowerShell;
            ChkAllowWrite.IsChecked = _config.AllowWrite;
            ChkAutoReconnect.IsChecked = _config.AutoReconnect;
            ChkStrictWhitelist.IsChecked = _config.StrictWhitelist;
            ChkPasswordGuard.IsChecked = _config.PasswordGuardEnabled;
            TxtPasswordGuard.Password = _config.PasswordGuard ?? "";
        }

        private void UpdateFooterTime()
        {
            StatusMessage.Text = $"就绪 · {DateTime.Now:HH:mm:ss}";
        }

        public void AddLog(string msg)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            Dispatcher.Invoke(() =>
            {
                TxtLog.Text += $"[{ts}] {msg}\n";
                TxtLog.ScrollToEnd();
                if (LogStatusText != null)
                    LogStatusText.Text = msg;
            });
        }

        public void SetConnectionState(bool connected)
        {
            _isConnected = connected;
            Dispatcher.Invoke(() =>
            {
                if (connected)
                {
                    StatusDot.Fill = (SolidColorBrush)FindResource("StatusOnlineBrush");
                    StatusText.Text = "已连接";
                    StatusText.Foreground = (SolidColorBrush)FindResource("AccentGreenBrush");
                    // 开始呼吸动画
                    if (FindResource("StatusDotPulse") is Storyboard pulse)
                        StatusDot.BeginStoryboard(pulse);
                }
                else
                {
                    StatusDot.Fill = (SolidColorBrush)FindResource("StatusOfflineBrush");
                    StatusText.Text = "未连接";
                    StatusText.Foreground = (SolidColorBrush)FindResource("AccentRedBrush");
                    // 停止所有动画并恢复正常大小
                    StatusDot.BeginStoryboard(new Storyboard());
                    StatusDot.Width = 12;
                    StatusDot.Height = 12;
                }
                // 同步更新托盘提示
                _trayManager?.UpdateConnectionStatus(connected);
            });
        }

        // ===== Public methods for TrayManager =====
        public void TrayConnect()
        {
            Dispatcher.Invoke(() => BtnConnect_Click(this, new RoutedEventArgs()));
        }

        public void TrayDisconnect()
        {
            Dispatcher.Invoke(() => BtnDisconnect_Click(this, new RoutedEventArgs()));
        }

        public void TrayInstallService()
        {
            Dispatcher.Invoke(() => BtnSvcInstall_Click(this, new RoutedEventArgs()));
        }

        public void TrayUninstallService()
        {
            Dispatcher.Invoke(() => BtnSvcUninstall_Click(this, new RoutedEventArgs()));
        }

        public void TrayStartService()
        {
            Dispatcher.Invoke(() => BtnSvcStart_Click(this, new RoutedEventArgs()));
        }

        public void TrayStopService()
        {
            Dispatcher.Invoke(() => BtnSvcStop_Click(this, new RoutedEventArgs()));
        }

        public void TrayServiceStatus()
        {
            Dispatcher.Invoke(() => RefreshServiceStatus());
        }

        public void TrayRefreshLogs()
        {
            Dispatcher.Invoke(() => RefreshFullLogs());
        }

        public void TrayOpenLogDir()
        {
            Dispatcher.Invoke(() => BtnOpenLogDir_Click(this, new RoutedEventArgs()));
        }

        public void TrayCheckUpdate()
        {
            Dispatcher.Invoke(() => BtnCheckUpdate_Click(this, new RoutedEventArgs()));
        }

        // ===== Button Handlers =====

        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected) return;

            string url = TxtServerUrl.Text.Trim();
            string token = "";

            // Prompt for token if empty
            if (string.IsNullOrEmpty(_config.Token))
            {
                token = InputDialog.Show(this, "认证令牌", "请输入服务器令牌:", "", true);
                if (token == null) return;
                _config.Token = token;
                ConfigManager.Save(_config);
            }
            else
            {
                token = _config.Token;
            }

            BtnConnect.IsEnabled = false;
            AddLog($"Connecting to {url}...");

            _agent = new AgentClient(_config);
            _agent.OnLog += AddLog;
            _agent.OnConnectionChanged += SetConnectionState;

            // Connect in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await _agent.ConnectAsync(url, token);
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        AddLog($"Connect error: {ex.Message}");
                        BtnConnect.IsEnabled = true;
                    });
                }
            });
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            if (_agent != null)
            {
                _agent.Disconnect();
                _agent = null;
            }
            SetConnectionState(false);
            BtnConnect.IsEnabled = true;
            AddLog("Disconnected");
        }

        private void BtnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            if (_agent == null || !_agent.IsConnected())
            {
                System.Windows.MessageBox.Show("请先连接服务器", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            AddLog("Screenshot requested...");
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Text = "";
        }

        // ===== Missing button handlers from XAML =====
        private void BtnShell_Click(object sender, RoutedEventArgs e)
        {
            if (_agent == null || !_agent.IsConnected())
            {
                System.Windows.MessageBox.Show("请先连接服务器", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            // Open command dialog
            string cmd = InputDialog.Show(this, "执行命令", "请输入要执行的命令:", "ipconfig");
            if (!string.IsNullOrEmpty(cmd))
            {
                AddLog($"Executing: {cmd}");
            }
        }

        private void BtnPowershell_Click(object sender, RoutedEventArgs e)
        {
            if (_agent == null || !_agent.IsConnected())
            {
                System.Windows.MessageBox.Show("请先连接服务器", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            string cmd = InputDialog.Show(this, "PowerShell", "请输入 PowerShell 命令:", "Get-Process");
            if (!string.IsNullOrEmpty(cmd))
            {
                AddLog($"PowerShell: {cmd}");
            }
        }

        private void BtnKeypress_Click(object sender, RoutedEventArgs e)
        {
            if (_agent == null || !_agent.IsConnected())
            {
                System.Windows.MessageBox.Show("请先连接服务器", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            string key = InputDialog.Show(this, "模拟按键", "请输入按键名:", "enter");
            if (!string.IsNullOrEmpty(key))
            {
                AddLog($"Key press: {key}");
            }
        }

        private void BtnMouse_Click(object sender, RoutedEventArgs e)
        {
            if (_agent == null || !_agent.IsConnected())
            {
                System.Windows.MessageBox.Show("请先连接服务器", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            AddLog("Mouse operation requested...");
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            if (_agent == null || !_agent.IsConnected())
            {
                System.Windows.MessageBox.Show("请先连接服务器", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            string path = InputDialog.Show(this, "打开程序", "请输入程序路径:", "notepad.exe");
            if (!string.IsNullOrEmpty(path))
            {
                AddLog($"Open: {path}");
            }
        }

        private void BtnReadFile_Click(object sender, RoutedEventArgs e)
        {
            if (_agent == null || !_agent.IsConnected())
            {
                System.Windows.MessageBox.Show("请先连接服务器", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            string path = InputDialog.Show(this, "读取文件", "请输入文件路径:", "C:\\Windows\\System32\\drivers\\etc\\hosts");
            if (!string.IsNullOrEmpty(path))
            {
                AddLog($"Read file: {path}");
            }
        }

        private void BtnWriteFile_Click(object sender, RoutedEventArgs e)
        {
            if (_agent == null || !_agent.IsConnected())
            {
                System.Windows.MessageBox.Show("请先连接服务器", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            string path = InputDialog.Show(this, "写入文件", "请输入文件路径:", "C:\\temp\\test.txt");
            if (!string.IsNullOrEmpty(path))
            {
                string content = InputDialog.Show(this, "写入内容", "请输入要写入的内容:", "Hello WinRemote!");
                AddLog($"Write file: {path}");
            }
        }

        // ===== Settings Tab =====

        private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _config.ServerUrl = TxtServerUrl.Text.Trim();
                _config.ConnectionTimeout = int.Parse(TxtShellTimeout.Text);
                _config.HeartbeatInterval = int.Parse(TxtHeartbeat.Text);
                _config.ReconnectInterval = int.Parse(TxtHeartbeat.Text);
                _config.ScreenshotQuality = int.Parse(TxtScreenshotQuality.Text);
                _config.AllowedIPs = TxtWhitelist.Text.Trim();
                _config.Token = TxtToken.Password;
                _config.MaxOutputBytes = int.Parse(TxtMaxOutput.Text);
                _config.MaxReadBytes = int.Parse(TxtMaxReadBytes.Text);
                _config.BlockedKeywords = TxtBlacklist.Text;
                _config.AllowPowerShell = ChkAllowPowershell.IsChecked == true;
                _config.AllowWrite = ChkAllowWrite.IsChecked == true;
                _config.AutoReconnect = ChkAutoReconnect.IsChecked == true;
                _config.StrictWhitelist = ChkStrictWhitelist.IsChecked == true;
                _config.PasswordGuardEnabled = ChkPasswordGuard.IsChecked == true;
                _config.PasswordGuard = TxtPasswordGuard.Password;

                ConfigManager.Save(_config);
                AddLog("Configuration saved");
                System.Windows.MessageBox.Show("配置已保存", "成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        // ===== Service Tab =====

        private void BtnSvcInstall_Click(object sender, RoutedEventArgs e)
        {
            var sm = new ServiceManager();
            AddLog("Installing service...");
            bool ok = sm.Install();
            AddLog(ok ? "Service installed successfully" : "Service installation failed (need nssm.exe)");
            RefreshServiceStatus();
        }

        private void BtnSvcUninstall_Click(object sender, RoutedEventArgs e)
        {
            var sm = new ServiceManager();
            AddLog("Uninstalling service...");
            bool ok = sm.Uninstall();
            AddLog(ok ? "Service uninstalled" : "Service uninstall failed");
            RefreshServiceStatus();
        }

        private void BtnSvcStart_Click(object sender, RoutedEventArgs e)
        {
            var sm = new ServiceManager();
            AddLog("Starting service...");
            bool ok = sm.Start();
            AddLog(ok ? "Service started" : "Service start failed");
            RefreshServiceStatus();
        }

        private void BtnSvcStop_Click(object sender, RoutedEventArgs e)
        {
            var sm = new ServiceManager();
            AddLog("Stopping service...");
            bool ok = sm.Stop();
            AddLog(ok ? "Service stopped" : "Service stop failed");
            RefreshServiceStatus();
        }

        private void BtnSvcRestart_Click(object sender, RoutedEventArgs e)
        {
            var sm = new ServiceManager();
            AddLog("Restarting service...");
            sm.Stop();
            System.Threading.Thread.Sleep(2000);
            bool ok = sm.Start();
            AddLog(ok ? "Service restarted" : "Service restart failed");
            RefreshServiceStatus();
        }

        private void BtnServiceStatus_Click(object sender, RoutedEventArgs e)
        {
            RefreshServiceStatus();
        }

        private void RefreshServiceStatus()
        {
            var sm = new ServiceManager();
            string status = sm.GetStatus();
            Dispatcher.Invoke(() =>
            {
                ServiceStatusText.Text = status;
                switch (status)
                {
                    case "Running":
                        ServiceStatusText.Foreground = (SolidColorBrush)FindResource("AccentGreenBrush");
                        break;
                    case "Stopped":
                        ServiceStatusText.Foreground = (SolidColorBrush)FindResource("AccentRedBrush");
                        break;
                    default:
                        ServiceStatusText.Foreground = (SolidColorBrush)FindResource("TextSecondaryBrush");
                        break;
                }
                TxtServiceLog.Text = sm.GetRecentLogs(50);
            });
        }

        private void BtnDownloadNssm_Click(object sender, RoutedEventArgs e)
        {
            AddLog("Downloading NSSM...");
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://nssm.cc/download",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void BtnViewLogs_Click(object sender, RoutedEventArgs e)
        {
            RefreshFullLogs();
        }

        // ===== Logs Tab =====

        private void BtnRefreshLog_Click(object sender, RoutedEventArgs e)
        {
            RefreshFullLogs();
        }

        private void BtnClearServiceLog_Click(object sender, RoutedEventArgs e)
        {
            TxtFullLog.Text = "";
        }

        private void BtnOpenLogDir_Click(object sender, RoutedEventArgs e)
        {
            string logDir = _config.LogPath;
            if (!Path.IsPathRooted(logDir))
                logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logDir);
            if (Directory.Exists(logDir))
                Process.Start("explorer.exe", logDir);
            else
                System.Windows.MessageBox.Show($"日志目录不存在: {logDir}", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        private void RefreshFullLogs()
        {
            var sm = new ServiceManager();
            string logs = sm.GetRecentLogs(200);
            Dispatcher.Invoke(() => TxtFullLog.Text = logs);
        }

        // ===== About Tab =====

        private void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show("当前已是最新版本 v1.2.0", "检查更新", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        private void BtnOpenGitHub_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/183kugua/astrbot_plugin_winremote",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        // ===== Tools Tab =====
        private void BtnSendTest_Click(object sender, RoutedEventArgs e)
        {
            if (_agent == null || !_agent.IsConnected())
            {
                System.Windows.MessageBox.Show("请先连接服务器", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            string cmd = TxtTestCommand.Text.Trim();
            if (string.IsNullOrEmpty(cmd)) return;
            AddLog($"Test command: {cmd}");
            // 这里可以添加实际发送测试指令的逻辑
        }
    }
}