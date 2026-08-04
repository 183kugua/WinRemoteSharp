@echo off
chcp 65001 >nul 2>&1
setlocal EnableDelayedExpansion

echo ==================================================
echo   WinRemote Agent V1.2 Publish Script
echo ==================================================
echo.

REM ============================================================
REM  STEP 1: Clean old artifacts
REM ============================================================
echo [STEP 1/4] Cleaning old build artifacts...
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"
if exist "dist" rmdir /s /q "dist"
echo   DONE Clean complete
echo.

REM ============================================================
REM  STEP 2: Restore NuGet packages
REM ============================================================
echo [STEP 2/4] Restoring NuGet packages...
dotnet restore "WinRemoteSharp.csproj"
if errorlevel 1 (
    echo   ERROR Restore failed
    goto :error
)
echo   DONE Restore complete
echo.

REM ============================================================
REM  STEP 3: Publish self-contained single-file
REM ============================================================
echo [STEP 3/4] Publishing self-contained single-file...
echo   Runtime: win-x64
echo   Target : net8.0-windows
echo   Output : dist\WinRemoteAgent.exe
echo.

dotnet publish "WinRemoteSharp.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "dist"
if errorlevel 1 (
    echo   ERROR Publish failed - see messages above
    goto :error
)
echo   DONE Publish complete
echo.

REM ============================================================
REM  STEP 4: Copy extra files to dist
REM ============================================================
echo [STEP 4/4] Copying files to dist...
if exist "config.json" copy /y "config.json" "dist\config.json" >nul
if exist "nssm.exe" copy /y "nssm.exe" "dist\nssm.exe" >nul
echo   DONE Files copied
echo.

echo ==================================================
echo   BUILD SUCCESSFUL
echo   Output: dist\WinRemoteAgent.exe
echo ==================================================
echo.
echo   Usage:
echo     WinRemoteAgent.exe              (GUI mode)
echo     WinRemoteAgent.exe --headless  (Agent mode)
echo     WinRemoteAgent.exe --install-service
echo.
goto :eof

:error
echo.
echo ==================================================
echo   ERROR Build failed - see messages above
echo ==================================================
echo.
echo   Troubleshooting:
echo   1. Verify .NET 8 SDK: dotnet --version
echo   2. Verify project file: dir WinRemoteSharp.csproj
echo   3. Clean and retry: rmdir /s /q bin obj dist
echo   4. Rebuild: dotnet build WinRemoteSharp.csproj
echo.
pause
exit /b 1
