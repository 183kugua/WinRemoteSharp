using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WinRemoteSharp.Core;

namespace WinRemoteSharp.Core
{
    public class AgentClient
    {
        private ClientWebSocket _ws;
        private Config _config;
        private CancellationTokenSource _cts;
        private bool _connected;
        private string _authToken = "";
        private System.Threading.Timer _heartbeatTimer;

        // Win32 GDI for screenshots - zero System.Drawing dependency
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SM_CXSCREEN = 0; // Primary screen width
        private const int SM_CYSCREEN = 1; // Primary screen height

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hDestDC, int x, int y, int nWidth, int nHeight, IntPtr hSrcDC, int xSrc, int ySrc, uint dwRop);

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, IntPtr lpvBits, ref BITMAPINFO lpbmi, uint uUsage);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // Constants
        private const uint SRCCOPY = 0x00CC0020;
        private const uint BI_RGB = 0;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_MENU = 0x12; // ALT
        private const byte VK_SHIFT = 0x10;
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            // RGBQUAD palette omitted - we use 24/32 bpp
        }

        public event Action<string> OnLog;
        public event Action<bool> OnConnectionChanged;
        public event Action<string> OnStatusMessage;

        public AgentClient(Config config)
        {
            _config = config;
            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();
        }

        public async Task ConnectAsync(string url, string token = "")
        {
            _authToken = string.IsNullOrEmpty(token) ? _config.Token : token;
            string serverUrl = string.IsNullOrEmpty(url) ? _config.ServerUrl : url;

            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    OnLog?.Invoke($"Connecting to {serverUrl}...");
                    await _ws.ConnectAsync(new Uri(serverUrl), _cts.Token);
                    _connected = true;
                    OnConnectionChanged?.Invoke(true);
                    OnLog?.Invoke("Connected!");

                    // Send auth
                    var authMsg = new Dictionary<string, object>
                    {
                        ["type"] = "auth",
                        ["token"] = _authToken,
                        ["version"] = "1.2.0",
                        ["platform"] = "windows-csharp"
                    };
                    await SendJsonAsync(authMsg);

                    // Start heartbeat
                    _heartbeatTimer = new System.Threading.Timer(async s => await SendHeartbeat(), null, 0, _config.HeartbeatInterval * 1000);

                    // Listen for messages
                    await ReceiveLoop();

                    // If we get here, connection dropped
                    _connected = false;
                    OnConnectionChanged?.Invoke(false);
                    OnLog?.Invoke("Disconnected. Reconnecting in {_config.ReconnectInterval}s...");
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"Error: {ex.Message}");
                    _connected = false;
                    OnConnectionChanged?.Invoke(false);
                }

                // Wait before reconnect
                for (int i = 0; i < _config.ReconnectInterval && !_cts.IsCancellationRequested; i++)
                    await Task.Delay(1000, _cts.Token);
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[65536];
            while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    break;
                }
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await HandleMessage(msg);
                }
            }
        }

        private async Task HandleMessage(string json)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("action", out JsonElement actionEl))
                        return;

                    string action = actionEl.GetString();
                    OnLog?.Invoke($"<- {action}");

                    switch (action)
                    {
                        case "ping":
                            await SendJsonAsync(new Dictionary<string, object> { ["type"] = "pong" });
                            break;

                        case "screenshot":
                            await HandleScreenshot(root);
                            break;

                        case "mouse_move":
                            HandleMouseMove(root);
                            break;

                        case "mouse_click":
                            HandleMouseClick(root);
                            break;

                        case "mouse_scroll":
                            HandleMouseScroll(root);
                            break;

                        case "key_press":
                            HandleKeyPress(root);
                            break;

                        case "key_combo":
                            HandleKeyCombo(root);
                            break;

                        case "type_text":
                            HandleTypeText(root);
                            break;

                        case "get_clipboard":
                            await HandleGetClipboard();
                            break;

                        case "set_clipboard":
                            HandleSetClipboard(root);
                            break;

                        case "exec_cmd":
                            await HandleExecCmd(root);
                            break;

                        case "get_processes":
                            await HandleGetProcesses();
                            break;

                        case "kill_process":
                            HandleKillProcess(root);
                            break;

                        case "get_system_info":
                            await HandleGetSystemInfo();
                            break;

                        case "restart_service":
                            HandleRestartService();
                            break;

                        case "stop_service":
                            HandleStopService();
                            break;

                        case "get_service_status":
                            await HandleGetServiceStatus();
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"HandleMessage error: {ex.Message}");
            }
        }

        // ===== SCREENSHOT using pure Win32 GDI =====
        private async Task HandleScreenshot(JsonElement root)
        {
            int quality = _config.ScreenshotQuality;
            if (root.TryGetProperty("quality", out JsonElement qEl))
                quality = Math.Clamp(qEl.GetInt32(), 10, 100);

            byte[] jpegData = CaptureScreenAsJpeg(quality);
            if (jpegData != null)
            {
                // Encode as base64
                string b64 = Convert.ToBase64String(jpegData);
                var resp = new Dictionary<string, object>
                {
                    ["type"] = "screenshot_result",
                    ["image"] = b64,
                    ["format"] = "jpeg",
                    ["size"] = jpegData.Length
                };
                await SendJsonAsync(resp);
                OnLog?.Invoke($"Screenshot sent ({jpegData.Length} bytes)");
            }
            else
            {
                await SendJsonAsync(new Dictionary<string, object>
                {
                    ["type"] = "error",
                    ["message"] = "Screenshot failed"
                });
            }
        }

        public byte[] CaptureScreenAsJpeg(int quality)
        {
            IntPtr desktopWnd = GetDesktopWindow();
            IntPtr desktopDC = GetWindowDC(desktopWnd);
            if (desktopDC == IntPtr.Zero) return null;

            int width = _config.ScreenshotWidth;
            int height = _config.ScreenshotHeight;

            // Get actual screen size if config is default
            if (width == 1920 && height == 1080)
            {
                // Use Win32 GetSystemMetrics (no System.Windows.Forms dependency)
                width = GetSystemMetrics(SM_CXSCREEN);
                height = GetSystemMetrics(SM_CYSCREEN);
            }

            IntPtr memDC = CreateCompatibleDC(desktopDC);
            IntPtr bitmap = CreateCompatibleBitmap(desktopDC, width, height);
            IntPtr oldBmp = SelectObject(memDC, bitmap);

            // Copy screen to bitmap
            bool bltOk = BitBlt(memDC, 0, 0, width, height, desktopDC, 0, 0, SRCCOPY);

            if (bltOk)
            {
                // Convert to JPEG using WPF (no System.Drawing needed)
                byte[] bmpData = BitmapToJpegViaWpf(memDC, bitmap, width, height, quality);
                SelectObject(memDC, oldBmp);
                DeleteObject(bitmap);
                DeleteDC(memDC);
                ReleaseDC(desktopWnd, desktopDC);
                return bmpData;
            }

            SelectObject(memDC, oldBmp);
            DeleteObject(bitmap);
            DeleteDC(memDC);
            ReleaseDC(desktopWnd, desktopDC);
            return null;
        }

        private byte[] BitmapToJpegViaWpf(IntPtr hDC, IntPtr hBitmap, int width, int height, int quality)
        {
            // Use WPF's Imaging APIs (no System.Drawing)
            int stride = width * 4; // BGRA
            int bufSize = stride * height;
            byte[] pixels = new byte[bufSize];

            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = width;
            bmi.bmiHeader.biHeight = -height; // top-down
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = BI_RGB;

            IntPtr pPixels = Marshal.AllocHGlobal(bufSize);
            try
            {
                int lines = GetDIBits(hDC, hBitmap, 0, (uint)height, pPixels, ref bmi, BI_RGB);
                if (lines == 0) return null;

                Marshal.Copy(pPixels, pixels, 0, bufSize);
            }
            finally
            {
                Marshal.FreeHGlobal(pPixels);
            }

            // Convert BGRA → BGR (WPF JPEG encoder expects BGR)
            byte[] bgrPixels = new byte[width * height * 3];
            for (int i = 0; i < width * height; i++)
            {
                bgrPixels[i * 3 + 0] = pixels[i * 4 + 0]; // B
                bgrPixels[i * 3 + 1] = pixels[i * 4 + 1]; // G
                bgrPixels[i * 3 + 2] = pixels[i * 4 + 2]; // R
            }

            // Use WPF to encode JPEG
            var bitmapSource = System.Windows.Media.Imaging.BitmapSource.Create(
                width, height, 96, 96,
                System.Windows.Media.PixelFormats.Bgr24,
                null, bgrPixels, width * 3);

            var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
            encoder.QualityLevel = quality;
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmapSource));

            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                return ms.ToArray();
            }
        }

        // ===== MOUSE CONTROL =====
        private void HandleMouseMove(JsonElement root)
        {
            int x = root.GetProperty("x").GetInt32();
            int y = root.GetProperty("y").GetInt32();
            SetCursorPos(x, y);
        }

        private void HandleMouseClick(JsonElement root)
        {
            string button = "left";
            if (root.TryGetProperty("button", out JsonElement bEl))
                button = bEl.GetString();

            uint downFlag = MOUSEEVENTF_LEFTDOWN;
            uint upFlag = MOUSEEVENTF_LEFTUP;
            if (button == "right") { downFlag = MOUSEEVENTF_RIGHTDOWN; upFlag = MOUSEEVENTF_RIGHTUP; }
            else if (button == "middle") { downFlag = MOUSEEVENTF_MIDDLEDOWN; upFlag = MOUSEEVENTF_MIDDLEUP; }

            mouse_event(downFlag, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(20);
            mouse_event(upFlag, 0, 0, 0, UIntPtr.Zero);
        }

        private void HandleMouseScroll(JsonElement root)
        {
            int clicks = 3;
            if (root.TryGetProperty("clicks", out JsonElement cEl))
                clicks = cEl.GetInt32();
            if (clicks < 0) clicks = -clicks;

            int delta = 120 * clicks;
            if (root.TryGetProperty("direction", out JsonElement dEl) && dEl.GetString() == "down")
                delta = -delta;

            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)delta, UIntPtr.Zero);
        }

        // ===== KEYBOARD CONTROL =====
        private void HandleKeyPress(JsonElement root)
        {
            string key = root.GetProperty("key").GetString();
            byte vk = KeyNameToVk(key);
            keybd_event(vk, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            Thread.Sleep(30);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private void HandleKeyCombo(JsonElement root)
        {
            var keys = root.GetProperty("keys");
            List<byte> vkList = new List<byte>();
            foreach (JsonElement k in keys.EnumerateArray())
            {
                byte vk = KeyNameToVk(k.GetString());
                vkList.Add(vk);
            }
            // Press all
            foreach (byte vk in vkList)
                keybd_event(vk, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            Thread.Sleep(50);
            // Release in reverse
            for (int i = vkList.Count - 1; i >= 0; i--)
                keybd_event(vkList[i], 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private void HandleTypeText(JsonElement root)
        {
            string text = root.GetProperty("text").GetString();
            if (string.IsNullOrEmpty(text)) return;
            // Use keybd_event to type each character (no WinForms dependency)
            foreach (char c in text)
            {
                // For ASCII printable characters, send directly
                short vk = (short)c;
                keybd_event((byte)vk, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
                keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                Thread.Sleep(10); // small delay between keys
            }
        }

        private byte KeyNameToVk(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0;
            key = key.ToLowerInvariant();
            switch (key)
            {
                case "enter": return 0x0D;
                case "tab": return 0x09;
                case "escape": case "esc": return 0x1B;
                case "space": return 0x20;
                case "backspace": return 0x08;
                case "delete": case "del": return 0x2E;
                case "up": return 0x26;
                case "down": return 0x28;
                case "left": return 0x25;
                case "right": return 0x27;
                case "ctrl": case "control": return 0x11;
                case "alt": return 0x12;
                case "shift": return 0x10;
                case "win": case "windows": return 0x5B;
                case "f1": return 0x70;
                case "f2": return 0x71;
                case "f3": return 0x72;
                case "f4": return 0x73;
                case "f5": return 0x74;
                case "f6": return 0x75;
                case "f7": return 0x76;
                case "f8": return 0x77;
                case "f9": return 0x78;
                case "f10": return 0x79;
                case "f11": return 0x7A;
                case "f12": return 0x7B;
                default:
                    if (key.Length == 1)
                    {
                        char c = key[0];
                        if (c >= 'a' && c <= 'z') return (byte)(c - 'a' + 0x41);
                        if (c >= '0' && c <= '9') return (byte)(c - '0' + 0x30);
                    }
                    return 0;
            }
        }

        // ===== CLIPBOARD =====
        private async Task HandleGetClipboard()
        {
            string text = "";
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (System.Windows.Clipboard.ContainsText())
                        text = System.Windows.Clipboard.GetText();
                }
                catch { }
            });
            await SendJsonAsync(new Dictionary<string, object>
            {
                ["type"] = "clipboard_result",
                ["text"] = text
            });
        }

        private void HandleSetClipboard(JsonElement root)
        {
            string text = root.GetProperty("text").GetString();
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                try { System.Windows.Clipboard.SetText(text); } catch { }
            });
        }

        // ===== COMMAND EXEC =====
        private async Task HandleExecCmd(JsonElement root)
        {
            string cmd = root.GetProperty("command").GetString();
            bool asAdmin = false;
            if (root.TryGetProperty("as_admin", out JsonElement adminEl))
                asAdmin = adminEl.GetBoolean();

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + cmd,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            if (asAdmin)
            {
                psi.Verb = "runas";
                psi.UseShellExecute = true;
            }

            using (var p = System.Diagnostics.Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit();
                await SendJsonAsync(new Dictionary<string, object>
                {
                    ["type"] = "exec_result",
                    ["stdout"] = output,
                    ["stderr"] = error,
                    ["exit_code"] = p.ExitCode
                });
            }
        }

        // ===== PROCESSES =====
        private async Task HandleGetProcesses()
        {
            var list = new List<Dictionary<string, object>>();
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    list.Add(new Dictionary<string, object>
                    {
                        ["pid"] = p.Id,
                        ["name"] = p.ProcessName,
                        ["memory"] = p.WorkingSet64
                    });
                }
                catch { }
            }
            await SendJsonAsync(new Dictionary<string, object>
            {
                ["type"] = "process_list",
                ["processes"] = list
            });
        }

        private void HandleKillProcess(JsonElement root)
        {
            int pid = root.GetProperty("pid").GetInt32();
            try
            {
                var p = System.Diagnostics.Process.GetProcessById(pid);
                p.Kill();
                OnLog?.Invoke($"Killed PID {pid}");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"Kill error: {ex.Message}");
            }
        }

        // ===== SYSTEM INFO =====
        private async Task HandleGetSystemInfo()
        {
            var info = new Dictionary<string, object>
            {
                ["os"] = Environment.OSVersion.ToString(),
                ["machine"] = Environment.MachineName,
                ["user"] = Environment.UserName,
                ["cpu_count"] = Environment.ProcessorCount,
                ["dotnet"] = Environment.Version.ToString(),
                ["uptime"] = (DateTime.Now - Process.GetCurrentProcess().StartTime).ToString(),
                ["agent_version"] = "1.2.0-csharp",
                ["agent_platform"] = "windows-csharp"
            };
            await SendJsonAsync(new Dictionary<string, object>
            {
                ["type"] = "system_info_result",
                ["info"] = info
            });
        }

        // ===== SERVICE CONTROL =====
        private void HandleRestartService()
        {
            var sm = new ServiceManager();
            sm.Stop();
            Thread.Sleep(2000);
            sm.Start();
        }

        private void HandleStopService()
        {
            var sm = new ServiceManager();
            sm.Stop();
        }

        private async Task HandleGetServiceStatus()
        {
            var sm = new ServiceManager();
            string status = sm.GetStatus();
            await SendJsonAsync(new Dictionary<string, object>
            {
                ["type"] = "service_status_result",
                ["status"] = status
            });
        }

        // ===== HEARTBEAT =====
        private async Task SendHeartbeat()
        {
            if (_ws.State != WebSocketState.Open) return;
            try
            {
                await SendJsonAsync(new Dictionary<string, object>
                {
                    ["type"] = "heartbeat",
                    ["timestamp"] = DateTime.Now.ToString("o"),
                    ["connected"] = _connected
                });
            }
            catch { }
        }

        // ===== SEND HELPERS =====
        private async Task SendJsonAsync(Dictionary<string, object> dict)
        {
            if (_ws.State != WebSocketState.Open) return;
            string json = JsonSerializer.Serialize(dict);
            byte[] data = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public void Disconnect()
        {
            _cts.Cancel();
            try { _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None).Wait(2000); } catch { }
        }

        public bool IsConnected() => _connected && _ws.State == WebSocketState.Open;
    }
}
