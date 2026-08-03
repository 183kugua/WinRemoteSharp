@echo off
setlocal

cd /d "%~dp0"

echo ==================================================
echo  WinRemote Agent - Publish Script
echo ==================================================
echo.

REM ---- STEP 1: Clean ----
echo [STEP 1/4] Cleaning old build artifacts...
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"
if exist "dist" rmdir /s /q "dist"
echo   DONE Clean complete
echo.

REM ---- STEP 2: Restore ----
echo [STEP 2/4] Restoring NuGet packages...
dotnet restore "WinRemoteSharp.csproj"
if errorlevel 1 goto :fail
echo   DONE Restore complete
echo.

REM ---- STEP 3: Publish ----
echo [STEP 3/4] Publishing self-contained single-file...
echo   Target: net8.0-windows
echo   RID:    win-x64
echo   Output: dist\WinRemoteAgent.exe
echo.
dotnet publish "WinRemoteSharp.csproj" -c Release -r win-x64 ^
  --self-contained true ^
  /p:PublishSingleFile=true ^
  /p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 goto :fail
echo   DONE Publish complete
echo.

REM ---- STEP 4: Copy to dist ----
echo [STEP 4/4] Copying EXE to dist...
if not exist "dist" mkdir "dist"
copy /y "bin\Release\net8.0-windows\win-x64\publish\WinRemoteAgent.exe" "dist\WinRemoteAgent.exe" >nul
if errorlevel 1 goto :fail
echo   DONE EXE copied
echo.

echo ==================================================
echo  BUILD SUCCESSFUL
echo  Output: dist\WinRemoteAgent.exe
echo ==================================================
echo.
echo Press any key to exit...
pause >nul
exit /b 0

:fail
echo.
echo ==================================================
echo  ERROR Build failed - see messages above
echo ==================================================
echo.
echo Troubleshooting:
echo   1. Verify .NET 8 SDK:  dotnet --version
echo   2. Verify project file: dir WinRemoteSharp.csproj
echo   3. Clean and retry:     rmdir /s /q bin obj dist
echo.
pause
exit /b 1
