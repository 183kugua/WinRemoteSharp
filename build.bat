@echo off
chcp 65001 >nul 2>&1
setlocal EnableDelayedExpansion

echo ==================================================
echo   WinRemote Agent V1.2 Build Script (Debug)
echo ==================================================
echo.

REM ============================================================
REM  STEP1: Clean
REM ============================================================
echo [STEP 1/3] Cleaning...
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"
echo   DONE
echo.

REM ============================================================
REM  STEP2: Restore
REM ============================================================
echo [STEP 2/3] Restoring packages...
dotnet restore "WinRemoteSharp.csproj"
if errorlevel 1 (
    echo   ERROR Restore failed
    goto :error
)
echo   DONE
echo.

REM ============================================================
REM  STEP3: Build (Debug, no single-file)
REM ============================================================
echo [STEP 3/3] Building Debug...
dotnet build "WinRemoteSharp.csproj" -c Debug
if errorlevel 1 (
    echo   ERROR Build failed - see messages above
    goto :error
)
echo   DONE Build complete
echo.

echo ==================================================
echo   BUILD SUCCESSFUL ^(Debug^)
echo   Output: bin\Debug\net8.0-windows\WinRemoteAgent.exe
echo ==================================================
echo.
echo   Next: run publish.bat for Release single-file build
echo.
goto :eof

:error
echo.
echo ==================================================
echo   ERROR Build failed
echo ==================================================
echo.
echo   Troubleshooting:
echo   1. Verify .NET 8 SDK: dotnet --version
echo   2. Check csproj:      type WinRemoteSharp.csproj
echo   3. Deep clean:        rmdir /s /q bin obj dist
echo   4. Verbose build:     dotnet build -v diag
echo.
pause
exit /b 1
