# WinRemote Agent C# WPF V1.2

基于 .NET 8 + WPF 的 Windows 远程控制客户端，与 AstrBot 插件协议完全兼容。

## 功能

- WebSocket 长连接 + 自动重连 + 心跳保活
- 远程截图（Win32 GDI 捕获 + WPF JPEG 编码）
- 鼠标控制（移动/点击/滚轮）
- 键盘控制（按键/组合键/文本输入）
- 剪贴板读写
- 远程命令执行
- 进程管理
- 系统信息获取
- Windows 服务安装/启停
- 薄荷天蓝配色 GUI

## 三种运行模式

| 模式 | 启动方式 | 说明 |
|------|---------|------|
| GUI | `WinRemoteAgent.exe` | 双击运行，显示控制面板 |
| 无头 | `WinRemoteAgent.exe --headless` | 无界面运行，仅 WebSocket 连接 |
| 服务 | `WinRemoteAgent.exe --install-service` | 安装为 Windows 服务 |

## 编译发布

```cmd
:: 一键发布（需要 .NET 8 SDK）
publish.bat

:: 输出: dist\WinRemoteAgent.exe (单文件, 自包含)
```

## 配置

编辑 `config.json`：

```json
{
  "ServerUrl": "ws://127.0.0.1:8000/ws/winremote",
  "Token": "your-secret-token",
  "ReconnectInterval": 5,
  "HeartbeatInterval": 30,
  "ScreenshotQuality": 80,
  "ScreenshotWidth": 1920,
  "ScreenshotHeight": 1080
}
```

## 协议兼容

与 Python 版 WinRemote Agent 100% 协议兼容，同一个 AstrBot 服务器可混用。

## 技术栈

- .NET 8 Runtime
- WPF (Windows Presentation Foundation)
- Win32 GDI P/Invoke (截图)
- Win32 User32 P/Invoke (键鼠)
- System.Text.Json (序列化)
- **零 NuGet 依赖**

## 系统要求

- Windows 10 / 11
- .NET 8 Runtime（发布时已自包含，无需额外安装）
- 管理员权限（控制键鼠/安装服务需要）
