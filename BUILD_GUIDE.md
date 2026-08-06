# WinRemote Agent 编译指南

## 前置要求

1. **.NET 8 SDK** - 必须安装
   - 下载地址：https://dotnet.microsoft.com/download/dotnet/8.0
   - 安装后验证：打开命令提示符，输入 `dotnet --version`

2. **Windows 系统** - 本项目仅支持 Windows x64

## 快速编译（推荐）

### 方法一：使用 build.bat（Debug 版本）

```batch
build.bat
```

输出位置：`bin\Debug\net8.0-windows\WinRemoteAgent.exe`

### 方法二：使用 publish.bat（Release 单文件版本）

```batch
publish.bat
```

输出位置：`dist\WinRemoteAgent.exe`

**推荐使用方法二**，生成的是独立的单文件可执行程序，无需安装 .NET 运行时。

## 手动编译

### Debug 版本

```batch
dotnet restore WinRemoteSharp.csproj
dotnet build WinRemoteSharp.csproj -c Debug
```

### Release 单文件版本

```batch
dotnet restore WinRemoteSharp.csproj
dotnet publish WinRemoteSharp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

## 常见问题

### 1. "dotnet 不是内部或外部命令"
- 原因：未安装 .NET SDK 或环境变量未配置
- 解决：重新安装 .NET 8 SDK，或手动将 `C:\Program Files\dotnet` 添加到 PATH

### 2. 编译时提示 NU1000 错误
- 原因：网络问题导致 NuGet 包下载失败
- 解决：检查网络连接，或配置国内 NuGet 镜像

### 3. 生成的程序无法运行
- 原因：缺少 Windows 组件或权限不足
- 解决：以管理员身份运行，或检查 Windows 版本是否支持 .NET 8

## 部署说明

编译完成后，将 `dist` 目录下的所有文件复制到目标机器：

```
dist/
├── WinRemoteAgent.exe    # 主程序
├── config.json           # 配置文件（首次运行自动生成）
└── nssm.exe             # 服务管理工具（可选，用于安装系统服务）
```

### 运行方式

1. **GUI 模式**（默认）
   ```
   WinRemoteAgent.exe
   ```

2. **无头模式**（后台服务）
   ```
   WinRemoteAgent.exe --headless
   ```

3. **安装为系统服务**
   ```
   WinRemoteAgent.exe --install-service
   ```

## 配置说明

首次运行时会自动生成 `config.json`，主要配置项：

```json
{
  "ServerUrl": "ws://127.0.0.1:8000/ws/winremote",
  "Token": "",
  "ScreenshotQuality": 80,
  "ScreenshotWidth": 1920,
  "ScreenshotHeight": 1080
}
```

修改 `ServerUrl` 为你的 AstrBot 服务器地址。

## 版本信息

- 当前版本：1.2.0
- 最后更新：2026-08-06
- 框架：.NET 8 + WPF
