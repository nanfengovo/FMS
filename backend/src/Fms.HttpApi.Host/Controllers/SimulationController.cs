using Fms.Domain.Identity;
using Fms.HttpApi.Host.Authorization;
using Fms.Application;
using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Topology;
using Fms.Domain.Vehicles;
using Fms.Engine;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fms.HttpApi.Host.Controllers;

[Authorize]
[ApiController]
[Route("api/fms/simulation")]
public class SimulationController : ControllerBase
{
    private readonly FmsAppService _fmsAppService;

    public SimulationController(FmsAppService fmsAppService)
    {
        _fmsAppService = fmsAppService;
    }

    [RequiresPermission(FmsPermissions.Simulation)]
    [HttpGet("state")]
    public IActionResult GetState()
    {
        var engine = _fmsAppService.Engine;
        return Ok(new
        {
            timeMs = engine.Clock.CurrentTimestampMs,
            timeScale = engine.Clock.TimeScale,
            isPaused = engine.Clock.IsPaused,
            vehicles = Realtime.VehicleSnapshotBuilder.Build(engine),
            activeOrders = Realtime.OrderSnapshotBuilder.Build(engine),
            orders = engine.Orders.Values.Select(o => new
            {
                id = o.Id,
                serial = o.OrderSerial,
                status = o.Status.ToString(),
                priority = o.Priority,
                assignedVehicle = o.AssignedVehicleId,
                currentStep = o.CurrentStepIndex,
                totalSteps = o.Steps.Count
            }),
            activeConflicts = engine.Vehicles.Values
                .Where(v => v.CurrentAvoidanceStrategy != AvoidanceStrategy.None)
                .Select(v => new
                {
                    vehicleId = v.Id,
                    strategy = v.CurrentAvoidanceStrategy.ToString(),
                    targetSpur = v.AvoidanceTargetStationId
                })
        });
    }

    [Authorize]
    [RequiresPermission(FmsPermissions.SimulationControl)]
    [HttpPost("pause")]
    public IActionResult Pause()
    {
        _fmsAppService.PauseSimulation();
        return Ok(new { success = true });
    }

    [Authorize]
    [RequiresPermission(FmsPermissions.SimulationControl)]
    [HttpPost("resume")]
    public IActionResult Resume()
    {
        _fmsAppService.ResumeSimulation();
        return Ok(new { success = true });
    }

    [Authorize]
    [RequiresPermission(FmsPermissions.SimulationControl)]
    [HttpPost("timescale")]
    public IActionResult SetTimeScale([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SetScaleDto? dto, [FromQuery] double? scale)
    {
        double targetScale = dto?.Scale ?? scale ?? 1.0;
        _fmsAppService.SetTimeScale(targetScale);
        return Ok(new { success = true, scale = _fmsAppService.Engine.Clock.TimeScale });
    }

    [Authorize]
    [RequiresPermission(FmsPermissions.SimulationControl)]
    [HttpPost("step")]
    public IActionResult Step([FromQuery] double dt = 0.1)
    {
        _fmsAppService.StepSimulation(dt);
        return Ok(new { success = true });
    }

    [Authorize]
    [RequiresPermission(FmsPermissions.SimulationControl)]
    [HttpPost("spawn-vehicle")]
    public IActionResult SpawnVehicle([FromBody] SpawnVehicleDto dto)
    {
        double x = dto.X;
        double y = dto.Y;
        double theta = dto.ThetaDeg * Math.PI / 180.0;

        // 如果提供了 initialNodeId 且未显式指定非零坐标，则根据拓扑地图节点计算初始位姿
        if (!string.IsNullOrEmpty(dto.InitialNodeId) && Math.Abs(x) < 0.0001 && Math.Abs(y) < 0.0001)
        {
            var node = _fmsAppService.Engine.Graph.GetNode(dto.InitialNodeId);
            if (node != null)
            {
                x = node.Position.X;
                y = node.Position.Y;
            }
        }

        var v = new VehicleAggregate(dto.Id, dto.Name ?? dto.Id, new Pose2D(x, y, theta));
        v.Kinematics.MaxSpeed = dto.MaxSpeed > 0 ? dto.MaxSpeed : 1.5;
        if (!string.IsNullOrEmpty(dto.InitialNodeId))
        {
            v.CurrentNodeId = dto.InitialNodeId;
        }
        _fmsAppService.Engine.RegisterVehicle(v);
        return Ok(new { success = true, vehicleId = v.Id });
    }

    [Authorize]
    [RequiresPermission(FmsPermissions.SimulationInjectFault)]
    [HttpPost("inject-fault")]
    public IActionResult InjectFault([FromBody] InjectFaultDto dto)
    {
        if (_fmsAppService.Engine.Vehicles.ContainsKey(dto.VehicleId))
        {
            _fmsAppService.Engine.InjectVehicleFault(dto.VehicleId, dto.FaultType ?? "");
            return Ok(new { success = true });
        }
        return NotFound(new { error = $"Vehicle {dto.VehicleId} not found." });
    }

    [RequiresPermission(FmsPermissions.SimulationInjectFault)]
    [HttpPost("run-scenario")]
    public async Task<IActionResult> RunScenario()
    {
        ScenarioDefinition? scenario = null;
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(body))
        {
            return BadRequest(new { error = "Request body is empty" });
        }

        try
        {
            if (Request.ContentType != null && Request.ContentType.Contains("json"))
            {
                scenario = System.Text.Json.JsonSerializer.Deserialize<ScenarioDefinition>(body, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            else
            {
                scenario = ScenarioRunner.ParseYaml(body);
            }
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Failed to parse scenario: {ex.Message}" });
        }

        if (scenario == null)
        {
            return BadRequest(new { error = "Failed to deserialize scenario" });
        }

        var runner = new ScenarioRunner();
        var result = runner.RunScenario(_fmsAppService.Engine, scenario);
        return Ok(result);
    }

    /// <summary>
    /// 获取全量任务列表及每项任务的详细执行步骤流水线
    /// </summary>
    [RequiresPermission(FmsPermissions.Orders)]
    [HttpGet("orders")]
    public IActionResult GetOrders()
    {
        var orders = _fmsAppService.Engine.Orders.Values
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                id = o.Id,
                serial = o.OrderSerial,
                bulkTaskType = o.BulkTaskType,
                taskType = o.TaskType,
                priority = o.Priority,
                status = o.Status.ToString(),
                assignedVehicle = o.AssignedVehicleId,
                bindVehicleId = o.BindVehicleId,
                sourceNodeId = o.Steps.FirstOrDefault(st => st.Action == ActionType.LiftUp && !string.IsNullOrEmpty(st.TargetNodeId))?.TargetNodeId ?? string.Empty,
                targetNodeId = o.Steps.LastOrDefault(st => !string.IsNullOrEmpty(st.TargetNodeId))?.TargetNodeId ?? string.Empty,
                currentStepIndex = o.CurrentStepIndex,
                totalSteps = o.Steps.Count,
                createdAt = o.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                startedAt = o.StartedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                completedAt = o.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                walkCostSec = Math.Round(o.TotalWalkTimeCostSec, 1),
                cargoCostSec = Math.Round(o.TotalCargoActionTimeCostSec, 1),
                failReason = o.FailReason,
                subNo = o.SubNo,
                taskCodeA = o.TaskCodeA,
                taskCodeB = o.TaskCodeB,
                finishingAction = o.FinishingAction,
                cargoSerial = o.CargoSerial,
                armAction = o.ArmAction,
                rfid = o.Rfid,
                mark = o.Mark,
                optionCode = o.OptionCode,
                passPoint = o.PassPoint,
                passPointToGoal = o.PassPointToGoal,
                storage = o.Storage,
                target = o.Target,
                mileageKm = Math.Round(o.MileageKm, 2),
                detailedSteps = o.DetailedSteps.Select(ds => new
                {
                    no = ds.No,
                    name = ds.Name,
                    type = ds.Type,
                    status = ds.Status,
                    parameter = ds.Parameter,
                    finishedAt = ds.FinishedAt?.ToString("yyyy-MM-dd HH:mm:ss")
                }),
                steps = o.Steps.Select(s => new
                {
                    stepIndex = s.StepIndex,
                    name = s.Name,
                    action = s.Action.ToString(),
                    targetNodeId = s.TargetNodeId,
                    optionCode = s.OptionCode,
                    cargoId = s.CargoId,
                    goalAction = s.GoalAction,
                    dwellSec = s.DwellSec,
                    isCompleted = s.IsCompleted,
                    startedAt = s.StartedAt?.ToString("HH:mm:ss"),
                    finishedAt = s.FinishedAt?.ToString("HH:mm:ss"),
                    isActionPermitted = s.IsActionPermitted,
                    isRfidVerified = s.IsRfidVerified
                })
            });

        return Ok(new { success = true, data = orders });
    }

    /// <summary>
    /// 获取单个任务详情
    /// </summary>
    [RequiresPermission(FmsPermissions.Orders)]
    [HttpGet("orders/{id}")]
    public IActionResult GetOrderDetail(string id)
    {
        var o = _fmsAppService.Engine.Orders.Values.FirstOrDefault(x => x.Id == id || x.OrderSerial == id);
        if (o == null) return NotFound(new { error = $"Order {id} not found" });

        return Ok(new
        {
            success = true,
            data = new
            {
                id = o.Id,
                serial = o.OrderSerial,
                bulkTaskType = o.BulkTaskType,
                taskType = o.TaskType,
                priority = o.Priority,
                status = o.Status.ToString(),
                assignedVehicle = o.AssignedVehicleId,
                bindVehicleId = o.BindVehicleId,
                currentStepIndex = o.CurrentStepIndex,
                totalSteps = o.Steps.Count,
                createdAt = o.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                startedAt = o.StartedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                completedAt = o.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                walkCostSec = Math.Round(o.TotalWalkTimeCostSec, 1),
                cargoCostSec = Math.Round(o.TotalCargoActionTimeCostSec, 1),
                failReason = o.FailReason,
                subNo = o.SubNo,
                taskCodeA = o.TaskCodeA,
                taskCodeB = o.TaskCodeB,
                finishingAction = o.FinishingAction,
                cargoSerial = o.CargoSerial,
                armAction = o.ArmAction,
                rfid = o.Rfid,
                mark = o.Mark,
                optionCode = o.OptionCode,
                passPoint = o.PassPoint,
                passPointToGoal = o.PassPointToGoal,
                storage = o.Storage,
                target = o.Target,
                mileageKm = Math.Round(o.MileageKm, 2),
                detailedSteps = o.DetailedSteps.Select(ds => new
                {
                    no = ds.No,
                    name = ds.Name,
                    type = ds.Type,
                    status = ds.Status,
                    parameter = ds.Parameter,
                    finishedAt = ds.FinishedAt?.ToString("yyyy-MM-dd HH:mm:ss")
                }),
                steps = o.Steps.Select(s => new
                {
                    stepIndex = s.StepIndex,
                    name = s.Name,
                    action = s.Action.ToString(),
                    targetNodeId = s.TargetNodeId,
                    optionCode = s.OptionCode,
                    cargoId = s.CargoId,
                    goalAction = s.GoalAction,
                    dwellSec = s.DwellSec,
                    isCompleted = s.IsCompleted,
                    startedAt = s.StartedAt?.ToString("HH:mm:ss"),
                    finishedAt = s.FinishedAt?.ToString("HH:mm:ss"),
                    isActionPermitted = s.IsActionPermitted,
                    isRfidVerified = s.IsRfidVerified
                })
            }
        });
    }

    /// <summary>
    /// 内部创建调度任务 (单点导航 / 点到点搬运 / 指定充电)
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.OrdersCreate)]
    [HttpPost("orders")]
    public IActionResult CreateOrder([FromBody] CreateOrderDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.TargetNodeId))
        {
            return BadRequest(new { error = "TargetNodeId 不能为空" });
        }

        var engine = _fmsAppService.Engine;
        var kind = dto.OrderType ?? "Transport";
        bool isTransport = !kind.Equals("Nav", StringComparison.OrdinalIgnoreCase) && !kind.Equals("Charge", StringComparison.OrdinalIgnoreCase);

        if (!engine.HasNode(dto.TargetNodeId))
        {
            return BadRequest(new { success = false, error = $"目标站点 {dto.TargetNodeId} 不在当前地图上" });
        }
        if (isTransport)
        {
            if (string.IsNullOrWhiteSpace(dto.SourceNodeId))
            {
                return BadRequest(new { success = false, error = "搬运任务需要选择起点站点" });
            }
            if (!engine.HasNode(dto.SourceNodeId))
            {
                return BadRequest(new { success = false, error = $"起点站点 {dto.SourceNodeId} 不在当前地图上" });
            }
        }

        string? bindVehicle = null;
        if (!string.IsNullOrWhiteSpace(dto.VehicleId))
        {
            bindVehicle = engine.ResolveVehicleId(dto.VehicleId);
            if (bindVehicle == null)
            {
                return BadRequest(new { success = false, error = $"找不到车辆 {dto.VehicleId}" });
            }
        }

        string serial = string.IsNullOrWhiteSpace(dto.TaskSerial)
            ? $"INT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}"
            : dto.TaskSerial;

        if (engine.Orders.Values.Any(o => o.OrderSerial == serial && !o.IsFinished))
        {
            return Conflict(new { success = false, error = $"任务编号 {serial} 已存在且尚未结束" });
        }

        var order = new TransportOrderAggregate(serial, dto.Priority, bindVehicle)
        {
            TaskType = dto.OrderType ?? "Transport",
            BulkTaskType = "internal"
        };

        if (dto.OrderType?.Equals("Nav", StringComparison.OrdinalIgnoreCase) == true)
        {
            // 单点导航流水线
            order.Steps.Add(new OrderStep
            {
                StepIndex = 0,
                Name = $"1. 航线规划并驶向工位 {dto.TargetNodeId}",
                Action = ActionType.Move,
                TargetNodeId = dto.TargetNodeId
            });
            order.Steps.Add(new OrderStep
            {
                StepIndex = 1,
                Name = $"2. 工位精准对齐与就绪就位",
                Action = ActionType.Wait,
                TargetNodeId = dto.TargetNodeId,
                DwellSec = 2.0
            });
        }
        else if (dto.OrderType?.Equals("Charge", StringComparison.OrdinalIgnoreCase) == true)
        {
            // 自动回充流水线
            order.Steps.Add(new OrderStep
            {
                StepIndex = 0,
                Name = $"1. 驶向充电桩工位 {dto.TargetNodeId}",
                Action = ActionType.Move,
                TargetNodeId = dto.TargetNodeId
            });
            order.Steps.Add(new OrderStep
            {
                StepIndex = 1,
                Name = $"2. 自动对接电极伸缩并充能至85%",
                Action = ActionType.Charge,
                TargetNodeId = dto.TargetNodeId
            });
        }
        else
        {
            // 标准工业点到点搬运流水线 (Step 1 ~ Step 5)
            string sourceNode = dto.SourceNodeId!;
            string cargo = string.IsNullOrWhiteSpace(dto.CargoId) ? $"PALLET-{Guid.NewGuid().ToString("N")[..4].ToUpper()}" : dto.CargoId;

            order.Steps.Add(new OrderStep
            {
                StepIndex = 0,
                Name = $"1. 空载驶向起点工位 {sourceNode}",
                Action = ActionType.Move,
                TargetNodeId = sourceNode
            });
            order.Steps.Add(new OrderStep
            {
                StepIndex = 1,
                Name = $"2. 起点工位对接与顶升取货 [{cargo}]",
                Action = ActionType.LiftUp,
                GoalAction = 1,
                CargoId = cargo,
                TargetNodeId = sourceNode,
                DwellSec = 3.5
            });
            order.Steps.Add(new OrderStep
            {
                StepIndex = 2,
                Name = $"3. 重载转运驶向终点工位 {dto.TargetNodeId}",
                Action = ActionType.Move,
                TargetNodeId = dto.TargetNodeId
            });
            order.Steps.Add(new OrderStep
            {
                StepIndex = 3,
                Name = $"4. 终点工位精准对齐并卸下托盘",
                Action = ActionType.PutDown,
                GoalAction = 2,
                CargoId = cargo,
                TargetNodeId = dto.TargetNodeId,
                DwellSec = 3.5
            });
            order.Steps.Add(new OrderStep
            {
                StepIndex = 4,
                Name = $"5. 任务交接完成与状态归档",
                Action = ActionType.Wait,
                TargetNodeId = dto.TargetNodeId,
                DwellSec = 1.5
            });
        }

        _fmsAppService.Engine.SubmitOrder(order);
        return Ok(new { success = true, orderId = order.Id, orderSerial = order.OrderSerial });
    }

    /// <summary>
    /// 取消或中止任务
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.OrdersCancel)]
    [HttpPost("orders/{id}/cancel")]
    public IActionResult CancelOrder(string id)
    {
        var o = _fmsAppService.Engine.Orders.Values.FirstOrDefault(x => x.Id == id || x.OrderSerial == id);
        if (o == null) return NotFound(new { error = $"Order {id} not found" });

        _fmsAppService.Engine.CancelOrder(o.Id, "由调度员手动中止任务");
        return Ok(new { success = true, msg = $"任务 {o.OrderSerial} 已取消" });
    }

    /// <summary>
    /// 暂停或恢复任务
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.OrdersPause)]
    [HttpPost("orders/{id}/toggle-pause")]
    public IActionResult TogglePauseOrder(string id)
    {
        var o = _fmsAppService.Engine.Orders.Values.FirstOrDefault(x => x.Id == id || x.OrderSerial == id);
        if (o == null) return NotFound(new { error = $"Order {id} not found" });

        _fmsAppService.Engine.TogglePauseOrder(o.Id);
        bool isPaused = false;
        if (!string.IsNullOrEmpty(o.AssignedVehicleId) && _fmsAppService.Engine.Vehicles.TryGetValue(o.AssignedVehicleId, out var v))
        {
            isPaused = !v.IsPaused;
        }
        return Ok(new { success = true, isPaused });
    }

    /// <summary>
    /// 手动跳步 / 强制步进下一流程
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.OrdersSkip)]
    [HttpPost("orders/{id}/skip-step")]
    public IActionResult SkipStepOrder(string id)
    {
        var o = _fmsAppService.Engine.Orders.Values.FirstOrDefault(x => x.Id == id || x.OrderSerial == id);
        if (o == null) return NotFound(new { error = $"Order {id} not found" });

        _fmsAppService.Engine.SkipStep(o.Id);
        return Ok(new { success = true, currentStep = o.CurrentStepIndex, totalSteps = o.Steps.Count });
    }

    /// <summary>
    /// 重置当前工步 (Reset current step)
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.OrdersSkip)]
    [HttpPost("orders/{id}/steps/reset")]
    public IActionResult ResetCurrentStep(string id)
    {
        var o = _fmsAppService.Engine.Orders.Values.FirstOrDefault(x => x.Id == id || x.OrderSerial == id);
        if (o == null) return NotFound(new { error = $"Order {id} not found" });

        _fmsAppService.Engine.ResetStep(o.Id);
        return Ok(new
        {
            success = true,
            msg = "已重置当前工步状态",
            currentStep = o.CurrentStepIndex,
            detailedSteps = o.DetailedSteps
        });
    }

    /// <summary>
    /// 终止当前工步 (Abort current step)
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.OrdersSkip)]
    [HttpPost("orders/{id}/steps/abort")]
    public IActionResult AbortCurrentStep(string id, [FromBody] AbortStepDto? dto)
    {
        var o = _fmsAppService.Engine.Orders.Values.FirstOrDefault(x => x.Id == id || x.OrderSerial == id);
        if (o == null) return NotFound(new { error = $"Order {id} not found" });

        _fmsAppService.Engine.AbortStep(o.Id, dto?.Reason ?? "操作员手动中止当前工步");
        return Ok(new
        {
            success = true,
            msg = "已中止当前工步",
            currentStep = o.CurrentStepIndex,
            detailedSteps = o.DetailedSteps
        });
    }

    public class AbortStepDto
    {
        public string? Reason { get; set; }
    }

    /// <summary>车队台账 + 实时遥测（数据来自引擎，含启用、地图、停靠点、电量）。</summary>
    [RequiresPermission(FmsPermissions.Fleet)]
    [HttpGet("fleet/vehicles")]
    public IActionResult GetFleetVehicles()
    {
        var engine = _fmsAppService.Engine;
        return Ok(new
        {
            success = true,
            mapId = engine.Graph.Name,
            mapName = engine.Graph.Name,
            heartbeatTimeoutSec = DispatchEngine.HeartbeatTimeoutSeconds,
            parkNodes = engine.Graph.Nodes
                .Where(n => n.Type == NodeType.StandbyPoint || n.Type == NodeType.ChargePoint)
                .OrderBy(n => n.Id)
                .Select(n => new { id = n.Id, name = string.IsNullOrWhiteSpace(n.Name) ? n.Id : n.Name, type = n.Type.ToString() }),
            items = Realtime.VehicleSnapshotBuilder.Build(engine)
        });
    }

    [RequiresPermission(FmsPermissions.Fleet)]
    [HttpGet("fleet/vehicles/{id}")]
    public IActionResult GetFleetVehicle(string id)
    {
        var engine = _fmsAppService.Engine;
        var resolved = engine.ResolveVehicleId(id) ?? id;
        if (!engine.Vehicles.TryGetValue(resolved, out var vehicle))
        {
            return NotFound(new { error = $"Vehicle {id} not found" });
        }
        return Ok(new { success = true, item = Realtime.VehicleSnapshotBuilder.Describe(engine, vehicle) });
    }

    [RequiresPermission(FmsPermissions.FleetTeleop)]
    [HttpPut("fleet/vehicles/{id}")]
    public async Task<IActionResult> UpdateFleetVehicle(string id, [FromBody] UpdateVehicleLedgerDto dto)
    {
        var engine = _fmsAppService.Engine;
        var resolved = engine.ResolveVehicleId(id) ?? id;
        if (!engine.Vehicles.ContainsKey(resolved))
        {
            return NotFound(new { error = $"Vehicle {id} not found" });
        }

        if (dto.HomeNodeId != null && dto.HomeNodeId.Length > 0)
        {
            var node = engine.Graph.GetNode(dto.HomeNodeId);
            if (node == null || (node.Type != NodeType.StandbyPoint && node.Type != NodeType.ChargePoint))
            {
                return BadRequest(new { error = "停靠点必须是当前地图的待命点或充电点" });
            }
        }

        bool ok = engine.UpdateVehicleLedger(
            resolved,
            dto.Name,
            dto.VehicleType,
            dto.DriveType,
            dto.Length,
            dto.Width,
            dto.MaxSpeed,
            dto.HomeNodeId);
        if (!ok) return BadRequest(new { error = "无法更新车辆台账" });

        await _fmsAppService.PersistVehiclesAsync();
        engine.Vehicles.TryGetValue(resolved, out var vehicle);
        return Ok(new { success = true, item = Realtime.VehicleSnapshotBuilder.Describe(engine, vehicle!) });
    }

    [RequiresPermission(FmsPermissions.FleetTeleop)]
    [HttpPost("fleet/vehicles/{id}/enable")]
    public Task<IActionResult> EnableFleetVehicle(string id) => SetFleetVehicleEnabled(id, true);

    [RequiresPermission(FmsPermissions.FleetTeleop)]
    [HttpPost("fleet/vehicles/{id}/disable")]
    public Task<IActionResult> DisableFleetVehicle(string id) => SetFleetVehicleEnabled(id, false);

    private async Task<IActionResult> SetFleetVehicleEnabled(string id, bool enabled)
    {
        var engine = _fmsAppService.Engine;
        var resolved = engine.ResolveVehicleId(id) ?? id;
        if (!engine.SetVehicleEnabled(resolved, enabled))
        {
            return NotFound(new { error = $"Vehicle {id} not found" });
        }

        await _fmsAppService.PersistVehiclesAsync();
        engine.Vehicles.TryGetValue(resolved, out var vehicle);
        return Ok(new
        {
            success = true,
            item = Realtime.VehicleSnapshotBuilder.Describe(engine, vehicle!)
        });
    }

    /// <summary>
    /// 自动电量规划：检测低于阈值的车辆，自动寻找空闲桩生成回充任务
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.FleetAutoCharge)]
    [HttpPost("fleet/auto-charge")]
    public IActionResult TriggerAutoChargePlan([FromQuery] double threshold = 25.0)
    {
        var engine = _fmsAppService.Engine;
        var chargeNodes = engine.Graph.Nodes.Where(n => n.Type == NodeType.ChargePoint).ToList();
        if (chargeNodes.Count == 0)
        {
            return BadRequest(new { error = "拓扑地图中未定义任何充电桩工位 (NodeType.ChargePoint)" });
        }

        var lowBatteryVehicles = engine.Vehicles.Values
            .Where(v => DispatchEngine.IsDispatchEligible(v) && v.Bms.SocPercent <= threshold)
            .ToList();

        var createdOrders = new List<object>();

        foreach (var v in lowBatteryVehicles)
        {
            var targetChargeNode = chargeNodes.OrderBy(n => n.Position.DistanceTo(v.Pose.Position)).First();
            string serial = $"AUTO-CHG-{DateTime.UtcNow:HHmmss}-{v.Id}";

            var order = new TransportOrderAggregate(serial, priority: 95, bindVehicleId: v.Id)
            {
                TaskType = "Charge",
                BulkTaskType = "system_auto"
            };

            order.Steps.Add(new OrderStep
            {
                StepIndex = 0,
                Name = $"低电保护：航向规划前往充电桩 {targetChargeNode.Id} (当前SOC: {v.Bms.SocPercent:F1}%)",
                Action = ActionType.Move,
                TargetNodeId = targetChargeNode.Id
            });
            order.Steps.Add(new OrderStep
            {
                StepIndex = 1,
                Name = $"接触充电：触点合闸，恒流快充至85%",
                Action = ActionType.Charge,
                TargetNodeId = targetChargeNode.Id
            });

            engine.SubmitOrder(order);
            createdOrders.Add(new { vehicleId = v.Id, soc = v.Bms.SocPercent, orderSerial = serial, chargeNode = targetChargeNode.Id });
        }

        return Ok(new
        {
            success = true,
            lowBatteryCount = lowBatteryVehicles.Count,
            dispatchedTasks = createdOrders
        });
    }

    /// <summary>
    /// 自动停靠待命：空闲车辆自动前往待命位避让主干道
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.FleetAutoDock)]
    [HttpPost("fleet/auto-dock")]
    public IActionResult TriggerAutoDockPlan()
    {
        var engine = _fmsAppService.Engine;
        var parkNodes = engine.Graph.Nodes
            .Where(n => n.Type == NodeType.PullOverPoint || n.Type == NodeType.StandbyPoint
                        || n.Id.StartsWith("PARK") || n.Id.StartsWith("SPUR"))
            .ToList();

        if (parkNodes.Count == 0)
        {
            return BadRequest(new { error = "拓扑地图中未找到可用待命点（StandbyPoint / PullOverPoint）" });
        }

        var idleVehicles = engine.Vehicles.Values
            .Where(DispatchEngine.IsDispatchEligible)
            .ToList();

        var createdOrders = new List<object>();

        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in idleVehicles)
        {
            var targetPark = parkNodes
                .Where(n => !occupied.Contains(n.Id) || parkNodes.Count <= occupied.Count)
                .OrderBy(n => n.Position.DistanceTo(v.Pose.Position))
                .First();
            occupied.Add(targetPark.Id);

            if (v.CurrentNodeId == targetPark.Id) continue;

            string serial = $"AUTO-DOCK-{DateTime.UtcNow:HHmmss}-{v.Id}";
            var order = new TransportOrderAggregate(serial, priority: 20, bindVehicleId: v.Id)
            {
                TaskType = "Nav",
                BulkTaskType = "auto_parking"
            };

            order.Steps.Add(new OrderStep
            {
                StepIndex = 0,
                Name = $"自动停靠：驶向待命位 {targetPark.Id} 释放主干道走廊",
                Action = ActionType.Move,
                TargetNodeId = targetPark.Id
            });
            order.Steps.Add(new OrderStep
            {
                StepIndex = 1,
                Name = $"停靠就绪：锁止制动并保持低功耗待命",
                Action = ActionType.Wait,
                TargetNodeId = targetPark.Id,
                DwellSec = 2.0
            });

            engine.SubmitOrder(order);
            createdOrders.Add(new { vehicleId = v.Id, orderSerial = serial, parkNode = targetPark.Id });
        }

        return Ok(new
        {
            success = true,
            dockedCount = createdOrders.Count,
            dispatchedTasks = createdOrders
        });
    }

    /// <summary>
    /// 手动遥控与车队指令
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.FleetTeleop)]
    [HttpPost("fleet/teleop")]
    public async Task<IActionResult> VehicleTeleop([FromBody] FleetTeleopDto dto)
    {
        if (!_fmsAppService.Engine.Vehicles.TryGetValue(dto.VehicleId, out var v))
        {
            return NotFound(new { error = $"Vehicle {dto.VehicleId} not found" });
        }

        string cmd = (dto.Command ?? "").Trim().ToLowerInvariant();
        if (cmd is "estop" or "clear_estop")
        {
            var permissions = HttpContext.RequestServices.GetRequiredService<Fms.Application.Identity.IPermissionService>();
            if (!await permissions.HasAsync(User, FmsPermissions.FleetEstop))
            {
                return StatusCode(403, new { code = "4003", msg = "当前角色没有权限 Fms.Fleet.Estop" });
            }
        }

        if (cmd == "estop")
        {
            _fmsAppService.Engine.SetVehicleEstop(v.Id, true);
        }
        else if (cmd == "clear_estop")
        {
            _fmsAppService.Engine.SetVehicleEstop(v.Id, false);
        }
        else if (cmd == "set_battery")
        {
            _fmsAppService.Engine.SetVehicleBattery(v.Id, dto.Value ?? 100.0);
        }
        else if (cmd == "move_to" && !string.IsNullOrEmpty(dto.TargetNodeId))
        {
            _fmsAppService.SubmitOrder($"TELEOP-{DateTime.UtcNow:HHmmss}-{v.Id}", dto.TargetNodeId, priority: 70, bindVehicleId: v.Id);
        }

        return Ok(new { success = true, vehicleId = v.Id, status = v.Status.ToString(), soc = v.Bms.SocPercent, isEstop = v.IsEstop });
    }

    /// <summary>
    /// 获取当前产线与机台虚拟 IO 信号状态寄存器 (如 DOCK_READY, LINE_CLEAR, MATERIAL_ARRIVED)
    /// </summary>
    [RequiresPermission(FmsPermissions.Simulation)]
    [HttpGet("io-signals")]
    public IActionResult GetIoSignals()
    {
        var signals = _fmsAppService.Engine.IoSignals.ToDictionary(k => k.Key, v => v.Value);
        return Ok(new { success = true, signals });
    }

    /// <summary>
    /// 设定指定虚拟 IO 信号值 (供上位机 / 自动化产线 PLC / 前端控制台调用)
    /// </summary>
    [RequiresPermission(FmsPermissions.SignalsWrite)]
    [HttpPost("io-signals")]
    public IActionResult SetIoSignal([FromBody] SetIoSignalDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.SignalName))
        {
            return BadRequest(new { error = "SignalName cannot be empty" });
        }
        string val = dto.Value ?? "0";
        _fmsAppService.Engine.SetIoSignal(dto.SignalName, val);
        return Ok(new { success = true, signalName = dto.SignalName, value = val });
    }

    /// <summary>
    /// 翻转指定虚拟 IO 信号值 (0 <=> 1)
    /// </summary>
    [RequiresPermission(FmsPermissions.SignalsWrite)]
    [HttpPost("io-signals/toggle")]
    public IActionResult ToggleIoSignal([FromBody] ToggleIoSignalDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.SignalName))
        {
            return BadRequest(new { error = "SignalName cannot be empty" });
        }
        string current = _fmsAppService.Engine.GetIoSignal(dto.SignalName);
        string nextVal = current == "1" ? "0" : "1";
        _fmsAppService.Engine.SetIoSignal(dto.SignalName, nextVal);
        return Ok(new { success = true, signalName = dto.SignalName, value = nextVal });
    }

    #region 高并发压力基准测试 API (Fleet Benchmark)

    [RequiresPermission(FmsPermissions.Simulation)]
    [HttpGet("benchmark/stats")]
    public IActionResult GetBenchmarkStats()
    {
        var stats = _fmsAppService.Engine.BenchmarkStats;
        return Ok(new { success = true, data = stats });
    }

    [RequiresPermission(FmsPermissions.SimulationInjectFault)]
    [HttpPost("benchmark/stress-test")]
    public IActionResult RunFleetStressTest([FromBody] RunBenchmarkDto dto)
    {
        int vehicles = Math.Clamp(dto.VehicleCount > 0 ? dto.VehicleCount : 30, 2, 100);
        int ticks = Math.Clamp(dto.SimulatedTicks > 0 ? dto.SimulatedTicks : 100, 10, 500);

        var report = _fmsAppService.Engine.RunFleetStressBenchmark(vehicles, ticks);
        return Ok(new { success = true, data = report });
    }

    #endregion

    #region 动态禁行划设与平滑重路由 API (Dynamic Exclusion & Rerouting)

    [RequiresPermission(FmsPermissions.MapEdit)]
    [HttpPost("closure/path")]
    public IActionResult SetPathClosure([FromBody] SetPathClosureDto dto)
    {
        bool success = _fmsAppService.Engine.SetPathClosed(dto.PathId, dto.IsClosed, dto.Reason);
        if (!success) return NotFound(new { error = $"Path '{dto.PathId}' not found" });
        return Ok(new { success = true, pathId = dto.PathId, isClosed = dto.IsClosed, reason = dto.Reason });
    }

    [RequiresPermission(FmsPermissions.MapEdit)]
    [HttpPost("closure/node")]
    public IActionResult SetNodeClosure([FromBody] SetNodeClosureDto dto)
    {
        bool success = _fmsAppService.Engine.SetNodeClosed(dto.NodeId, dto.IsClosed, dto.Reason);
        if (!success) return NotFound(new { error = $"Node '{dto.NodeId}' not found" });
        return Ok(new { success = true, nodeId = dto.NodeId, isClosed = dto.IsClosed, reason = dto.Reason });
    }

    #endregion

    #region 异常恢复矩阵 API (Fault Matrix Recovery)

    [RequiresPermission(FmsPermissions.Fleet)]
    [HttpPost("vehicles/{id}/heartbeat")]
    public IActionResult VehicleHeartbeat(string id)
    {
        _fmsAppService.Engine.RecordVehicleHeartbeat(id);
        return Ok(new { success = true, vehicleId = id, timestampUtc = DateTime.UtcNow });
    }

    [RequiresPermission(FmsPermissions.OrdersSkip)]
    [HttpPost("orders/{id}/retry")]
    public IActionResult RetryOrderStep(string id)
    {
        bool success = _fmsAppService.Engine.RetryStep(id);
        if (!success) return BadRequest(new { error = $"无法重试工单 '{id}' 当前步骤" });
        return Ok(new { success = true, message = $"工单 '{id}' 当前步骤已触发重试" });
    }

    [RequiresPermission(FmsPermissions.OrdersSkip)]
    [HttpPost("orders/{id}/rollback")]
    public IActionResult RollbackOrder(string id, [FromBody] RollbackOrderDto? dto)
    {
        bool success = _fmsAppService.Engine.SafeRollbackOrder(id, dto?.FallbackStationId);
        if (!success) return BadRequest(new { error = $"无法安全回退工单 '{id}'" });
        return Ok(new { success = true, message = $"工单 '{id}' 已成功执行安全撤退回滚" });
    }

    #endregion

    public record RunBenchmarkDto(int VehicleCount = 30, int SimulatedTicks = 100);
    public record SetPathClosureDto(string PathId, bool IsClosed, string? Reason);
    public record SetNodeClosureDto(string NodeId, bool IsClosed, string? Reason);
    public record RollbackOrderDto(string? FallbackStationId);

    public class SetIoSignalDto
    {
        public string SignalName { get; set; } = string.Empty;
        public string Value { get; set; } = "1";
    }

    public class ToggleIoSignalDto
    {
        public string SignalName { get; set; } = string.Empty;
    }

    public class CreateOrderDto
    {
        public string? OrderType { get; set; } = "Transport"; // Nav, Transport, Charge
        public string? TaskSerial { get; set; }
        public string? VehicleId { get; set; }
        public string? SourceNodeId { get; set; }
        public string TargetNodeId { get; set; } = string.Empty;
        public string? CargoId { get; set; }
        public int Priority { get; set; } = 50;
    }

    public class FleetTeleopDto
    {
        public string VehicleId { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty; // estop, clear_estop, set_battery, move_to
        public double? Value { get; set; }
        public string? TargetNodeId { get; set; }
    }

    public class UpdateVehicleLedgerDto
    {
        public string? Name { get; set; }
        public string? VehicleType { get; set; }
        public string? DriveType { get; set; }
        public double? Length { get; set; }
        public double? Width { get; set; }
        public double? MaxSpeed { get; set; }
        public string? HomeNodeId { get; set; }
    }

    public class SetScaleDto { public double Scale { get; set; } = 1.0; }
    public class SpawnVehicleDto
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? InitialNodeId { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double ThetaDeg { get; set; }
        public double MaxSpeed { get; set; } = 1.5;
    }
    public class InjectFaultDto
    {
        public string VehicleId { get; set; } = string.Empty;
        public string FaultType { get; set; } = "estop";
    }
}
