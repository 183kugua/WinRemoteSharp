@echo off
REM ============================================
REM  WinRemote Agent V1.2 - Publish Script
REM  Mint Sky Fresh Theme
REM ============================================

setlocal enabledelayedexpansion

echo.
echo  ============================================
echo    WinRemote Agent V1.2 Publish Script
echo  ============================================
echo.

REM 1. Check dotnet
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] dotnet not found. Install .NET 8 SDK first:
    echo   https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0
    pause
    exit /b 1
)

for /f "tokens=*" %%i in ('dotnet --version') do set DOTNET_VER=%%i
echo [INFO] .NET SDK version: %DOTNET_VER%
echo.

REM 2. Clean old build
echo [STEP] Cleaning old build artifacts...
if exist bin rmdir /s /q bin
if exist obj rmdir /s /q obj
if exist dist rmdir /s /q dist
echo [DONE] Clean complete
echo.

REM 3. Restore packages
echo [STEP] Restoring NuGet packages...
dotnet restore
if errorlevel 1 (
    echo [ERROR] Restore failed
    pause
    exit /b 1
)
echo [DONE] Restore complete
echo.

REM 4. Publish (self-contained + single-file + win-x64)
echo [STEP] Publishing (self-contained, single-file)...
echo   Target: net8.0-windows
echo   RID:    win-x64
echo   Output: dist\WinRemoteAgent.exe
echo.

dotnet publish WinRemoteSharp.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  /p:PublishSingleFile=true ^
  /p:IncludeNativeLibrariesForSelfExtract=true ^
  /p:EnableCompressionInSingleFile=true ^
  /p:DebugType=embedded ^
  -o dist

if errorlevel 1 (
    echo.
    echo [ERROR] Publish failed!
    pause
    exit /b 1
)

echo.
echo  ============================================
echo    PUBLISH SUCCESS
echo  ============================================
echo.

if exist "dist\WinRemoteAgent.exe" (
    for %%A in ("dist\WinRemoteAgent.exe") do set SIZE=%%~zA
    set /a SIZE_MB=!SIZE!/1048576
    echo   File: dist\WinRemoteAgent.exe
    echo   Size: !SIZE_MB! MB
    echo.
    echo   Usage:
    echo    Double-click WinRemoteAgent.exe       GUI mode
    echo    WinRemoteAgent.exe --mode agent       Console mode
    echo.
)

set /p "RUN=Run GUI now? (Y/n): "
if /i "!RUN!"=="y" (
    start "" "dist\WinRemoteAgent.exe"
)

echo.
echo Press any key to exit...
pause >nul
endlocal
exit /b 0
