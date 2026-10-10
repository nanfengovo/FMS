# AGV / AMR 智能集群调度与数字孪生仿真平台 (FMS Platform)

一套工业级、高性能、模块化单体架构的自主移动机器人 (AMR/AGV) 调度管理与数字孪生仿真评测系统。

- **后端架构**: .NET 10.0 + ABP vNext 模块化单体分层架构 (Clean Architecture)，核心调度内核采用 100% 内存驱动 + `System.Threading.Channels` 异步无锁流式写入。
- **前端架构**: Vue 3 + Soybean Admin 风格设计规范 (Naive UI + UnoCSS + Pinia) + **PixiJS v8** 60FPS WebGL 硬件加速矢量地图与车辆动态位姿平滑插值渲染 (LERP)。
- **硬件适配协议**:
  - **科聪 (Kecong MRC/KCFMS)**: HTTP REST 协议规范。
  - **欧洲工业标准 VDA 5050 v2.0**: 基于 MQTT 的规范化订单、即时动作与状态遥测。
  - **虚拟化驱动 (Simulated AMR)**: 纯内存运动学积分动力学仿真器。
- **上层系统对接**: 严格按照新松及 RCS 外部接口文档标准规范实现，作为服务端侦听 `9999` 端口，向上层 RCS (`9000` 端口) 提供带任务类型分流前缀 (`/PA`) 的双向异步回调。
- **低代码工艺编排**: 借鉴仙工 (Seer) 可视化 DAG 动作流，支持取料、放料、等待、条件分支与自动回充动作。

---

## 核心系统模块结构

```text
AGV_AMR_FmsPlatform/
├── backend/                              # .NET 10 调度后端解决方案 (FmsPlatform.sln)
│   ├── src/
│   │   ├── Fms.Domain.Shared/            # 几何基础 (Point2D, Pose2D, BezierMath 导数/曲率/弧长积分), 领域枚举
│   │   ├── Fms.Domain/                   # 拓扑图、三次贝塞尔路径、互斥锁区、车辆聚合根、BMS模型、DAG工作流
│   │   ├── Fms.Algorithm/                # A* 路由、Wait-For Graph 死锁圈检测、支线避让 (Avoidance Solver)
│   │   ├── Fms.Engine/                   # 虚拟运动学解算器、中央内存调度引擎、场景自动化测试器 (ScenarioRunner)
│   │   ├── Fms.Adapters.Simulated/       # 纯内存仿真 AMR 硬件驱动适配器
│   │   ├── Fms.Adapters.Kecong/          # 科聪 MRC HTTP 驱动适配器
│   │   ├── Fms.Adapters.Vda5050/         # VDA 5050 MQTT 驱动适配器
│   │   ├── Fms.Application/              # XMAP 解析器 (支持 60 万激光点云)、RCS 9000 端口多前缀回调服务
│   │   ├── Fms.EntityFrameworkCore/      # SQLite / EF Core 异步批量落库与事件溯源持久化骨架
│   │   └── Fms.HttpApi.Host/             # 9999 端口 Host: RCS 接口、仿真控制 API、SignalR 10Hz 推送、静态托管
│   └── test/
│       └── Fms.Tests/                    # 贝塞尔数学、A* 路径、死锁避让、XMAP、场景推演 xUnit 单元测试套件
├── frontend/                             # Vue 3 + Soybean Admin + PixiJS v8 态势感知大屏与评测工作台
│   ├── src/
│   │   ├── components/MapCanvas.vue      # PixiJS v8 60FPS WebGL 视口: 贝塞尔平滑渲染、互斥锁区半透明高亮、车辆 LERP
│   │   ├── views/monitor/                # 态势大屏监控 (实时数字孪生、车辆卡片、任务进度、冲突告警)
│   │   ├── views/simulation/             # 仿真与测试控制台 (DES / 时间轮、速度切换、车辆生成、故障注入、任务下发)
│   │   ├── views/map-workbench/          # 地图与拓扑工作台 (贝塞尔曲率检查、图层控制、XMAP/mapinfo.json 上传)
│   │   └── views/workflow/               # 仙工低代码 DAG 动作流编排器
├── benchmarks/                           # 自动化基准测试用例集
│   ├── maps/                             # 真实工业地图 (SIASUN 贝塞尔地图, 科聪 52MB XMAP 点云地图)
│   └── scenarios/                        # 窄道对头死锁基准、5-AMR 高并发网格吞吐量基准 YAML
```

---

## 快速启动指南

### 1. 启动后端引擎 (默认托管前端 SPA)
```bash
cd backend
dotnet build
dotnet test
dotnet run --project src/Fms.HttpApi.Host/Fms.HttpApi.Host.csproj
```
后端服务将在 `http://localhost:9999` 启动：
- **Web 可视化控制台**: `http://localhost:9999/`
- **Swagger API 文档**: `http://localhost:9999/swagger`
- **SignalR 实时管道**: `http://localhost:9999/hubs/fms`

### 2. 启动前端独立开发服务器 (可选，用于热重载开发)
```bash
cd frontend
pnpm install
pnpm dev
```
打开 `http://localhost:5173/` 即可进行前端热重载开发，API 和 WebSocket 均已配置自动代理至 `9999` 端口。

---

## 外部 RCS 调度接口说明 (`调度外部http接口.pdf`)

调度系统侦听 `9999` 端口，上层 RCS 侦听 `9000` 端口。

| 接口分类 | 路径 | 方法 | 功能描述 |
| :--- | :--- | :--- | :--- |
| **RCS 调调度** | `/api/xinsong/heart_beat?timestamp={ts}` | GET | 心跳保活与时间戳对齐 |
| **RCS 调调度** | `/api/v1/xinsong/task_add` | POST | 批量下发物料搬运与工艺动作任务 |
| **RCS 调调度** | `/api/v1/xinsong/task_delete` | POST | 终止或取消指定流水号任务 |
| **调度回写 RCS** | `/{type}/api/v1/xinsong/task_info` | POST | 任务状态节点跳变汇报 (带工艺前缀如 `/PA`) |
| **调度回写 RCS** | `/{type}/api/v1/xinsong/report_robot_status` | POST | 1 秒周期车队电量与位姿全量汇报 |
| **调度回写 RCS** | `/{type}/api/v1/xinsong/device_ctrl` | POST | 升降机/风淋门/对接机台工艺联锁请求 |
| **调度回写 RCS** | `/{type}/api/v1/xinsong/stk_apply_info` | POST | STK 自动立库仓位预约与申请 |

---

## 自动化算法测试与基准推演

### 窄道对头死锁避让基准 (Cleanroom Narrow Aisle)
在窄道单行线场景中，两台 AMR 相对行驶产生对头冲突。系统内置的死锁检测器 (Wait-For Graph) 在前瞻锁区申请失败时立即触发仲裁器，低优先级车辆主动挂载 Pull-over 支线避让策略，拐入临时停靠 spur 点；高优先级车辆顺利通行后，低优先级车辆恢复主线导航，实现 **0 碰撞、0 人工介入、0 永久死锁**。

运行基准：
```bash
curl -X POST http://localhost:9999/api/fms/simulation/run-scenario \
  -H "Content-Type: application/x-yaml" \
  --data-binary @benchmarks/scenarios/cleanroom_narrow_aisle.yaml
```
输出指标示例：
```json
{
  "scenarioId": "SCN-NARROW-AISLE-01",
  "totalOrdersSubmitted": 2,
  "totalOrdersCompleted": 1,
  "durationSec": 30,
  "throughputPerHour": 120,
  "conflictCount": 153,
  "success": true
}
```
