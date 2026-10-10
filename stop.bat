@echo off
chcp 65001 >nul
title AMR / AGV 平台 一键停止脚本
cls

echo ========================================================================
echo   正在停止 AMR / AGV 调度仿真平台所有服务...
echo ========================================================================
echo.

:: 终止占用 9999 端口的进程 (后端)
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :9999 ^| findstr LISTENING 2^>nul') do (
    echo 正在释放后端端口 9999 (PID: %%a)...
    taskkill /f /pid %%a >nul 2>nul
)

:: 终止占用 9527 端口的进程 (前端)
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :9527 ^| findstr LISTENING 2^>nul') do (
    echo 正在释放前端端口 9527 (PID: %%a)...
    taskkill /f /pid %%a >nul 2>nul
)

:: 终止相关进程名称
taskkill /f /im Fms.HttpApi.Host.exe >nul 2>nul

echo.
echo [成功] 所有服务已完全停止！端口 9999 与 9527 已释放。
echo.
timeout /t 3 >nul
