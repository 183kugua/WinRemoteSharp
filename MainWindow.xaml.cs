using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
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

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        public void SetTrayManager(TrayManager trayManager)
        {
            _trayManager = trayManager;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
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

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_closingToTray)
            {
                // 最小化到托盘而不是退出
                e.Cancel = true;
                Hide();
                _trayManager?.ShowBalloonTip("WinRemote Agent", "已最小化到系统托盘，双击图标可显示窗口", ToolTipIcon.Info);
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
        }

        private void UpdateFooterTime()
        {
            // FooterTime - 未在 XAML 中定义，暂时跳过
        }

        private void AddLog(string msg)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            Dispatcher.Invoke(() =>
            {
                TxtLog.Text += $"[{ts}] {msg}\n";
                LogStatusText.Text = msg;
            });
        }

        private void SetConnectionState(bool connected)
        {
            _isConnected = connected;
            Dispatcher.Invoke(() =>
            {
                if (connected)
                {
                    StatusDot.Fill = (SolidColorBrush)FindResource("StatusOnlineBrush");
                    StatusText.Text = "已连接";
                    StatusText.Foreground = (SolidColorBrush)FindResource("AccentGreenBrush");
                }
                else
                {
                    StatusDot.Fill = (SolidColorBrush)FindResource("StatusOfflineBrush");
                    StatusText.Text = "未连接";
                    StatusText.Foreground = (SolidColorBrush)FindResource("AccentRedBrush");
                }
                // 同步更新托盘提示
                _trayManager?.UpdateConnectionStatus(connected);
            });
        }

        // ===== Button Handlers =====

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
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

        private async void BtnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            if (_agent == null || !_agent.IsConnected())
            {
                MessageBox.Show("请先连接服务器", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            AddLog("Screenshot requested...");
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Text = "";
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

                ConfigManager.Save(_config);
                AddLog("Configuration saved");
                MessageBox.Show("配置已保存", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===== Service Tab =====

        private void BtnInstallService_Click(object sender, RoutedEventArgs e)
        {
            var sm = new ServiceManager();
            AddLog("Installing service...");
            bool ok = sm.Install();
            AddLog(ok ? "Service installed successfully" : "Service installation failed (need nssm.exe)");
            RefreshServiceStatus();
        }

        private void BtnUninstallService_Click(object sender, RoutedEventArgs e)
        {
            var sm = new ServiceManager();
            AddLog("Uninstalling service...");
            bool ok = sm.Uninstall();
            AddLog(ok ? "Service uninstalled" : "Service uninstall failed");
            RefreshServiceStatus();
        }

        private void BtnStartService_Click(object sender, RoutedEventArgs e)
        {
            var sm = new ServiceManager();
            AddLog("Starting service...");
            bool ok = sm.Start();
            AddLog(ok ? "Service started" : "Service start failed");
            RefreshServiceStatus();
        }

        private void BtnStopService_Click(object sender, RoutedEventArgs e)
        {
            var sm = new ServiceManager();
            AddLog("Stopping service...");
            bool ok = sm.Stop();
            AddLog(ok ? "Service stopped" : "Service stop failed");
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

        // ===== Logs Tab =====

        private void BtnRefreshLogs_Click(object sender, RoutedEventArgs e)
        {
            RefreshFullLogs();
        }

        private void BtnOpenLogDir_Click(object sender, RoutedEventArgs e)
        {
            string logDir = _config.LogPath;
            if (!Path.IsPathRooted(logDir))
                logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logDir);
            if (Directory.Exists(logDir))
                Process.Start("explorer.exe", logDir);
            else
                MessageBox.Show($"日志目录不存在: {logDir}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show("当前已是最新版本 v1.2.0", "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
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
    }
}