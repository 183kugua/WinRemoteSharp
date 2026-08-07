@echo off
chcp 65001 >nul
echo ================================
echo   WinRemoteSharp 构建脚本
echo ================================
echo.

echo [STEP 1/4] Cleaning old build artifacts...
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"
if exist "publish" rmdir /s /q "publish"
echo DONE Clean complete
echo.

echo [STEP 2/4] Restoring NuGet packages...
dotnet restore WinRemoteSharp.csproj
echo DONE Restore complete
echo.

echo [STEP 3/4] Building Release configuration...
dotnet build --configuration Release --no-restore
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR Build failed - see messages above
    echo.
    pause
    exit /b 1
)
echo DONE Build complete
echo.

echo [STEP 4/4] Publishing self-contained single-file...
dotnet publish --configuration Release --no-restore --output publish
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR Publish failed - see messages above
    echo.
    pause
    exit /b 1
)
echo DONE Publish complete
echo.

echo ================================
echo   构建成功！
echo   输出目录：.\publish
echo ================================
echo.
pause
