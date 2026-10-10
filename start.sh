#!/usr/bin/env bash

# ==============================================================================
# AMR / AGV 智能调度与数字孪生仿真平台 (FMS) 一键全栈启动脚本
# ==============================================================================
# 支持功能:
#   ./start.sh             前台交互启动 (按 Ctrl+C 优雅停止前后端)
#   ./start.sh -d, --daemon 后台常驻启动 (守护进程，输出日志至 .run/logs)
#   ./start.sh stop        停止所有正在运行的前后端服务
#   ./start.sh restart     重启前后端服务
#   ./start.sh status      检查前后端服务运行状态与端口
#   ./start.sh logs        实时查看后台运行日志
# ==============================================================================

set -e

# 终端色彩
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

# 定位平台工程根目录
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ -d "$SCRIPT_DIR/AGV_AMR_FmsPlatform" ]; then
  PROJECT_ROOT="$SCRIPT_DIR/AGV_AMR_FmsPlatform"
else
  PROJECT_ROOT="$SCRIPT_DIR"
fi

BACKEND_DIR="$PROJECT_ROOT/backend"
BACKEND_CSPROJ="$PROJECT_ROOT/backend/src/Fms.HttpApi.Host/Fms.HttpApi.Host.csproj"
FRONTEND_DIR="$PROJECT_ROOT/frontend"
RUN_DIR="$PROJECT_ROOT/.run"
LOG_DIR="$RUN_DIR/logs"

BACKEND_PORT=9999
FRONTEND_PORT=9527

mkdir -p "$LOG_DIR"

# ------------------------------------------------------------------------------
# 辅助函数: 检查端口是否被占用
# ------------------------------------------------------------------------------
check_port() {
  local port=$1
  lsof -i :"$port" -sTCP:LISTEN -t 2>/dev/null || true
}

# ------------------------------------------------------------------------------
# 辅助函数: 安全终止指定端口的进程
# ------------------------------------------------------------------------------
kill_port() {
  local port=$1
  local pids
  pids=$(lsof -i :"$port" -sTCP:LISTEN -t 2>/dev/null || true)
  if [ -n "$pids" ]; then
    echo -e "${YELLOW}⚠️  检测到端口 :${port} 正在被占用 (PID: ${pids})，正在安全释放...${NC}"
    kill -15 $pids 2>/dev/null || true
    sleep 1
    # 若仍未退出则强杀
    local remain
    remain=$(lsof -i :"$port" -sTCP:LISTEN -t 2>/dev/null || true)
    if [ -n "$remain" ]; then
      kill -9 $remain 2>/dev/null || true
      sleep 0.5
    fi
    echo -e "${GREEN}✓ 端口 :${port} 已释放${NC}"
  fi
}

# ------------------------------------------------------------------------------
# 依赖与环境自检
# ------------------------------------------------------------------------------
check_prerequisites() {
  echo -e "${CYAN}🔍 [1/3] 检查运行环境与工具链...${NC}"

  # 1. 检查 .NET SDK
  if ! command -v dotnet >/dev/null 2>&1; then
    echo -e "${RED}❌ 错误: 未检测到 dotnet 命令，请先安装 .NET SDK 8.0+。${NC}"
    exit 1
  fi
  local dotnet_ver
  dotnet_ver=$(dotnet --version 2>/dev/null || echo "未知")
  echo -e "   ${GREEN}✓ .NET SDK 版本:${NC} $dotnet_ver"

  # 2. 检查 Node.js
  if ! command -v node >/dev/null 2>&1; then
    echo -e "${RED}❌ 错误: 未检测到 Node.js，请安装 Node 18+ (推荐 v20 或 v22)。${NC}"
    exit 1
  fi
  local node_ver
  node_ver=$(node -v 2>/dev/null || echo "未知")
  echo -e "   ${GREEN}✓ Node.js 版本:${NC} $node_ver"

  # 3. 检查包管理器 (优先使用 pnpm，若无则使用 npm)
  if command -v pnpm >/dev/null 2>&1; then
    PKG_MGR="pnpm"
  else
    PKG_MGR="npm"
  fi
  echo -e "   ${GREEN}✓ 前端包管理器:${NC} $PKG_MGR"

  # 4. 自动检测本机代理 (为 Google Gemini AI 模块自动赋能)
  if lsof -i :7890 >/dev/null 2>&1; then
    export HTTPS_PROXY="http://127.0.0.1:7890"
    export HTTP_PROXY="http://127.0.0.1:7890"
    echo -e "   ${GREEN}✓ 已自动挂载本地代理 (127.0.0.1:7890)${NC} -> 保证 Google Gemini AI 高速连通"
  elif lsof -i :7897 >/dev/null 2>&1; then
    export HTTPS_PROXY="http://127.0.0.1:7897"
    export HTTP_PROXY="http://127.0.0.1:7897"
    echo -e "   ${GREEN}✓ 已自动挂载本地代理 (127.0.0.1:7897)${NC} -> 保证 Google Gemini AI 高速连通"
  fi

  # 5. 检查前端依赖是否已安装
  if [ ! -d "$FRONTEND_DIR/node_modules" ]; then
    echo -e "${YELLOW}📦 前端依赖未安装，正在执行 ${PKG_MGR} install...${NC}"
    (cd "$FRONTEND_DIR" && $PKG_MGR install)
  fi
}

# ------------------------------------------------------------------------------
# 服务停止
# ------------------------------------------------------------------------------
stop_services() {
  echo -e "${YELLOW}🛑 正在停止 AMR/AGV 调度平台所有服务...${NC}"

  # 通过 PID 文件终止
  if [ -f "$RUN_DIR/backend.pid" ]; then
    local bpid
    bpid=$(cat "$RUN_DIR/backend.pid" 2>/dev/null || true)
    if [ -n "$bpid" ] && kill -0 "$bpid" 2>/dev/null; then
      echo -e "   终止后端服务 (PID: $bpid)..."
      kill -15 "$bpid" 2>/dev/null || true
    fi
    rm -f "$RUN_DIR/backend.pid"
  fi

  if [ -f "$RUN_DIR/frontend.pid" ]; then
    local fpid
    fpid=$(cat "$RUN_DIR/frontend.pid" 2>/dev/null || true)
    if [ -n "$fpid" ] && kill -0 "$fpid" 2>/dev/null; then
      echo -e "   终止前端服务 (PID: $fpid)..."
      kill -15 "$fpid" 2>/dev/null || true
    fi
    rm -f "$RUN_DIR/frontend.pid"
  fi

  # 通过端口兜底检查并清理
  kill_port $BACKEND_PORT
  kill_port $FRONTEND_PORT

  # 清理任何残留的 Fms.HttpApi.Host 进程
  pkill -f "Fms.HttpApi.Host.dll" 2>/dev/null || true

  echo -e "${GREEN}✓ 所有服务已完全停止。${NC}"
}

# ------------------------------------------------------------------------------
# 服务状态查看
# ------------------------------------------------------------------------------
status_services() {
  echo -e "${BOLD}${CYAN}================================================================${NC}"
  echo -e "${BOLD}       AMR/AGV 智能调度与数字孪生平台 运行状态看板       ${NC}"
  echo -e "${BOLD}${CYAN}================================================================${NC}"

  local b_pid
  b_pid=$(check_port $BACKEND_PORT)
  if [ -n "$b_pid" ]; then
    echo -e " ⚙️  ${GREEN}后端核心服务 [运行中]${NC} - 端口: ${BACKEND_PORT} | PID: ${b_pid}"
    echo -e "     接口文档: http://127.0.0.1:${BACKEND_PORT}/swagger"
  else
    echo -e " ⚙️  ${RED}后端核心服务 [未运行]${NC} - 端口: ${BACKEND_PORT}"
  fi

  local f_pid
  f_pid=$(check_port $FRONTEND_PORT)
  if [ -n "$f_pid" ]; then
    echo -e " 🖥️  ${GREEN}前端调度大屏 [运行中]${NC} - 端口: ${FRONTEND_PORT} | PID: ${f_pid}"
    echo -e "     访问地址: http://localhost:${FRONTEND_PORT}"
  else
    echo -e " 🖥️  ${RED}前端调度大屏 [未运行]${NC} - 端口: ${FRONTEND_PORT}"
  fi

  echo -e "${CYAN}----------------------------------------------------------------${NC}"
  if [ -n "$b_pid" ] && [ -n "$f_pid" ]; then
    echo -e "${GREEN}🎉 系统全栈处于健康运行状态！${NC}"
  else
    echo -e "${YELLOW}💡 提示: 可执行 ./start.sh 快速启动全套服务。${NC}"
  fi
  echo -e "${BOLD}${CYAN}================================================================${NC}"
}

# ------------------------------------------------------------------------------
# 实时日志追踪
# ------------------------------------------------------------------------------
view_logs() {
  echo -e "${CYAN}📄 正在实时监听前后端运行日志 (按 Ctrl+C 退出日志监控)...${NC}"
  touch "$LOG_DIR/backend.log" "$LOG_DIR/frontend.log"
  tail -f "$LOG_DIR/backend.log" "$LOG_DIR/frontend.log"
}

# ------------------------------------------------------------------------------
# 打印启动就绪信息看板
# ------------------------------------------------------------------------------
print_banner() {
  echo ""
  echo -e "${BOLD}${GREEN}========================================================================${NC}"
  echo -e "${BOLD}${GREEN}  🚀 AMR / AGV 智能调度与数字孪生仿真平台 (FMS) 已全部启动成功！     ${NC}"
  echo -e "${BOLD}${GREEN}========================================================================${NC}"
  echo -e "  🖥️  ${BOLD}调度大屏与系统前端 :${NC} ${CYAN}http://localhost:${FRONTEND_PORT}${NC}"
  echo -e "  ⚙️  ${BOLD}后端 API 与 Swagger :${NC} ${CYAN}http://127.0.0.1:${BACKEND_PORT}/swagger${NC}"
  echo -e "  📡  ${BOLD}SignalR 实时孪生通道:${NC} ${CYAN}ws://127.0.0.1:${BACKEND_PORT}/hubs/simulation${NC}"
  echo -e "  🧠  ${BOLD}AI 智能排障与调度助手:${NC} ${CYAN}http://localhost:${FRONTEND_PORT}/logs/ai${NC}"
  echo -e "  🔑  ${BOLD}大模型与接口密钥配置 :${NC} ${CYAN}http://localhost:${FRONTEND_PORT}/logs/openapi${NC}"
  echo -e "  ----------------------------------------------------------------------"
  echo -e "  👤 ${BOLD}系统预设演示账号:${NC}"
  echo -e "     - ${BOLD}系统管理员:${NC}  ${GREEN}admin${NC}    /  ${GREEN}admin123${NC}      (全部系统与调度权限)"
  echo -e "     - ${BOLD}调度操作员:${NC}  ${GREEN}operator${NC} /  ${GREEN}operator123${NC}   (下发任务/呼叫搬运)"
  echo -e "     - ${BOLD}安全管理员:${NC}  ${GREEN}safety${NC}   /  ${GREEN}safety123${NC}     (急停与安全互锁)"
  echo -e "     - ${BOLD}监控观察员:${NC}  ${GREEN}viewer${NC}   /  ${GREEN}viewer123${NC}     (只读查看大屏孪生)"
  echo -e "${BOLD}${GREEN}========================================================================${NC}"
  echo ""
}

# ------------------------------------------------------------------------------
# 核心启动流程
# ------------------------------------------------------------------------------
start_all() {
  local daemon_mode=$1

  check_prerequisites

  # 清理残留端口
  kill_port $BACKEND_PORT
  kill_port $FRONTEND_PORT

  echo -e "${CYAN}🚀 [2/3] 启动后端调度核心 (.NET 8)...${NC}"
  if [ "$daemon_mode" = true ]; then
    nohup dotnet run --project "$BACKEND_CSPROJ" --launch-profile http > "$LOG_DIR/backend.log" 2>&1 &
    BACKEND_PID=$!
    echo $BACKEND_PID > "$RUN_DIR/backend.pid"
    echo -e "   ${GREEN}✓ 后端已转入后台运行 (PID: $BACKEND_PID，日志: .run/logs/backend.log)${NC}"
  else
    # 前台启动准备
    dotnet run --project "$BACKEND_CSPROJ" --launch-profile http > "$LOG_DIR/backend.log" 2>&1 &
    BACKEND_PID=$!
    echo $BACKEND_PID > "$RUN_DIR/backend.pid"
  fi

  # 等待后端接口就绪 (健康探测)
  echo -ne "   ⏳ 正在等待后端就绪"
  local attempts=0
  local backend_ready=false
  while [ $attempts -lt 30 ]; do
    if curl -s "http://127.0.0.1:${BACKEND_PORT}/swagger/index.html" >/dev/null 2>&1 || curl -s "http://127.0.0.1:${BACKEND_PORT}/api" >/dev/null 2>&1 || [ -n "$(check_port $BACKEND_PORT)" ]; then
      backend_ready=true
      break
    fi
    echo -ne "."
    sleep 1
    attempts=$((attempts + 1))
  done
  echo ""

  if [ "$backend_ready" = true ]; then
    echo -e "   ${GREEN}✓ 后端服务已就绪！ (http://127.0.0.1:${BACKEND_PORT})${NC}"
  else
    echo -e "   ${YELLOW}⚠️  后端仍在加载中，前端将先行启动...${NC}"
  fi

  echo -e "${CYAN}🚀 [3/3] 启动前端调度大屏 (Vue 3 + Vite)...${NC}"
  if [ "$daemon_mode" = true ]; then
    (cd "$FRONTEND_DIR" && nohup $PKG_MGR dev > "$LOG_DIR/frontend.log" 2>&1 & echo $! > "$RUN_DIR/frontend.pid")
    FRONTEND_PID=$(cat "$RUN_DIR/frontend.pid")
    echo -e "   ${GREEN}✓ 前端已转入后台运行 (PID: $FRONTEND_PID，日志: .run/logs/frontend.log)${NC}"

    print_banner
    echo -e "${BLUE}💡 常用运维指令:${NC}"
    echo -e "   - 查看服务状态 : ${BOLD}./start.sh status${NC}"
    echo -e "   - 实时查看日志 : ${BOLD}./start.sh logs${NC}"
    echo -e "   - 停止所有服务 : ${BOLD}./start.sh stop${NC} 或 ${BOLD}./stop.sh${NC}"
    echo -e "   - 重启所有服务 : ${BOLD}./start.sh restart${NC}"
    echo ""

    # macOS 自动打开浏览器
    if command -v open >/dev/null 2>&1; then
      sleep 2
      open "http://localhost:${FRONTEND_PORT}" 2>/dev/null || true
    fi
  else
    # 前台模式
    (cd "$FRONTEND_DIR" && $PKG_MGR dev > "$LOG_DIR/frontend.log" 2>&1 & echo $! > "$RUN_DIR/frontend.pid")
    FRONTEND_PID=$(cat "$RUN_DIR/frontend.pid")

    print_banner
    echo -e "${YELLOW}⚡ 当前处于前台交互模式 (日志同时记录在 .run/logs/ 中)${NC}"
    echo -e "${BOLD}${RED}👉 按 Ctrl+C 可一键安全退出并关闭所有前后端服务${NC}"
    echo ""

    # 捕捉退出信号，清理所有子进程
    cleanup_foreground() {
      echo ""
      echo -e "${YELLOW}🛑 收到退出信号，正在关闭系统...${NC}"
      stop_services
      exit 0
    }
    trap cleanup_foreground SIGINT SIGTERM EXIT

    # macOS 自动打开浏览器
    if command -v open >/dev/null 2>&1; then
      (sleep 2 && open "http://localhost:${FRONTEND_PORT}" 2>/dev/null) &
    fi

    # 持续追踪输出日志
    tail -f "$LOG_DIR/backend.log" "$LOG_DIR/frontend.log"
  fi
}

# ------------------------------------------------------------------------------
# 命令行参数解析
# ------------------------------------------------------------------------------
ACTION="foreground"

case "$1" in
  -d|--daemon|daemon)
    ACTION="daemon"
    ;;
  stop|down)
    stop_services
    exit 0
    ;;
  restart)
    stop_services
    sleep 1
    start_all true
    exit 0
    ;;
  status)
    status_services
    exit 0
    ;;
  logs|log)
    view_logs
    exit 0
    ;;
  -h|--help|help)
    echo "使用方法:"
    echo "  ./start.sh            前台交互启动 (按 Ctrl+C 安全停止)"
    echo "  ./start.sh -d         后台常驻启动 (守护进程)"
    echo "  ./start.sh stop       停止所有服务"
    echo "  ./start.sh restart    重启所有服务"
    echo "  ./start.sh status     查看运行状态"
    echo "  ./start.sh logs       查看实时日志"
    exit 0
    ;;
  *)
    ACTION="foreground"
    ;;
esac

if [ "$ACTION" = "daemon" ]; then
  start_all true
else
  start_all false
fi
