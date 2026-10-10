using Fms.Domain.Identity;
using Fms.HttpApi.Host.Authorization;
using Fms.Application;
using Fms.Application.Identity;
using Fms.Application.Logging;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Topology;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fms.HttpApi.Host.Controllers;

[Authorize]
[ApiController]
[Route("api/fms/map")]
public class MapWorkbenchController : ControllerBase
{
    private readonly FmsAppService _fmsAppService;
    private readonly IIdentityService _identityService;
    private readonly IAuditLogService _auditLogService;

    public MapWorkbenchController(
        FmsAppService fmsAppService,
        IIdentityService identityService,
        IAuditLogService auditLogService)
    {
        _fmsAppService = fmsAppService;
        _identityService = identityService;
        _auditLogService = auditLogService;
    }

    /// <summary>
    /// 获取当前生效的全局拓扑结构（节点、贝塞尔/直线路径、互斥锁区）
    /// </summary>
    [RequiresPermission(FmsPermissions.Map)]
    [HttpGet]
    public IActionResult GetFullMap()
    {
        var graph = _fmsAppService.Engine.RoadNetworkManager.DraftGraph.Nodes.Count > 0
            ? _fmsAppService.Engine.RoadNetworkManager.DraftGraph
            : _fmsAppService.Engine.Graph;
        return Ok(new
        {
            code = "0000",
            msg = "获取成功",
            data = new
            {
                name = graph.Name,
                bounds = new { minX = graph.MinX, maxX = graph.MaxX, minY = graph.MinY, maxY = graph.MaxY },
                nodes = graph.Nodes.Select(n => new
                {
                    id = n.Id,
                    name = n.Name,
                    x = n.Position.X,
                    y = n.Position.Y,
                    type = n.Type.ToString(),
                    headingDeg = n.HeadingDeg,
                    allowRevolve = n.AllowRevolve,
                    spinDirection = n.SpinDirection,
                    className = n.ClassName
                }),
                paths = graph.Paths.Select(p => new
                {
                    id = p.Id,
                    from = p.FromNodeId,
                    to = p.ToNodeId,
                    type = p.Type.ToString(),
                    curveType = p.CurveType,
                    direction = p.Direction,
                    roadWidth = p.RoadWidth,
                    length = Math.Round(p.Length, 3),
                    maxSpeed = p.MaxSpeed,
                    bidirectional = p.Bidirectional,
                    samplePoints = p.Sample(0.5).Select(pt => new { x = pt.X, y = pt.Y }),
                    bezierControls = p is CubicBezierPath b ? new
                    {
                        p0 = new { x = b.P0.X, y = b.P0.Y },
                        p1 = new { x = b.P1.X, y = b.P1.Y },
                        p2 = new { x = b.P2.X, y = b.P2.Y },
                        p3 = new { x = b.P3.X, y = b.P3.Y }
                    } : null
                }),
                zones = graph.Zones.Select(z => new
                {
                    id = z.Id,
                    name = z.Name,
                    maxCapacity = z.MaxCapacity,
                    timeoutSec = z.TimeoutSec,
                    polygon = z.Polygon.Select(pt => new { x = pt.X, y = pt.Y }),
                    occupied = z.IsOccupied,
                    claimantVehicles = z.ActiveClaimantVehicleIds.ToArray(),
                    associatedPathIds = z.AssociatedPathIds.ToArray()
                })
            }
        });
    }

    /// <summary>
    /// 新增或更新节点 (触发工业安全互锁校验)
    /// </summary>
    [RequiresPermission(FmsPermissions.MapEdit)]
    [HttpPost("nodes")]
    public IActionResult SaveNode([FromBody] SaveNodeDto dto)
    {
        var (isSafe, reason) = _identityService.CheckSafetyInterlock("MAP_MODIFY", _fmsAppService.Engine);
        if (!isSafe)
        {
            return BadRequest(new { code = "4030", msg = reason });
        }

        var nodeType = Enum.TryParse<NodeType>(dto.Type, true, out var nt) ? nt : NodeType.JobPoint;
        var node = new MapNode(dto.Id, new Point2D(dto.X, dto.Y), nodeType, dto.Name ?? dto.Id)
        {
            HeadingDeg = dto.HeadingDeg,
            AllowRevolve = dto.AllowRevolve,
            SpinDirection = dto.SpinDirection ?? "both",
            ClassName = dto.ClassName ?? "LandMark"
        };

        _fmsAppService.Engine.RoadNetworkManager.DraftGraph.AddNode(node);
        _auditLogService.Log(User.Identity?.Name ?? "Admin", "SAVE_NODE", $"Node-{dto.Id}", $"保存工位节点 {dto.Id} ({dto.X}, {dto.Y})");

        return Ok(new { code = "0000", msg = "草稿节点保存成功" });
    }

    /// <summary>
    /// 删除节点 (触发工业安全互锁校验)
    /// </summary>
    [RequiresPermission(FmsPermissions.MapEdit)]
    [HttpDelete("nodes/{id}")]
    public IActionResult DeleteNode(string id)
    {
        var (isSafe, reason) = _identityService.CheckSafetyInterlock("MAP_DELETE", _fmsAppService.Engine);
        if (!isSafe)
        {
            return BadRequest(new { code = "4030", msg = reason });
        }

        _fmsAppService.Engine.RoadNetworkManager.DraftGraph.RemoveNode(id);
        _auditLogService.Log(User.Identity?.Name ?? "Admin", "DELETE_NODE", $"Node-{id}", $"删除工位节点 {id}");

        return Ok(new { code = "0000", msg = "草稿节点删除成功" });
    }

    /// <summary>
    /// 新增或更新路径（支持 StraightLine 与 CubicBezier 贝塞尔曲线）
    /// </summary>
    [RequiresPermission(FmsPermissions.MapEdit)]
    [HttpPost("paths")]
    public IActionResult SavePath([FromBody] SavePathDto dto)
    {
        var (isSafe, reason) = _identityService.CheckSafetyInterlock("MAP_MODIFY", _fmsAppService.Engine);
        if (!isSafe)
        {
            return BadRequest(new { code = "4030", msg = reason });
        }

        var draftGraph = _fmsAppService.Engine.RoadNetworkManager.DraftGraph;
        var fromNode = draftGraph.GetNode(dto.FromNodeId) ?? _fmsAppService.Engine.Graph.GetNode(dto.FromNodeId);
        var toNode = draftGraph.GetNode(dto.ToNodeId) ?? _fmsAppService.Engine.Graph.GetNode(dto.ToNodeId);
        if (fromNode == null || toNode == null)
        {
            return BadRequest(new { code = "4004", msg = "起点或终点节点不存在" });
        }

        MapPath path;
        if (dto.Type?.Equals("CubicBezier", StringComparison.OrdinalIgnoreCase) == true && dto.P1 != null && dto.P2 != null)
        {
            path = new CubicBezierPath(
                dto.Id,
                dto.FromNodeId,
                dto.ToNodeId,
                fromNode.Position,
                new Point2D(dto.P1.X, dto.P1.Y),
                new Point2D(dto.P2.X, dto.P2.Y),
                toNode.Position,
                dto.Bidirectional,
                dto.MaxSpeed > 0 ? dto.MaxSpeed : 1.2
            );
        }
        else
        {
            path = new StraightLinePath(
                dto.Id,
                dto.FromNodeId,
                dto.ToNodeId,
                fromNode.Position,
                toNode.Position,
                dto.Bidirectional,
                dto.MaxSpeed > 0 ? dto.MaxSpeed : 1.5
            );
        }

        path.Direction = dto.Direction ?? "forward";
        path.RoadWidth = dto.RoadWidth > 0 ? dto.RoadWidth : 1.2;

        draftGraph.AddPath(path);
        _auditLogService.Log("Admin", "SAVE_PATH", $"Path-{dto.Id}", $"保存路径 {dto.Id} ({dto.FromNodeId} -> {dto.ToNodeId}, 类型={path.Type}, 导引={path.Direction})");

        return Ok(new { code = "0000", msg = "草稿路径保存成功" });
    }

    /// <summary>
    /// 删除路径 (触发工业安全互锁校验)
    /// </summary>
    [RequiresPermission(FmsPermissions.MapEdit)]
    [HttpDelete("paths/{id}")]
    public IActionResult DeletePath(string id)
    {
        var (isSafe, reason) = _identityService.CheckSafetyInterlock("MAP_DELETE", _fmsAppService.Engine);
        if (!isSafe)
        {
            return BadRequest(new { code = "4030", msg = reason });
        }

        _fmsAppService.Engine.RoadNetworkManager.DraftGraph.RemovePath(id);
        _auditLogService.Log("Admin", "DELETE_PATH", $"Path-{id}", $"删除路径 {id}");

        return Ok(new { code = "0000", msg = "草稿路径删除成功" });
    }

    /// <summary>
    /// 新增或更新互斥锁区 (Mutex Zone)
    /// </summary>
    [RequiresPermission(FmsPermissions.MapEdit)]
    [HttpPost("zones")]
    public IActionResult SaveZone([FromBody] SaveZoneDto dto)
    {
        var zone = new MutexZone
        {
            Id = dto.Id,
            Name = dto.Name ?? dto.Id,
            MaxCapacity = dto.MaxCapacity > 0 ? dto.MaxCapacity : 1,
            TimeoutSec = dto.TimeoutSec > 0 ? dto.TimeoutSec : 60.0,
            Polygon = dto.Polygon.Select(p => new Point2D(p.X, p.Y)).ToList()
        };

        _fmsAppService.Engine.RoadNetworkManager.DraftGraph.AddZone(zone);
        _auditLogService.Log("Admin", "SAVE_MUTEX_ZONE", $"Zone-{dto.Id}", $"配置互斥区 {dto.Id}, 容量={zone.MaxCapacity}");

        return Ok(new { code = "0000", msg = "草稿互斥锁区保存成功" });
    }

    public record ForceReleaseZoneDto(string? Operator, string? Reason);

    /// <summary>
    /// 管理员强制解除互斥锁（死锁现场排故特权，要求实名与原因，留下完整审计与决策追踪）
    /// </summary>
    [RequiresPermission(FmsPermissions.TrafficForceRelease)]
    [HttpPost("zones/{id}/force-release")]
    public IActionResult ForceReleaseZone(string id, [FromBody] ForceReleaseZoneDto? dto)
    {
        string op = User.Identity?.Name ?? dto?.Operator ?? "Supervisor";
        string reason = dto?.Reason?.Trim() ?? "现场调度员紧急解除互斥锁";

        var (isSafe, interlockErr) = _identityService.CheckSafetyInterlock("FORCE_RELEASE_MUTEX", _fmsAppService.Engine, op, reason);
        if (!isSafe)
        {
            return BadRequest(new { code = "4030", msg = interlockErr });
        }

        var zone = _fmsAppService.Engine.Graph.GetZone(id);
        if (zone != null)
        {
            var claimants = zone.ActiveClaimantVehicleIds.ToArray();
            zone.ForceRelease();

            // 轨 1: 审计日志落库
            _auditLogService.Log(op, "FORCE_RELEASE_MUTEX", $"Zone-{id}",
                $"现场强制解除互斥锁区: 原因=[{reason}], 当时占用/申请车辆=[{string.Join(", ", claimants)}]");

            // 轨 2: 调度决策日志
            _fmsAppService.Engine.DecisionLogger?.Record(
                DecisionCategory.DeadlockResolution,
                $"Zone-{id}",
                "ForceReleaseMutex",
                "Executed",
                $"操作员 {op} 强制解锁: {reason}，清退车辆 [{string.Join(", ", claimants)}]"
            );

            return Ok(new { code = "0000", msg = $"互斥锁区 {id} 已强制释放", claimants });
        }
        return NotFound(new { code = "4004", msg = "互斥锁区不存在" });
    }

    /// <summary>
    /// 发布全新路网不可变版本（安全互锁：有在途车运行时严禁发布）
    /// </summary>
    [RequiresPermission(FmsPermissions.MapPublish)]
    [HttpPost("publish")]
    public async Task<IActionResult> PublishRoadNetwork()
    {
        var publisher = User.Identity?.Name ?? "Admin";
        var result = _fmsAppService.Engine.RoadNetworkManager.Publish(
            publisher,
            _fmsAppService.Engine.StorageManager,
            _fmsAppService.Engine.Vehicles.Values,
            _fmsAppService.Engine.Graph
        );

        if (!result.Success)
        {
            return BadRequest(new { code = "4031", msg = result.Message });
        }

        await _fmsAppService.SaveCurrentMapAsync();
        _auditLogService.Log(publisher, "PUBLISH_ROAD_NETWORK", result.Version ?? "v1.0", result.Message);

        return Ok(new
        {
            code = "0000",
            msg = result.Message,
            data = new
            {
                version = result.Version,
                publishedAt = DateTime.UtcNow
            }
        });
    }

    /// <summary>
    /// 获取当前路网的所有线路视图 (RouteLines)
    /// </summary>
    [RequiresPermission(FmsPermissions.Map)]
    [HttpGet("lines")]
    public IActionResult GetRouteLines()
    {
        var lines = _fmsAppService.Engine.RoadNetworkManager.GetAllRouteLines();
        return Ok(new
        {
            code = "0000",
            msg = "获取成功",
            data = lines
        });
    }

    /// <summary>
    /// 保存或更新线路定义
    /// </summary>
    [RequiresPermission(FmsPermissions.MapEdit)]
    [HttpPost("lines")]
    public async Task<IActionResult> SaveRouteLine([FromBody] SaveRouteLineDto dto)
    {
        var line = new RouteLine
        {
            Id = dto.Id,
            Name = dto.Name ?? dto.Id,
            MemberPathIds = dto.MemberPathIds ?? new(),
            AllowedVehicleTypes = dto.AllowedVehicleTypes ?? new() { "ALL" },
            DefaultDirection = dto.DefaultDirection ?? "forward",
            AllowBackward = dto.AllowBackward,
            SpeedLimit = dto.SpeedLimit > 0 ? dto.SpeedLimit : 1.5,
            IsClosed = dto.IsClosed,
            ClosureReason = dto.ClosureReason
        };

        _fmsAppService.Engine.RoadNetworkManager.RegisterRouteLine(line);
        await _fmsAppService.SaveCurrentMapAsync();

        return Ok(new { code = "0000", msg = "线路保存成功", data = line });
    }

    public record SaveNodeDto(string Id, string? Name, double X, double Y, string? Type, double HeadingDeg, bool AllowRevolve = true, string? SpinDirection = "both", string? ClassName = "LandMark");
    public record SavePathDto(string Id, string FromNodeId, string ToNodeId, string? Type, bool Bidirectional, double MaxSpeed, PointDto? P1, PointDto? P2, string? Direction = "forward", double RoadWidth = 1.2);
    public record SaveZoneDto(string Id, string? Name, int MaxCapacity, double TimeoutSec, List<PointDto> Polygon);
    public record SaveRouteLineDto(string Id, string? Name, List<string>? MemberPathIds, List<string>? AllowedVehicleTypes, string? DefaultDirection, bool AllowBackward, double SpeedLimit, bool IsClosed, string? ClosureReason);
    public record PointDto(double X, double Y);
}
