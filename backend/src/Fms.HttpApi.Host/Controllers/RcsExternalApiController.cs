using Fms.Domain.Identity;
using Fms.HttpApi.Host.Authorization;
using Fms.Application;
using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fms.HttpApi.Host.Controllers;

[Authorize]
[ApiController]
public class RcsExternalApiController : ControllerBase
{
    private readonly FmsAppService _fmsAppService;
    private readonly ILogger<RcsExternalApiController> _logger;

    public RcsExternalApiController(FmsAppService fmsAppService, ILogger<RcsExternalApiController> logger)
    {
        _fmsAppService = fmsAppService;
        _logger = logger;
    }

    /// <summary>
    /// 1. 心跳接口 (GET /api/xinsong/heart_beat 或 /{type}/api/xinsong/heart_beat)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("api/xinsong/heart_beat")]
    [HttpGet("{type}/api/xinsong/heart_beat")]
    [HttpGet("api/v1/xinsong/heart_beat")]
    [HttpGet("{type}/api/v1/xinsong/heart_beat")]
    public IActionResult HeartBeat([FromQuery] string? timestamp = null, [FromRoute] string? type = null)
    {
        string nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.ffffff");
        return Ok(new
        {
            ErrMsg = nowStr,
            Result = true
        });
    }

    /// <summary>
    /// 2. 下发任务 (POST /api/v1/xinsong/task_add 或 /{type}/api/v1/xinsong/task_add)
    /// </summary>
    [RequiresPermission(FmsPermissions.OrdersCreate)]
    [HttpPost("api/v1/xinsong/task_add")]
    [HttpPost("{type}/api/v1/xinsong/task_add")]
    public IActionResult AddTask([FromBody] TaskAddRequestDto request, [FromRoute] string? type = null)
    {
        if (request == null)
        {
            return BadRequest(new { ErrMsg = "请求体为空", Result = false });
        }

        // 兼容 MHS / TXC 任务列表格式 (job_list / transportList)
        if ((request.sub_task == null || request.sub_task.Count == 0))
        {
            if (request.job_list != null && request.job_list.Count > 0)
            {
                request.sub_task = request.job_list;
            }
            else if (request.transportList != null && request.transportList.Count > 0)
            {
                request.sub_task = request.transportList;
            }
        }

        // 兼容扁平结构
        if ((request.sub_task == null || request.sub_task.Count == 0) &&
            (!string.IsNullOrEmpty(request.task_id) || !string.IsNullOrEmpty(request.job_id) || request.target != null || request.destPosition != null))
        {
            request.sub_task = new List<SubTaskDto>
            {
                new SubTaskDto
                {
                    task_serial = request.task_id ?? request.job_id ?? request.lot_id ?? Guid.NewGuid().ToString("N"),
                    task_type = request.task_type ?? type ?? request.bulk_task_type,
                    source_point = request.source_point ?? request.srcPosition,
                    target = request.target ?? request.destPosition,
                    goal_action = request.goal_action,
                    priority = request.priority,
                    shelf_code = request.shelf_code,
                    cargo_id = request.cargo_id ?? request.carrier_id,
                    robot_id = request.robot_id,
                    AGV_serial = int.TryParse(request.robot_id?.Replace("AMR-", "")?.Replace("AGV-", ""), out var s) ? s : 0
                }
            };
        }

        if (request.sub_task == null || request.sub_task.Count == 0)
        {
            return BadRequest(new { ErrMsg = "任务列表为空", Result = false });
        }

        // 字段自适应补全与接续号排序
        foreach (var sub in request.sub_task)
        {
            if (string.IsNullOrWhiteSpace(sub.task_serial))
            {
                sub.task_serial = sub.job_id ?? sub.lot_id ?? Guid.NewGuid().ToString("N");
            }
            if (sub.target == null && sub.destPosition != null) sub.target = sub.destPosition;
            if (sub.source_point == null && sub.srcPosition != null) sub.source_point = sub.srcPosition;
            if (string.IsNullOrEmpty(sub.cargo_id)) sub.cargo_id = sub.carrier_id ?? sub.shelf_code;
            if (string.IsNullOrEmpty(sub.task_type)) sub.task_type = type ?? request.bulk_task_type;
        }

        if (request.sub_task.Any(s => s.succession > 0))
        {
            request.sub_task = request.sub_task.OrderBy(s => s.succession).ToList();
        }

        try
        {
            var engine = _fmsAppService.Engine;
            var prepared = new List<TransportOrderAggregate>();

            // 先整体校验再入队：任何一条子任务不合法，整包拒绝，避免部分下发
            foreach (var sub in request.sub_task)
            {
                if (string.IsNullOrWhiteSpace(sub.task_serial))
                {
                    return Ok(new { ErrMsg = "task_serial 不能为空", Result = false });
                }
                if (engine.Orders.Values.Any(o => o.OrderSerial == sub.task_serial && !o.IsFinished) || prepared.Any(o => o.OrderSerial == sub.task_serial))
                {
                    return Ok(new { ErrMsg = $"任务编号 {sub.task_serial} 已存在", Result = false });
                }

                var target = sub.target?.ToString() ?? "";
                if (!engine.HasNode(target))
                {
                    return Ok(new { ErrMsg = $"目标点位 {target} 不在当前地图上", Result = false });
                }

                var source = sub.source_point?.ToString() ?? "";
                bool hasSource = !string.IsNullOrEmpty(source) && (source != "0" || engine.HasNode(source));
                if (hasSource && !engine.HasNode(source))
                {
                    return Ok(new { ErrMsg = $"起点点位 {source} 不在当前地图上", Result = false });
                }

                // 指定车辆：robot_id 或 AGV_serial（编号如 25 / AMR-25 解析到真实车辆）
                string? rawVehicle = !string.IsNullOrWhiteSpace(sub.robot_id) ? sub.robot_id : (sub.AGV_serial > 0 ? sub.AGV_serial.ToString() : null);
                string? bound = null;
                if (!string.IsNullOrEmpty(rawVehicle))
                {
                    bound = engine.ResolveVehicleId(rawVehicle);
                    if (bound == null)
                    {
                        return Ok(new { ErrMsg = $"找不到指定车辆 {rawVehicle}", Result = false });
                    }
                }

                var order = new TransportOrderAggregate(sub.task_serial, sub.priority)
                {
                    BulkTaskType = request.bulk_task_type,
                    TaskType = sub.task_type ?? request.bulk_task_type,
                    BindVehicleId = bound,
                    RobotType = sub.robot_type ?? "3",
                    Succession = sub.succession,
                    PreReport = sub.pre_report ?? "0"
                };

                string cargo = sub.cargo_id ?? sub.shelf_code ?? "";
                string option = sub.option_code ?? "0,0";

                if (hasSource)
                {
                    // 取放货：到起点取货 -> 驶向终点 -> 终点放货
                    order.Steps.Add(new OrderStep { StepIndex = 0, Action = ActionType.Move, TargetNodeId = source, OptionCode = option, CargoId = cargo, Name = $"前往取货点 {source}" });
                    order.Steps.Add(new OrderStep { StepIndex = 1, Action = ActionType.LiftUp, TargetNodeId = source, OptionCode = option, CargoId = cargo, GoalAction = 1, DwellSec = 3.0, Name = $"在 {source} 取货" });
                    order.Steps.Add(new OrderStep { StepIndex = 2, Action = ActionType.Move, TargetNodeId = target, OptionCode = option, CargoId = cargo, Name = $"前往放货点 {target}" });
                    order.Steps.Add(new OrderStep { StepIndex = 3, Action = ActionType.PutDown, TargetNodeId = target, OptionCode = option, CargoId = cargo, GoalAction = 2, DwellSec = 3.0, Name = $"在 {target} 放货" });
                }
                else
                {
                    var action = sub.goal_action == 1 ? ActionType.LiftUp : (sub.goal_action == 2 ? ActionType.PutDown : ActionType.Move);
                    order.Steps.Add(new OrderStep
                    {
                        StepIndex = 0,
                        Action = action,
                        TargetNodeId = target,
                        OptionCode = option,
                        CargoId = cargo,
                        GoalAction = sub.goal_action,
                        DwellSec = action == ActionType.Move ? 0 : 3.0,
                        Name = action switch { ActionType.LiftUp => $"前往 {target} 取货", ActionType.PutDown => $"前往 {target} 放货", _ => $"前往 {target}" }
                    });
                }

                prepared.Add(order);
            }

            foreach (var order in prepared)
            {
                engine.SubmitOrder(order);
                _logger.LogInformation("Submitted task from RCS: {TaskSerial}, Steps: {Steps}, Vehicle: {Vehicle}", order.OrderSerial, order.Steps.Count, order.BindVehicleId ?? "auto");
            }

            return Ok(new
            {
                ErrMsg = "",
                Result = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add task from RCS");
            return Ok(new
            {
                ErrMsg = ex.Message,
                Result = false
            });
        }
    }

    /// <summary>
    /// 8. RCS 端操作删除任务 (POST /api/v1/xinsong/task_delete 或 /{type}/api/v1/xinsong/task_delete)
    /// </summary>
    [RequiresPermission(FmsPermissions.OrdersCancel)]
    [HttpPost("api/v1/xinsong/task_delete")]
    [HttpPost("{type}/api/v1/xinsong/task_delete")]
    public IActionResult DeleteTask([FromBody] TaskDeleteRequestDto request, [FromRoute] string? type = null)
    {
        string taskSerial = request.task_serial;
        if (string.IsNullOrEmpty(taskSerial))
        {
            taskSerial = request.job_id ?? request.task_id ?? request.lot_id ?? "";
        }

        if (string.IsNullOrEmpty(taskSerial))
        {
            return BadRequest(new { ErrMsg = "task_serial 不能为空", Result = false });
        }

        var order = _fmsAppService.Engine.Orders.Values.FirstOrDefault(o => o.OrderSerial == taskSerial);
        if (order != null)
        {
            _fmsAppService.Engine.CancelOrder(order.Id, "Cancelled via Northbound RCS API");
        }

        return Ok(new
        {
            ErrMsg = "",
            Result = true,
            Delete_flag = "2"
        });
    }

    /// <summary>
    /// 9. 调度网页操作删除任务 (POST /api/v1/fms/orders/delete_web)
    /// 会向上位系统申请删除，若上位系统同意则删除该任务及其组合任务
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.OrdersCancel)]
    [HttpPost("api/v1/fms/orders/delete_web")]
    public async Task<IActionResult> DeleteTaskFromWeb([FromBody] WebDeleteTaskRequestDto request)
    {
        if (string.IsNullOrEmpty(request.task_serial))
        {
            return BadRequest(new { ErrMsg = "task_serial 不能为空", Result = false });
        }

        bool success = await _fmsAppService.DeleteOrderFromWebAsync(request.task_serial, request.AGV_serial?.ToString());
        return Ok(new
        {
            ErrMsg = success ? "" : "上位机 RCS 拒绝删除该任务",
            Result = success
        });
    }

    public class WebDeleteTaskRequestDto
    {
        public string task_serial { get; set; } = string.Empty;
        public object? AGV_serial { get; set; }
    }

    public class TaskAddRequestDto
    {
        public int bulk_task_count { get; set; }
        public string bulk_task_type { get; set; } = "task";
        public List<SubTaskDto>? sub_task { get; set; }
        public List<SubTaskDto>? job_list { get; set; }
        public List<SubTaskDto>? transportList { get; set; }

        // Flat fallback fields for testing / alternative RCS models
        public string? task_id { get; set; }
        public string? job_id { get; set; }
        public string? lot_id { get; set; }
        public string? robot_id { get; set; }
        public string? task_type { get; set; }
        public object? source_point { get; set; }
        public object? srcPosition { get; set; }
        public object? target { get; set; }
        public object? destPosition { get; set; }
        public int goal_action { get; set; }
        public int priority { get; set; } = 50;
        public string? shelf_code { get; set; }
        public string? cargo_id { get; set; }
        public string? carrier_id { get; set; }
    }

    public class SubTaskDto
    {
        public int AGV_serial { get; set; }
        public string? robot_id { get; set; }
        public string? cargo_id { get; set; }
        public string? carrier_id { get; set; }
        public string? complete_time { get; set; }
        public int goal_action { get; set; }
        public string? mark { get; set; }
        public string? option_code { get; set; }
        public string? pre_report { get; set; }
        public int priority { get; set; } = 50;
        public string? robot_type { get; set; }
        public string? storage { get; set; }
        public int succession { get; set; }
        public object? source_point { get; set; }
        public object? srcPosition { get; set; }
        public object? target { get; set; }
        public object? destPosition { get; set; }
        public string task_serial { get; set; } = string.Empty;
        public string? job_id { get; set; }
        public string? lot_id { get; set; }
        public string? task_type { get; set; }
        public string? shelf_code { get; set; }
    }

    public class TaskDeleteRequestDto
    {
        public int delete_task_count { get; set; } = 1;
        public string task_serial { get; set; } = string.Empty;
        public string? job_id { get; set; }
        public string? task_id { get; set; }
        public string? lot_id { get; set; }
        public int AGV_serial { get; set; } = 0;
    }
}
