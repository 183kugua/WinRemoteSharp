using System;

namespace WinRemoteSharp.Core
{
    /// <summary>
    /// Agent 运行配置 — 供 AgentClient 使用
    /// </summary>
    public class AgentConfig
    {
        public string ServerUrl { get; set; } = "ws://127.0.0.1:6190/winremote";
        public string Token { get; set; } = "";
        public string AgentId { get; set; } = "";
        public int HeartbeatIntervalSec { get; set; } = 30;
        public int CommandTimeoutSec { get; set; } = 30;
        public int ReconnectBaseDelaySec { get; set; } = 2;
        public int ReconnectMaxDelaySec { get; set; } = 60;
        public bool EnableKeyboard { get; set; } = true;
        public bool EnableMouse { get; set; } = true;
        public bool EnableFileWrite { get; set; } = false;
        public string ScreenshotFormat { get; set; } = "jpg";
        public string[] FileReadWhitelist { get; set; } = Array.Empty<string>();
    }
}