# AGV / AMR 调度仿真与算法评测基准套件 (Benchmark Suite)

本目录包含高并发、窄道对头死锁、三次贝塞尔曲率平滑以及多车型混合交通的声明式评测基准。

## 目录结构

- `maps/`
  - `siasun_mapinfo.json`: 新松标准拓扑地图（431KB），包含完整的三次贝塞尔平滑倒角与分支拐角。
  - `kecong_demo.xmap`: 科聪标准 XML 地图（52MB），包含 60 万激光雷达点云与微元圆弧拓扑。
- `scenarios/`
  - `cleanroom_narrow_aisle.yaml`: 半导体洁净室窄道对头死锁基准（AMR-101 与 AMR-102 互冲，验证等待图闭环检测与支线避让）。
  - `five_amr_cleanroom_throughput.yaml`: 5 车高并发网格吞吐量基准。

## 运行评测

### 1. 自动化单元与仿真评测 (CLI)
```bash
cd backend
dotnet test
```

### 2. 通过 FMS 仿真控制台 API 执行
```bash
curl -X POST http://localhost:9999/api/fms/simulation/run-scenario \
  -H "Content-Type: application/x-yaml" \
  --data-binary @benchmarks/scenarios/cleanroom_narrow_aisle.yaml
```

### 3. 通过 Web 前端界面实时可视化评测
在浏览器中打开 `http://localhost:9999/simulation`，点击 **"执行 2-AMR 窄道对头死锁自动评测"**，可在 60FPS PixiJS 态势图谱中实时观察避让动作与时间轴指标。
