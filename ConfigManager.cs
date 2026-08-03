using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinRemoteSharp.Core
{
    public class AgentConfig
    {
        public string ServerUrl { get; set; } = "ws://127.0.0.1:6190/winremote";
        public string Token { get; set; } = "";
        public string AgentId { get; set; } = Environment.MachineName;
        public int HeartbeatInterval { get; set; } = 15;
        public int CommandTimeout { get; set; } = 30;
        public string ScreenshotFormat { get; set; } = "PNG";
        public int ScreenshotQuality { get; set; } = 85;
        public bool EnableInputSimulation { get; set; } = false;
        public bool EnableFileWrite { get; set; } = false;
        public List<string> FileReadWhitelist { get; set; } = new() { @"C:\Windows\Temp", @"C:\Users\Public" };
        public List<string> FileWriteBlacklist { get; set; } = new() { "format", "shutdown", "rmdir", "del /s", "fdisk" };
    }

    public class ConfigManager
    {
        private readonly string _configPath;
        public AgentConfig Current { get; private set; } = new();

        public ConfigManager(string? configPath = null)
        {
            _configPath = configPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WinRemote", "agent_config.json");
            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    var cfg = JsonSerializer.Deserialize<AgentConfig>(json);
                    if (cfg != null) Current = cfg;
                }
            }
            catch { /* 使用默认值 */ }
            ApplyDefaults();
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }

        public void ResetToDefaults()
        {
            Current = new AgentConfig();
            ApplyDefaults();
            Save();
        }

        private void ApplyDefaults()
        {
            if (string.IsNullOrEmpty(Current.ServerUrl)) Current.ServerUrl = "ws://127.0.0.1:6190/winremote";
            if (string.IsNullOrEmpty(Current.AgentId)) Current.AgentId = Environment.MachineName;
            if (Current.HeartbeatInterval <= 0) Current.HeartbeatInterval = 15;
            if (Current.CommandTimeout <= 0) Current.CommandTimeout = 30;
            if (string.IsNullOrEmpty(Current.ScreenshotFormat)) Current.ScreenshotFormat = "PNG";
            if (Current.ScreenshotQuality <= 0 || Current.ScreenshotQuality > 100) Current.ScreenshotQuality = 85;
            Current.FileReadWhitelist ??= new List<string>();
            Current.FileWriteBlacklist ??= new List<string>();
        }

        public static string GenerateRandomToken(int length = 32)
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[length];
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "").Substring(0, length);
        }
    }
}
