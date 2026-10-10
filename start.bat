@echo off
chcp 65001 >nul
title AMR / AGV 智能调度与数字孪生仿真平台 一键启动器
cls

echo ========================================================================
echo   AMR / AGV 智能调度与数字孪生仿真平台 (FMS) - Windows 一键启动
echo ========================================================================
echo.

:: 确定工程路径
set "SCRIPT_DIR=%~dp0"
if exist "%SCRIPT_DIR%AGV_AMR_FmsPlatform" (
    set "PROJECT_ROOT=%SCRIPT_DIR%AGV_AMR_FmsPlatform"
) else (
    set "PROJECT_ROOT=%SCRIPT_DIR%"
)

set "BACKEND_CSPROJ=%PROJECT_ROOT%\backend\src\Fms.HttpApi.Host\Fms.HttpApi.Host.csproj"
set "FRONTEND_DIR=%PROJECT_ROOT%\frontend"

:: 检查 dotnet
where dotnet >nul 2>nul
if %errorlevel% neq 0 (
    echo [错误] 未检测到 .NET SDK，请访问 https://dotnet.microsoft.com/download 安装 .NET 8.0+
    pause
    exit /b 1
)

:: 检查 node
where node >nul 2>nul
if %errorlevel% neq 0 (
    echo [错误] 未检测到 Node.js，请访问 https://nodejs.org 安装 Node 18+ (推荐 v20 或 v22)
    pause
    exit /b 1
)

:: 检查前端包管理器 (优先 pnpm)
where pnpm >nul 2>nul
if %errorlevel% equ 0 (
    set "PKG_MGR=pnpm"
) else (
    set "PKG_MGR=npm"
)

echo [1/3] 环境检查通过:
echo       - .NET SDK 就绪
echo       - Node.js 就绪
echo       - 包管理器: %PKG_MGR%
echo.

:: 检查前端 node_modules
if not exist "%FRONTEND_DIR%\node_modules" (
    echo [提示] 正在安装前端依赖，请稍候...
    cd /d "%FRONTEND_DIR%"
    call %PKG_MGR% install
    cd /d "%SCRIPT_DIR%"
)

:: 清理历史端口占用
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :9999 ^| findstr LISTENING 2^>nul') do (
    echo 正在清理端口 9999 残留进程 (PID: %%a)...
    taskkill /f /pid %%a >nul 2>nul
)
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :9527 ^| findstr LISTENING 2^>nul') do (
    echo 正在清理端口 9527 残留进程 (PID: %%a)...
    taskkill /f /pid %%a >nul 2>nul
)

echo [2/3] 正在启动后端核心调度服务 (端口: 9999)...
start "AMR-FMS 后端调度服务 [:9999]" cmd /k "cd /d "%PROJECT_ROOT%\backend" && dotnet run --project "%BACKEND_CSPROJ%" --launch-profile http"

echo [3/3] 正在启动前端调度大屏 (端口: 9527)...
start "AMR-FMS 前端大屏服务 [:9527]" cmd /k "cd /d "%FRONTEND_DIR%" && %PKG_MGR% dev"

echo.
echo ========================================================================
echo   AMR / AGV 智能调度与数字孪生仿真平台已成功启动！
echo ========================================================================
echo   调度大屏与系统前端 : http://localhost:9527
echo   后端 API 与 Swagger : http://127.0.0.1:9999/swagger
echo   SignalR 实时孪生通道: ws://127.0.0.1:9999/hubs/simulation
echo   AI 智能排障与调度助手: http://localhost:9527/logs/ai
echo   大模型与接口密钥配置 : http://localhost:9527/logs/openapi
echo ------------------------------------------------------------------------
echo   预设演示账号:
echo      - 系统管理员:  admin    /  admin123
echo      - 调度操作员:  operator /  operator123
echo      - 安全管理员:  safety   /  safety123
echo      - 监控观察员:  viewer   /  viewer123
echo ========================================================================
echo.
echo 正在自动为您打开系统大屏...
timeout /t 3 >nul
start http://localhost:9527

echo 如需关闭服务，可双击运行 stop.bat，或直接关闭弹出的两个命令行窗口。
echo.
pause
