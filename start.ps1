# PowerShell 启动脚本
$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (Test-Path "$ScriptDir\AGV_AMR_FmsPlatform") {
    $ProjectRoot = "$ScriptDir\AGV_AMR_FmsPlatform"
} else {
    $ProjectRoot = $ScriptDir
}

$BackendCsproj = "$ProjectRoot\backend\src\Fms.HttpApi.Host\Fms.HttpApi.Host.csproj"
$FrontendDir = "$ProjectRoot\frontend"

Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host "  🚀 AMR / AGV 智能调度与数字孪生仿真平台 (FMS) - Windows PowerShell 启动器" -ForegroundColor Green
Write-Host "========================================================================" -ForegroundColor Cyan

# 检查环境
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "❌ 未检测到 .NET SDK，请先安装 .NET 8.0+" -ForegroundColor Red
    Exit 1
}
if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    Write-Host "❌ 未检测到 Node.js，请先安装 Node 18+" -ForegroundColor Red
    Exit 1
}

$PkgMgr = if (Get-Command pnpm -ErrorAction SilentlyContinue) { "pnpm" } else { "npm" }

# 释放端口
function Stop-PortOccupant([int]$Port) {
    $processes = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique
    foreach ($pidToKill in $processes) {
        Write-Host "⚠️  释放端口 :$Port (PID: $pidToKill)..." -ForegroundColor Yellow
        Stop-Process -Id $pidToKill -Force -ErrorAction SilentlyContinue
    }
}

Stop-PortOccupant 9999
Stop-PortOccupant 9527

Write-Host "🚀 启动后端调度核心服务 (端口: 9999)..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$ProjectRoot\backend'; dotnet run --project '$BackendCsproj' --launch-profile http"

Write-Host "🚀 启动前端调度大屏 (端口: 9527)..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$FrontendDir'; $PkgMgr dev"

Start-Sleep -Seconds 3
Start-Process "http://localhost:9527"

Write-Host "========================================================================" -ForegroundColor Green
Write-Host "✓ 服务已全部拉起，正在自动打开浏览器 http://localhost:9527" -ForegroundColor Green
Write-Host "  默认管理员账号: admin / admin123" -ForegroundColor Cyan
Write-Host "========================================================================" -ForegroundColor Green
