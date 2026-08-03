using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using WinRemoteSharp.Core;

namespace WinRemoteSharp.Core
{
    public class AgentClient
    {
        private readonly ConfigManager _config;
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private readonly Timer? _heartbeatTimer;
        private bool _isConnected;
        private bool _isRunning;
        private int _reconnectAttempts;

        public event Action<string>? OnLog;
        public event Action<string>? OnStateChanged;
        public event Action<string, Dictionary<string, object>>? OnMessage;

        public AgentClient(ConfigManager config)
        {
            _config = config;
            _heartbeatTimer = new Timer(DoHeartbeat, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void ApplyConfig(AgentConfig cfg) { /* 下次连接生效 */ }

        public async Task InitializeAsync()
        {
            Log("info", $"Agent 初始化完成 (标识: {_config.Current.AgentId})");
        }

        public async Task StartAsync()
        {
            if (_isRunning) return;
            _isRunning = true;
            OnStateChanged?.Invoke("connecting");
            _ = Task.Run(RunLoopAsync);
        }

        public async Task StopAsync()
        {
            _isRunning = false;
            OnStateChanged?.Invoke("disconnected");
            try { _cts?.Cancel(); } catch { }
            try { if (_ws?.State == WebSocketState.Open) await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "stop", CancellationToken.None); } catch { }
            _heartbeatTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private async Task RunLoopAsync()
        {
            while (_isRunning)
            {
                try
                {
                    await ConnectWithRetryAsync();
                    if (_ws?.State == WebSocketState.Open)
                    {
                        _isConnected = true;
                        _reconnectAttempts = 0;
                        OnStateChanged?.Invoke("connected");
                        Log("ok", "已连接到服务器 ✅");
                        _heartbeatTimer?.Change(0, _config.Current.HeartbeatInterval * 1000);
                        await ReceiveLoopAsync();
                    }
                }
                catch (Exception ex)
                {
                    Log("error", $"连接异常: {ex.Message}");
                }

                _isConnected = false;
                _heartbeatTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                OnStateChanged?.Invoke("disconnected");

                if (!_isRunning) break;

                _reconnectAttempts++;
                var delay = Math.Min(30000, 2000 * Math.Pow(2, Math.Min(_reconnectAttempts, 5)));
                Log("warn", $"{delay / 1000:0} 秒后重连 (第 {_reconnectAttempts} 次)...");
                await Task.Delay((int)delay);
            }
        }

        private async Task ConnectWithRetryAsync()
        {
            _ws?.Dispose();
            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();

            var url = _config.Current.ServerUrl;
            Log("info", $"正在连接 {url} ...");
            await _ws.ConnectAsync(new Uri(url), _cts.Token);
            await SendAuthAsync();
        }

        private async Task SendAuthAsync()
        {
            var auth = new Dictionary<string, object>
            {
                ["type"] = "auth",
                ["token"] = _config.Current.Token,
                ["agent_id"] = _config.Current.AgentId,
                ["version"] = "1.2",
                ["platform"] = "windows"
            };
            await SendJsonAsync(auth);
            Log("debug", "认证信息已发送");
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[64 * 1024];
            while (_ws?.State == WebSocketState.Open && !_cts!.IsCancellationRequested)
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                try
                {
                    var doc = JsonDocument.Parse(json).RootElement;
                    var type = doc.GetProperty("type").GetString() ?? "unknown";
                    var dict = JsonToDict(doc);
                    OnMessage?.Invoke(type, dict);
                    await HandleCommandAsync(type, dict);
                }
                catch (Exception ex) { Log("error", $"消息处理失败: {ex.Message}"); }
            }
        }

        private Dictionary<string, object> JsonToDict(JsonElement root)
        {
            var d = new Dictionary<string, object>();
            foreach (var p in root.EnumerateObject()) d[p.Name] = p.Value.ToString() ?? "";
            return d;
        }

        private async Task HandleCommandAsync(string type, Dictionary<string, object> data)
        {
            switch (type)
            {
                case "exec":
                case "shell":
                    var cmd = data.GetValueOrDefault("cmd", "").ToString() ?? "";
                    var r = await ExecuteShellAsync(cmd);
                    await SendResult(data.GetValueOrDefault("id", "").ToString() ?? "", r);
                    break;
                case "ping":
                    await SendJsonAsync(new() { ["type"] = "pong", ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
                    break;
                default:
                    Log("debug", $"收到指令: {type}");
                    break;
            }
        }

        private async Task SendResult(string id, string output)
        {
            await SendJsonAsync(new()
            {
                ["type"] = "result",
                ["id"] = id,
                ["output"] = output,
                ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        private async Task SendJsonAsync(Dictionary<string, object> msg)
        {
            if (_ws?.State != WebSocketState.Open) return;
            var json = JsonSerializer.Serialize(msg);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private void DoHeartbeat(object? state)
        {
            if (_ws?.State == WebSocketState.Open)
            {
                _ = SendJsonAsync(new()
                {
                    ["type"] = "heartbeat",
                    ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ["agent_id"] = _config.Current.AgentId
                });
            }
        }

        // ===== 对外 API =====
        public async Task<bool> TestConnectionAsync()
        {
            if (_ws?.State != WebSocketState.Open) return false;
            try
            {
                await SendJsonAsync(new() { ["type"] = "ping" });
                return true;
            }
            catch { return false; }
        }

        public async Task<string> ExecuteShellAsync(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return "";
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", $"/c {cmd}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi)!;
                var cts = new CancellationTokenSource(_config.Current.CommandTimeout * 1000);
                var t = Task.Run(() => p.StandardOutput.ReadToEnd());
                if (await Task.WhenAny(t, Task.Delay(-1, cts.Token)) == t)
                {
                    var outp = await t;
                    var err = await p.StandardError.ReadToEndAsync();
                    return string.IsNullOrEmpty(err) ? outp : $"{outp}{err}";
                }
                try { p.Kill(true); } catch { }
                return "命令执行超时 ⏱";
            }
            catch (Exception ex) { return $"执行失败: {ex.Message}"; }
        }

        public async Task<string> ExecutePowerShellAsync(string script)
        {
            if (string.IsNullOrWhiteSpace(script)) return "";
            try
            {
                var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -Command {script}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi)!;
                var cts = new CancellationTokenSource(_config.Current.CommandTimeout * 1000);
                var t = Task.Run(() => p.StandardOutput.ReadToEnd());
                if (await Task.WhenAny(t, Task.Delay(-1, cts.Token)) == t)
                {
                    var outp = await t;
                    var err = await p.StandardError.ReadToEndAsync();
                    return string.IsNullOrEmpty(err) ? outp : $"{outp}{err}";
                }
                try { p.Kill(true); } catch { }
                return "命令执行超时 ⏱";
            }
            catch (Exception ex) { return $"执行失败: {ex.Message}"; }
        }

        public async Task<string> TakeScreenshotAsync()
        {
            try
            {
                var fname = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.{_config.Current.ScreenshotFormat.ToLower()}";
                var psi = new ProcessStartInfo("powershell.exe",
                    $"-NoProfile -Command \"Add-Type -AssemblyName System.Windows.Forms; " +
                    $"[System.Windows.Forms.Screen]::PrimaryScreen | %% {{ $b = New-Object System.Drawing.Bitmap($_.Bounds.Width, $_.Bounds.Height); " +
                    $"$g = [System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen($_.Bounds.Location, [System.Drawing.Point]::Empty, $_.Bounds.Size); " +
                    $"$b.Save('{fname}')\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                await p!.WaitForExitAsync();
                return $"截图已保存: {fname}";
            }
            catch (Exception ex) { return $"截图失败: {ex.Message}"; }
        }

        public async Task<string> SendKeysAsync(string keys)
        {
            if (!_config.Current.EnableInputSimulation) return "键盘模拟未启用 ⚠️";
            // Windows 原生 API 调用在 Win32 命名空间实现
            try
            {
                var psi = new ProcessStartInfo("powershell.exe",
                    $"-NoProfile -Command \"Add-Type -AssemblyName System.Windows.Forms; " +
                    $"[System.Windows.Forms.SendKeys]::SendWait('{keys.Replace("'", "''")}')\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                await p!.WaitForExitAsync();
                return $"已发送按键: {keys}";
            }
            catch (Exception ex) { return $"发送失败: {ex.Message}"; }
        }

        public async Task<string> SendMouseAsync(string spec)
        {
            if (!_config.Current.EnableInputSimulation) return "鼠标模拟未启用 ⚠️";
            // spec: "x=100,y=200,click=left"
            try
            {
                var parts = spec.Split(',');
                int x = 0, y = 0; string click = "";
                foreach (var p in parts)
                {
                    var kv = p.Split('=');
                    if (kv.Length != 2) continue;
                    if (kv[0].Trim() == "x") int.TryParse(kv[1], out x);
                    else if (kv[0].Trim() == "y") int.TryParse(kv[1], out y);
                    else if (kv[0].Trim() == "click") click = kv[1].Trim();
                }
                var ps = $"Add-Type -AssemblyName System.Windows.Forms; " +
                         $"[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point({x},{y});";
                if (click == "left") ps += "[System.Windows.Forms.SendKeys]::SendWait('{{LEFT CLICK}}')";
                else if (click == "right") ps += "[System.Windows.Forms.SendKeys]::SendWait('{{RIGHT CLICK}}')";
                var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -Command \"{ps}\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                await p!.WaitForExitAsync();
                return $"鼠标已移动到 ({x},{y})" + (string.IsNullOrEmpty(click) ? "" : $" 并执行 {click} 键点击");
            }
            catch (Exception ex) { return $"鼠标操作失败: {ex.Message}"; }
        }

        public async Task<string> ReadFileAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "路径为空";
            var full = System.IO.Path.GetFullPath(path);
            var allowed = _config.Current.FileReadWhitelist;
            var ok = allowed.Count == 0 || allowed.Exists(w => full.StartsWith(w, StringComparison.OrdinalIgnoreCase));
            if (!ok) return $"路径不在白名单 ❌ ({full})";
            try
            {
                if (!System.IO.File.Exists(full)) return "文件不存在";
                var txt = await System.IO.File.ReadAllTextAsync(full);
                return txt.Length > 4000 ? txt.Substring(0, 4000) + "\n...[截断]" : txt;
            }
            catch (Exception ex) { return $"读取失败: {ex.Message}"; }
        }

        public async Task<string> OpenProgramAsync(string program)
        {
            if (string.IsNullOrWhiteSpace(program)) return "程序名为空";
            try
            {
                Process.Start(new ProcessStartInfo(program) { UseShellExecute = true });
                return $"已启动: {program}";
            }
            catch (Exception ex) { return $"启动失败: {ex.Message}"; }
        }

        public async Task<string> ShowPopupAsync(string title, string message)
        {
            try
            {
                var ps = $"Add-Type -AssemblyName System.Windows.Forms; " +
                         $"[System.Windows.Forms.MessageBox]::Show('{message.Replace("'", "''")}','{title.Replace("'", "''")}','OK','Information') | Out-Null";
                var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -Command \"{ps}\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                await p!.WaitForExitAsync();
                return $"通知已显示: {title}";
            }
            catch (Exception ex) { return $"通知失败: {ex.Message}"; }
        }

        // ===== 辅助 =====
        private void Log(string level, string msg)
        {
            OnLog?.Invoke(level, msg);
        }
    }
}
