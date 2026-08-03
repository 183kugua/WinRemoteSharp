using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WinRemoteSharp.Core;

namespace WinRemoteSharp
{
    public partial class MainWindow : Window
    {
        private readonly AgentClient _agent;
        private readonly ConfigManager _config;
        private readonly ServiceManager _svc;
        private bool _isAgentRunning;
        private readonly List<string> _logBuffer = new List<string>();
        private const int MaxLogLines = 1000;

        // 薄荷配色画刷（与 App.xaml 资源保持一致）
        private readonly Brush _brushMintDeep = new SolidColorBrush(Color.FromRgb(0x3A, 0xAF, 0x8A));
        private readonly Brush _brushMintMain = new SolidColorBrush(Color.FromRgb(0x6C, 0xC9, 0xA8));
        private readonly Brush _brushSkyDeep  = new SolidColorBrush(Color.FromRgb(0x4A, 0xAE, 0xD0));
        private readonly Brush _brushCoral    = new SolidColorBrush(Color.FromRgb(0xEE, 0x6B, 0x6B));
        private readonly Brush _brushLemon    = new SolidColorBrush(Color.FromRgb(0xF4, 0xD3, 0x5E));
        private readonly Brush _brushGreen    = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
        private readonly Brush _brushWhite    = Brushes.White;

        public MainWindow()
        {
            InitializeComponent();
            _config = new ConfigManager();
            _agent = new AgentClient(_config);
            _svc = new ServiceManager();

            _agent.OnLog += AppendLog;
            _agent.OnStateChanged += OnAgentStateChanged;
            _agent.OnMessage += OnAgentMessage;

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadConfigToUI();
            RefreshServiceStatus();
            await _agent.InitializeAsync();
            AppendLog("info", "WinRemote 薄荷清新版 V1.2 已启动");
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            try { _agent?.StopAsync().Wait(3000); } catch { }
        }

        // ===== 状态灯 =====
        private SolidColorBrush GetStatusBrush(string level) => level switch
        {
            "ok" => _brushGreen,
            "warn" => _brushLemon,
            "err" => _brushCoral,
            _ => _brushCoral
        };

        private void SetAgentStatus(string text, string level)
        {
            Dispatcher.Invoke(() =>
            {
                AgentStatusText.Text = text;
                AgentStatusDot.Fill = GetStatusBrush(level);
            });
        }

        private void SetServiceStatus(string text, string level)
        {
            Dispatcher.Invoke(() =>
            {
                ServiceStatusText.Text = text;
                ServiceStatusDot.Fill = GetStatusBrush(level);
            });
        }

        private void SetConnStatus(string text, bool connected)
        {
            Dispatcher.Invoke(() =>
            {
                TxtConnStatus.Text = text;
                ConnDot.Fill = connected ? _brushGreen : _brushCoral;
            });
        }

        // ===== 日志 =====
        private void AppendLog(string level, string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] [{level.ToUpper(),-5}] {msg}{Environment.NewLine}";
            lock (_logBuffer)
            {
                _logBuffer.Add(line);
                if (_logBuffer.Count > MaxLogLines) _logBuffer.RemoveAt(0);
            }
            Dispatcher.Invoke(() =>
            {
                if (TxtLog != null) TxtLog.AppendText(line);
                if (TxtFullLog != null) TxtFullLog.AppendText(line);
                UpdateLogCount();
            });
        }

        private void UpdateLogCount()
        {
            if (LogCountText != null)
            {
                int count;
                lock (_logBuffer) { count = _logBuffer.Count; }
                LogCountText.Text = $"共 {count} 条日志";
            }
        }

        // ===== Agent 事件 =====
        private void OnAgentStateChanged(string state)
        {
            Dispatcher.Invoke(() =>
            {
                switch (state)
                {
                    case "connected":
                        _isAgentRunning = true;
                        SetAgentStatus("Agent 运行中", "ok");
                        SetConnStatus("已连接", true);
                        BtnStartAgent.IsEnabled = false;
                        BtnStopAgent.IsEnabled = true;
                        StatusText.Text = "Agent 已连接";
                        break;
                    case "disconnected":
                        _isAgentRunning = false;
                        SetAgentStatus("Agent 已停止", "err");
                        SetConnStatus("未连接", false);
                        BtnStartAgent.IsEnabled = true;
                        BtnStopAgent.IsEnabled = false;
                        StatusText.Text = "Agent 已断开";
                        break;
                    case "connecting":
                        SetAgentStatus("Agent 连接中...", "warn");
                        SetConnStatus("连接中...", false);
                        StatusText.Text = "正在连接服务器...";
                        break;
                }
            });
        }

        private void OnAgentMessage(string type, Dictionary<string, object> data)
        {
            AppendLog("debug", $"<< {type} 数据: {data.Count} 字段");
        }

        // ===== 主控台按钮 =====
        private async void BtnStartAgent_Click(object sender, RoutedEventArgs e)
        {
            BtnStartAgent.IsEnabled = false;
            AppendLog("info", "正在启动 Agent...");
            try { await _agent.StartAsync(); }
            catch (Exception ex) { AppendLog("error", $"启动失败: {ex.Message}"); BtnStartAgent.IsEnabled = true; }
        }

        private async void BtnStopAgent_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("info", "正在停止 Agent...");
            try { await _agent.StopAsync(); }
            catch (Exception ex) { AppendLog("error", $"停止失败: {ex.Message}"); }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            lock (_logBuffer) { _logBuffer.Clear(); }
            TxtLog.Clear(); TxtFullLog.Clear(); UpdateLogCount();
        }

        private async void BtnTestConn_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("info", "测试连接...");
            if (!_isAgentRunning) { AppendLog("warn", "Agent 未运行，先启动"); return; }
            var ok = await _agent.TestConnectionAsync();
            AppendLog(ok ? "ok" : "error", ok ? "连接测试通过 ✅" : "连接测试失败 ❌");
        }

        private async void BtnQuickIp_Click(object sender, RoutedEventArgs e)
        {
            if (!_isAgentRunning) { AppendLog("warn", "Agent 未运行"); return; }
            var r = await _agent.ExecuteShellAsync("ipconfig | findstr IPv4");
            AppendLog("ok", $"IP 查询结果:{Environment.NewLine}{r}");
        }

        private async void BtnQuickProcess_Click(object sender, RoutedEventArgs e)
        {
            if (!_isAgentRunning) { AppendLog("warn", "Agent 未运行"); return; }
            var r = await _agent.ExecuteShellAsync("tasklist /FI \"MEMUSAGE gt 50000\"");
            AppendLog("ok", $"进程列表:{Environment.NewLine}{r}");
        }

        private async void BtnQuickScreenshot_Click(object sender, RoutedEventArgs e)
        {
            if (!_isAgentRunning) { AppendLog("warn", "Agent 未运行"); return; }
            var r = await _agent.TakeScreenshotAsync();
            AppendLog("ok", $"截图完成: {r}");
        }

        // ===== 设置页 =====
        private void LoadConfigToUI()
        {
            var c = _config.Current;
            CfgServer.Text = c.ServerUrl;
            CfgToken.Password = c.Token;
            CfgAgentId.Text = c.AgentId;
            CfgHeartbeat.Text = c.HeartbeatInterval.ToString();
            CfgTimeout.Text = c.CommandTimeout.ToString();
            CfgScreenshotFmt.SelectedItem = c.ScreenshotFormat;
            CfgScreenshotQuality.Text = c.ScreenshotQuality.ToString();
            CfgEnableInput.IsChecked = c.EnableInputSimulation;
            CfgEnableWrite.IsChecked = c.EnableFileWrite;
            CfgReadWhitelist.Text = string.Join(Environment.NewLine, c.FileReadWhitelist);
            CfgWriteBlacklist.Text = string.Join(Environment.NewLine, c.FileWriteBlacklist);

            TxtServer.Text = c.ServerUrl;
            TxtToken.Text = string.IsNullOrEmpty(c.Token) ? "(未设置)" : $"(已设置，长度 {c.Token.Length})";
            TxtAgentId.Text = c.AgentId;
        }

        private void BtnGenToken_Click(object sender, RoutedEventArgs e)
        {
            var t = ConfigManager.GenerateRandomToken(32);
            CfgToken.Password = t;
            AppendLog("info", $"已生成随机令牌 (长度 {t.Length})");
        }

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var c = _config.Current;
                c.ServerUrl = CfgServer.Text.Trim();
                c.Token = CfgToken.Password;
                c.AgentId = CfgAgentId.Text.Trim();
                c.HeartbeatInterval = int.Parse(CfgHeartbeat.Text);
                c.CommandTimeout = int.Parse(CfgTimeout.Text);
                c.ScreenshotFormat = (CfgScreenshotFmt.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "PNG";
                c.ScreenshotQuality = int.Parse(CfgScreenshotQuality.Text);
                c.EnableInputSimulation = CfgEnableInput.IsChecked == true;
                c.EnableFileWrite = CfgEnableWrite.IsChecked == true;
                c.FileReadWhitelist = CfgReadWhitelist.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                c.FileWriteBlacklist = CfgWriteBlacklist.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();

                _config.Save();
                _agent.ApplyConfig(_config.Current);
                LoadConfigToUI();
                AppendLog("ok", "设置已保存 ✅");
                MessageBox.Show("设置已保存", "WinRemote", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { AppendLog("error", $"保存失败: {ex.Message}"); }
        }

        private void BtnResetSettings_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("恢复默认设置？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _config.ResetToDefaults();
            LoadConfigToUI();
            AppendLog("info", "已恢复默认设置");
        }

        // ===== 系统服务页 =====
        private void RefreshServiceStatus()
        {
            var svcName = _svc.ServiceName;
            var status = _svc.GetStatus();
            SvcStatusText.Text = status switch
            {
                "Running" => "运行中",
                "Stopped" => "已停止",
                "NotFound" => "未安装",
                _ => status
            };
            SvcStartType.Text = _svc.GetStartType();
            SvcNssmPath.Text = _svc.FindNssm() ?? "(未找到)";
            SvcExePath.Text = _svc.GetExePath();

            var ok = status == "Running";
            var lvl = ok ? "ok" : (status == "Stopped" ? "warn" : "err");
            SvcStatusDot.Fill = GetStatusBrush(lvl);
            SetServiceStatus(ok ? "服务: 运行中" : $"服务: {SvcStatusText.Text}", ok ? "ok" : "warn");
        }

        private void BtnSvcInstall_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WinRemoteAgent.exe");
                var r = _svc.Install(exe);
                AppendLog(r.Success ? "ok" : "error", $"安装服务: {r.Message}");
                RefreshServiceStatus();
            }
            catch (Exception ex) { AppendLog("error", $"安装异常: {ex.Message}"); }
        }

        private void BtnSvcStart_Click(object sender, RoutedEventArgs e)
        {
            var r = _svc.Start();
            AppendLog(r.Success ? "ok" : "error", $"启动服务: {r.Message}");
            RefreshServiceStatus();
        }

        private void BtnSvcStop_Click(object sender, RoutedEventArgs e)
        {
            var r = _svc.Stop();
            AppendLog(r.Success ? "ok" : "warn", $"停止服务: {r.Message}");
            RefreshServiceStatus();
        }

        private void BtnSvcRestart_Click(object sender, RoutedEventArgs e)
        {
            _svc.Stop();
            System.Threading.Thread.Sleep(1000);
            var r = _svc.Start();
            AppendLog(r.Success ? "ok" : "error", $"重启服务: {r.Message}");
            RefreshServiceStatus();
        }

        private void BtnSvcUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定卸载服务？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var r = _svc.Uninstall();
            AppendLog(r.Success ? "ok" : "error", $"卸载服务: {r.Message}");
            RefreshServiceStatus();
        }

        private void BtnCheckDeps_Click(object sender, RoutedEventArgs e) => CheckDependencies();
        private void CheckDependencies()
        {
            DependenciesPanel.Children.Clear();
            var deps = new[] {
                (".NET 8 Runtime", "dotnet --list-runtimes"),
                ("NSSM (服务)", "where nssm"),
                ("GDI32 (截屏)", null),
                ("WinForms (通知)", null),
            };
            foreach (var (name, _) in deps)
            {
                var ok = name switch
                {
                    ".NET 8 Runtime" => CheckDotNet(),
                    "NSSM (服务)" => !string.IsNullOrEmpty(_svc.FindNssm()),
                    _ => true
                };
                var border = new Border
                {
                    Background = ok ? new SolidColorBrush(Color.FromRgb(0xD0, 0xED, 0xDD)) : new SolidColorBrush(Color.FromRgb(0xFD, 0xE8, 0xE8)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 0, 8, 8)
                };
                var tb = new TextBlock
                {
                    Text = ok ? $"✅ {name}" : $"❌ {name}",
                    FontSize = 11.5,
                    Foreground = ok ? _brushMintDeep : _brushCoral,
                    FontWeight = FontWeights.SemiBold
                };
                border.Child = tb;
                DependenciesPanel.Children.Add(border);
            }
        }

        private bool CheckDotNet()
        {
            try
            {
                var psi = new ProcessStartInfo("dotnet", "--list-runtimes")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                var outp = p?.StandardOutput.ReadToEnd() ?? "";
                return outp.Contains("Microsoft.NETCore.App 8");
            }
            catch { return false; }
        }

        private void BtnViewStdout_Click(object sender, RoutedEventArgs e) => OpenFile(_svc.GetStdoutPath());
        private void BtnViewStderr_Click(object sender, RoutedEventArgs e) => OpenFile(_svc.GetStderrPath());
        private void OpenFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            { AppendLog("warn", "日志文件不存在"); return; }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        // ===== 运行日志页 =====
        private void BtnRefreshLog_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("info", "日志已刷新");
        }

        private void BtnSaveLog_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "文本文件|*.txt|所有文件|*.*",
                FileName = $"WinRemote_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };
            if (dlg.ShowDialog() == true)
            {
                lock (_logBuffer) { File.WriteAllLines(dlg.FileName, _logBuffer); }
                AppendLog("ok", $"日志已保存: {dlg.FileName}");
            }
        }

        private void BtnClearAllLog_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("清空全部日志？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            lock (_logBuffer) { _logBuffer.Clear(); }
            TxtLog.Clear(); TxtFullLog.Clear(); UpdateLogCount();
            AppendLog("info", "日志已清空");
        }

        // ===== 工具箱页 =====
        private async void BtnRunCmd_Click(object sender, RoutedEventArgs e)
        {
            if (!_isAgentRunning) { AppendLog("warn", "Agent 未运行"); return; }
            var r = await _agent.ExecuteShellAsync(ToolCmd.Text);
            TxtToolResult.Text = r;
        }

        private async void BtnRunPs_Click(object sender, RoutedEventArgs e)
        {
            if (!_isAgentRunning) { AppendLog("warn", "Agent 未运行"); return; }
            var r = await _agent.ExecutePowerShellAsync(ToolPs.Text);
            TxtToolResult.Text = r;
        }

        private async void BtnSendKeys_Click(object sender, RoutedEventArgs e)
        {
            if (!_isAgentRunning) { AppendLog("warn", "Agent 未运行"); return; }
            var r = await _agent.SendKeysAsync(ToolKeys.Text);
            TxtToolResult.Text = r;
        }

        private async void BtnSendMouse_Click(object sender, RoutedEventArgs e)
        {
            if (!_isAgentRunning) { AppendLog("warn", "Agent 未运行"); return; }
            var r = await _agent.SendMouseAsync(ToolMouse.Text);
            TxtToolResult.Text = r;
        }

        private async void BtnReadFile_Click(object sender, RoutedEventArgs e)
        {
            if (!_isAgentRunning) { AppendLog("warn", "Agent 未运行"); return; }
            var r = await _agent.ReadFileAsync(ToolReadFile.Text);
            TxtToolResult.Text = r;
        }

        private async void BtnOpenProgram_Click(object sender, RoutedEventArgs e)
        {
            if (!_isAgentRunning) { AppendLog("warn", "Agent 未运行"); return; }
            var r = await _agent.OpenProgramAsync(ToolOpenProgram.Text);
            TxtToolResult.Text = r;
        }

        private async void BtnShowPopup_Click(object sender, RoutedEventArgs e)
        {
            if (!_isAgentRunning) { AppendLog("warn", "Agent 未运行"); return; }
            var r = await _agent.ShowPopupAsync(ToolPopupTitle.Text, ToolPopupMsg.Text);
            TxtToolResult.Text = r;
        }

        private async void BtnToolScreenshot_Click(object sender, RoutedEventArgs e)
        {
            if (!_isAgentRunning) { AppendLog("warn", "Agent 未运行"); return; }
            var r = await _agent.TakeScreenshotAsync();
            TxtToolResult.Text = r;
        }

        // ===== 顶栏按钮 =====
        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_isAgentRunning && MessageBox.Show("Agent 仍在运行，确定退出？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            Close();
        }
    }
}
