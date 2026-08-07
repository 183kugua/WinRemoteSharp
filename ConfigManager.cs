using System;
using System.IO;
using System.Text.Json;

namespace WinRemoteSharp.Core
{
    /// <summary>
    /// 持久化配置（来自 config.json）。与服务端默认 WS 地址保持一致：ws://127.0.0.1:6190/winremote
    /// </summary>
    public class Config
    {
        public string ServerUrl { get; set; } = "ws://127.0.0.1:6190/winremote";
        public string Token { get; set; } = "";
        public string AgentId { get; set; } = "";
        public int ReconnectInterval { get; set; } = 5;
        public int HeartbeatInterval { get; set; } = 30;
        public bool AutoStart { get; set; } = false;
        public string ServiceName { get; set; } = "WinRemoteAgent";
        public string AllowedIPs { get; set; } = "";
        public int ScreenshotQuality { get; set; } = 80;
        public int ScreenshotWidth { get; set; } = 1920;
        public int ScreenshotHeight { get; set; } = 1080;
        public string LogPath { get; set; } = "logs";
        public int LogLevel { get; set; } = 2;
        public int ConnectionTimeout { get; set; } = 15;
        public bool EnableServiceControl { get; set; } = true;

        public int MaxOutputBytes { get; set; } = 65536;
        public int MaxReadBytes { get; set; } = 1048576;
        public string BlockedKeywords { get; set; } = "";
        public bool AllowPowerShell { get; set; } = true;
        public bool AllowWrite { get; set; } = false;
        public bool AutoReconnect { get; set; } = true;
        public bool StrictWhitelist { get; set; } = false;
        public bool PasswordGuardEnabled { get; set; } = false;
        public string PasswordGuard { get; set; } = "";
        public string[] FileReadWhitelist { get; set; } = Array.Empty<string>();
    }

    public static class ConfigManager
    {
        public static Config Load(string path = "config.json")
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var cfg = JsonSerializer.Deserialize<Config>(json);
                    if (cfg != null)
                    {
                        ApplyDefaults(cfg);
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Load error: {ex.Message}");
            }
            var newCfg = new Config();
            Save(newCfg, path);
            return newCfg;
        }

        public static void Save(Config config, string path = "config.json")
        {
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(config, opts);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Save error: {ex.Message}");
            }
        }

        public static void ApplyDefaults(Config cfg)
        {
            if (string.IsNullOrEmpty(cfg.ServerUrl)) cfg.ServerUrl = "ws://127.0.0.1:6190/winremote";
            if (cfg.ReconnectInterval <= 0) cfg.ReconnectInterval = 5;
            if (cfg.HeartbeatInterval <= 0) cfg.HeartbeatInterval = 30;
            if (cfg.ScreenshotQuality <= 0 || cfg.ScreenshotQuality > 100) cfg.ScreenshotQuality = 80;
            if (cfg.ScreenshotWidth <= 0) cfg.ScreenshotWidth = 1920;
            if (cfg.ScreenshotHeight <= 0) cfg.ScreenshotHeight = 1080;
            if (cfg.ConnectionTimeout <= 0) cfg.ConnectionTimeout = 15;
            if (string.IsNullOrEmpty(cfg.ServiceName)) cfg.ServiceName = "WinRemoteAgent";
            if (string.IsNullOrEmpty(cfg.LogPath)) cfg.LogPath = "logs";
            if (cfg.LogLevel < 0 || cfg.LogLevel > 5) cfg.LogLevel = 2;

            if (cfg.MaxOutputBytes <= 0) cfg.MaxOutputBytes = 65536;
            if (cfg.MaxReadBytes <= 0) cfg.MaxReadBytes = 1048576;
            if (cfg.BlockedKeywords == null) cfg.BlockedKeywords = "";
            if (cfg.FileReadWhitelist == null) cfg.FileReadWhitelist = Array.Empty<string>();
        }
    }
}
