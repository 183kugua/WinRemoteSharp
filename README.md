# WinRemote Agent V1.2 — 薄荷天蓝清新版

通过 QQ 消息远程控制 Windows 主机：执行命令、截图、键鼠模拟、文件读写。

C# + WPF 原生实现，单文件发布，双击即用。

## 配色方案

薄荷绿 + 天蓝双主色，清新明亮：

| 用途 | 色值 | 效果 |
|---|---|---|
| 主背景 | `#FFF5FAFA` 薄荷白 | 极浅薄荷底 |
| 次背景 | `#FFE8F6F0` 薄荷奶油 | 卡片/侧栏 |
| 主色 | `#FF6CC9A8` 薄荷绿 | 按钮/激活态 |
| 深主色 | `#FF3AAF8A` 深薄荷 | 悬停/按下 |
| 辅色 | `#FF7EC8E3` 天蓝 | 次按钮 |
| 深辅色 | `#FF4AAED0` 深天蓝 | 底栏/信息 |
| 警告 | `#FFF5A623` 琥珀 | 警告提示 |
| 错误 | `#FFE04848` 珊瑚红 | 危险操作 |
| 成功 | `#FF2ECC71` 翠绿 | 状态灯/成功 |
| 文字 | `#FF1A3A33` 深墨绿 | 主文字 |

## 快速开始

```cmd
:: 1. 安装 .NET 8 SDK
::    https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0

:: 2. 一键发布
cd WinRemoteSharp
publish.bat

:: 3. 产物
dist\WinRemoteAgent.exe   ← 单个文件，约 30-50MB
```

## 三种运行模式

```cmd
WinRemoteAgent.exe                  ← 双击：薄荷 GUI 界面
WinRemoteAgent.exe --mode agent     ← 控制台模式（做服务用）
WinRemoteAgent.exe --mode popup --title "提示" --msg "内容"
```

## 界面预览（5 标签页）

| 标签页 | 功能 |
|---|---|
| 🏠 主控台 | 启停 Agent / 连接信息 / 快速测试 / 实时日志 |
| ⚙️ 设置 | 服务器/令牌/AgentID/心跳/超时/安全选项 |
| 🔧 系统服务 | NSSM 安装/启停/重启/卸载 + 依赖检查 |
| 📋 运行日志 | 筛选/刷新/保存/清空 |
| 🛠️ 工具箱 | CMD/PS/键鼠/文件/通知/截图 |

## 与 Python 版互通

C# Agent 与 Python Agent 通过 **WebSocket + JSON** 通信，服务端 AStrBot 插件**一行不改**即可混用。

## 协议

AGPL-3.0，与 AStrBot 主项目保持一致。
