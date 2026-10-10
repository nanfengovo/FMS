using System.Text.Json;
using Fms.Application.Map;
using Fms.Application.Rcs;
using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Storage;
using Fms.Domain.Topology;
using Fms.Domain.Vehicles;
using Fms.Engine;
using Fms.EntityFrameworkCore;
using Fms.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fms.Application;

public class FmsAppService
{
    public DispatchEngine Engine { get; }
    public XMapParserService MapParser { get; }
    public XMapSerializerService MapSerializer { get; }
    public IRcsCallbackService RcsCallback { get; }
    public RcsConfigurationService RcsConfig { get; }
    public Gateway.IndustrialGatewayService? GatewayService { get; }
    private readonly IServiceScopeFactory _scopeFactory;

    public FmsAppService(
        DispatchEngine engine,
        XMapParserService mapParser,
        XMapSerializerService mapSerializer,
        IRcsCallbackService rcsCallback,
        RcsConfigurationService rcsConfig,
        IServiceScopeFactory scopeFactory,
        Gateway.IndustrialGatewayService? gatewayService = null)
    {
        Engine = engine;
        MapParser = mapParser;
        MapSerializer = mapSerializer;
        RcsCallback = rcsCallback;
        RcsConfig = rcsConfig;
        _scopeFactory = scopeFactory;
        GatewayService = gatewayService;

        if (GatewayService != null)
        {
            Engine.SetGatewaySignalReader(s => GatewayService.ReadSignal(s));
            Engine.IoSignalChanged += (k, v) => GatewayService.UpdateSignal(k, v);
        }

        // 1. 任务开始执行事件回调 (对接 PDF 3. 上报任务开始执行 task_info)
        Engine.OrderStarted += async (_, e) =>
        {
            await RcsCallback.NotifyTaskStartedAsync(e.Order.OrderSerial, e.Vehicle.Id, e.Order.TaskType);
        };

        // 2. 车辆到达目的地事件回调 (对接 PDF 4. 上报车辆到达目的地 task_arrive_target)
        Engine.OrderArrivedTarget += async (_, e) =>
        {
            await RcsCallback.NotifyTaskArrivedTargetAsync(e.Order.OrderSerial, e.Vehicle.Id, e.TargetNodeId, e.Order.TaskType);
        };

        // 3. 动作与RFID校验事件回调 (对接 PDF 5. 请求设备动作放行 & 6. 物料 RFID 校验)
        Engine.OrderActionExecuting += async (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Step.OptionCode) && e.Step.OptionCode != "0,0")
            {
                e.Step.IsActionPermitted = await RcsCallback.RequestActionPermissionAsync(
                    e.Order.OrderSerial, e.Vehicle.Id, e.Step.OptionCode, e.Order.TaskType);
            }

            if (!string.IsNullOrEmpty(e.Step.CargoId))
            {
                e.Step.IsRfidVerified = await RcsCallback.VerifyCargoRfidAsync(
                    e.Order.OrderSerial, e.Vehicle.Id, e.Step.CargoId, e.Order.TaskType);
            }
        };

        // 4. 任务完成事件回调 (对接 PDF 7. 上报任务完成 task_finish)
        Engine.OrderCompleted += async (_, order) =>
        {
            if (order.AssignedVehicleId != null && Engine.Vehicles.TryGetValue(order.AssignedVehicleId, out var vehicle))
            {
                await RcsCallback.NotifyTaskFinishedAsync(order, vehicle, finishType: 1);
            }
        };
    }

    /// <summary>
    /// 9. 调度网页操作删除任务 (对标 PDF: 调度网页操作删除任务，会向上位系统申请删除)
    /// </summary>
    public async Task<bool> DeleteOrderFromWebAsync(string taskSerial, string? vehicleId = null)
    {
        var order = Engine.Orders.Values.FirstOrDefault(o => o.OrderSerial == taskSerial);
        string vId = vehicleId ?? order?.AssignedVehicleId ?? "0";
        string taskType = order?.TaskType ?? "";

        // 向上位机发起删除申请
        bool approved = await RcsCallback.RequestTaskDeleteFromWebAsync(taskSerial, vId, taskType);
        if (approved)
        {
            // 走引擎统一的取消流程：状态转移校验、释放车辆与货位预约、落库
            if (order != null)
            {
                Engine.CancelOrder(order.Id, "调度网页删除任务，上位系统已同意");
            }
            return true;
        }

        return false;
    }

    public List<Point2D> CurrentPointCloud { get; set; } = new();
    public int TotalPointCloudCount { get; set; } = 0;
    public MapRaster? CurrentRaster { get; private set; }
    public int DownsampleFactor { get; set; } = 25;

    public async Task<XmapParseResultDto> LoadXmapAsDraftAsync(Stream stream)
    {
        var result = await MapParser.ParseXmapXmlAsync(stream);
        ApplyParsedMapToDraft(result);
        return result;
    }

    public async Task<XmapParseResultDto> LoadXmapFromFileAsDraftAsync(string filePath)
    {
        var result = await MapParser.ParseXmapFileAsync(filePath);
        ApplyParsedMapToDraft(result);
        return result;
    }

    public XmapParseResultDto LoadMapinfoJsonAsDraft(string json)
    {
        var result = MapParser.ParseMapinfoJson(json);
        ApplyParsedMapToDraft(result);
        return result;
    }

    public void ApplyParsedMapToDraft(XmapParseResultDto result)
    {
        var draftGraph = Engine.RoadNetworkManager.DraftGraph;
        draftGraph.Clear();
        draftGraph.Name = result.MapName;
        draftGraph.MinX = result.MinX;
        draftGraph.MaxX = result.MaxX;
        draftGraph.MinY = result.MinY;
        draftGraph.MaxY = result.MaxY;

        foreach (var node in result.Graph.Nodes) draftGraph.AddNode(node);
        foreach (var path in result.Graph.Paths) draftGraph.AddPath(path);
        foreach (var zone in result.Graph.Zones) draftGraph.AddZone(zone);

        CurrentPointCloud = result.PointCloud;
        TotalPointCloudCount = result.TotalPointCloudCount;
        DownsampleFactor = result.DownsampleFactor > 0 ? result.DownsampleFactor : 25;

        // 将高级区域 advanced_area 全面映射至领域对象：库区、交管区、禁行区、限速区
        if (result.AdvancedAreas != null)
        {
            foreach (var area in result.AdvancedAreas)
            {
                if (area.Category == "StorageZone")
                {
                    var sz = new StorageZone(area.Id, string.IsNullOrEmpty(area.Name) ? area.Id : area.Name, StorageZoneType.Wip, 20)
                    {
                        Polygon = area.Polygon
                    };
                    Engine.StorageManager.RegisterZone(sz);
                }
                else if (area.Category == "MutexZone")
                {
                    var existingZone = draftGraph.GetZone(area.Id);
                    if (existingZone == null)
                    {
                        var mz = new MutexZone(area.Id, string.IsNullOrEmpty(area.Name) ? area.Id : area.Name, 1, 60.0)
                        {
                            Polygon = area.Polygon
                        };
                        draftGraph.AddZone(mz);
                    }
                }
            }
        }
    }

    public async Task SaveDraftPointCloudAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        var pcRecord = await db.MapPointClouds.FirstOrDefaultAsync(p => p.Id == "default");
        if (pcRecord == null)
        {
            pcRecord = new MapPointCloudRecord { Id = "default" };
            db.MapPointClouds.Add(pcRecord);
        }

        if (CurrentPointCloud.Count > 0)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(CurrentPointCloud.Count);
            foreach (var pt in CurrentPointCloud)
            {
                bw.Write((float)pt.X);
                bw.Write((float)pt.Y);
            }
            pcRecord.Data = ms.ToArray();
            pcRecord.TotalCount = CurrentPointCloud.Count;
            pcRecord.RawTotalCount = TotalPointCloudCount > 0 ? TotalPointCloudCount : CurrentPointCloud.Count;
            pcRecord.DownsampleFactor = DownsampleFactor > 0 ? DownsampleFactor : 25;
        }
        else
        {
            pcRecord.Data = Array.Empty<byte>();
            pcRecord.TotalCount = 0;
            pcRecord.RawTotalCount = 0;
            pcRecord.DownsampleFactor = 25;
        }
        pcRecord.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<XmapParseResultDto> LoadXmapAsync(Stream stream)
    {
        var result = await MapParser.ParseXmapXmlAsync(stream);
        ApplyParsedMap(result);
        return result;
    }

    public void ReplaceRaster(byte[] bytes, string sourceName)
    {
        var graph = Engine.Graph;
        CurrentRaster = KecongMapImage.FromBytes(bytes, sourceName, graph.MinX, graph.MinY, graph.MaxX, graph.MaxY, 0);
    }

    public async Task<XmapParseResultDto> LoadXmapFromFileAsync(string filePath)
    {
        var result = await MapParser.ParseXmapFileAsync(filePath);
        ApplyParsedMap(result);
        CurrentRaster = KecongMapImage.TryLoadSibling(filePath, result.MinX, result.MinY, result.MaxX, result.MaxY, result.Resolution);
        return result;
    }

    public void ApplyParsedMap(XmapParseResultDto result)
    {
        Engine.Graph.Clear();
        Engine.Graph.Name = result.MapName;
        Engine.Graph.MinX = result.MinX;
        Engine.Graph.MaxX = result.MaxX;
        Engine.Graph.MinY = result.MinY;
        Engine.Graph.MaxY = result.MaxY;

        foreach (var node in result.Graph.Nodes) Engine.Graph.AddNode(node);
        foreach (var path in result.Graph.Paths) Engine.Graph.AddPath(path);
        foreach (var zone in result.Graph.Zones) Engine.Graph.AddZone(zone);

        CurrentPointCloud = result.PointCloud;
        TotalPointCloudCount = result.TotalPointCloudCount;
        DownsampleFactor = result.DownsampleFactor > 0 ? result.DownsampleFactor : 25;

        // 将高级区域 advanced_area 全面映射至领域对象：库区、交管区、禁行区、限速区
        if (result.AdvancedAreas != null)
        {
            foreach (var area in result.AdvancedAreas)
            {
                if (area.Category == "StorageZone")
                {
                    var sz = new StorageZone(area.Id, string.IsNullOrEmpty(area.Name) ? area.Id : area.Name, StorageZoneType.Wip, 20)
                    {
                        Polygon = area.Polygon
                    };
                    Engine.StorageManager.RegisterZone(sz);
                }
                else if (area.Category == "MutexZone")
                {
                    var existingZone = result.Graph.GetZone(area.Id);
                    if (existingZone == null)
                    {
                        var mz = new MutexZone(area.Id, string.IsNullOrEmpty(area.Name) ? area.Id : area.Name, 1, 60.0)
                        {
                            Polygon = area.Polygon
                        };
                        result.Graph.AddZone(mz);
                    }
                }
                else if (area.Category == "ForbiddenZone")
                {
                    // 禁行区：将落在该多边形范围内的航道设为封锁
                    foreach (var path in result.Graph.Paths)
                    {
                        var samples = path.Sample(0.5);
                        if (samples.Any(pt => MutexZone.IsPointInPolygon(pt, area.Polygon)))
                        {
                            path.IsBlocked = true;
                        }
                    }
                }
                else if (area.Category == "SpeedLimitZone")
                {
                    // 限速区：将落在该多边形范围内的航道线速度设为安全限速 (0.6 m/s)
                    foreach (var path in result.Graph.Paths)
                    {
                        var samples = path.Sample(0.5);
                        if (samples.Any(pt => MutexZone.IsPointInPolygon(pt, area.Polygon)))
                        {
                            path.MaxSpeed = Math.Min(path.MaxSpeed, 0.6);
                        }
                    }
                }
            }
        }

        // 重新分配车队位置到地图上的安全地标点
        var startNodes = result.Graph.Nodes
            .Where(n => n.Type == NodeType.StandbyPoint || n.Type == NodeType.ChargePoint || n.Type == NodeType.Landmark)
            .Take(10)
            .ToList();

        if (startNodes.Count > 0)
        {
            int idx = 0;
            foreach (var kv in Engine.Vehicles)
            {
                var targetNode = startNodes[idx % startNodes.Count];
                kv.Value.Pose = new Pose2D(targetNode.Position.X, targetNode.Position.Y, targetNode.HeadingDeg * Math.PI / 180.0);
                kv.Value.CurrentNodeId = targetNode.Id;
                kv.Value.TargetNodeId = null;
                kv.Value.ClearRoute();
                idx++;
            }
        }

        // 货位必须绑定在当前地图的真实站点上，否则下发的搬运任务无处可去
        Engine.RebindStorageToGraph();

        Engine.RoadNetworkManager.SyncDraftFrom(Engine.Graph);
    }

    public XmapParseResultDto LoadMapinfoJson(string json)
    {
        var result = MapParser.ParseMapinfoJson(json);
        ApplyParsedMap(result);
        return result;
    }

    public XmapParseResultDto LoadEditorMapJson(string json)
    {
        var result = MapParser.ParseEditorJson(json);
        ApplyParsedMap(result);
        return result;
    }

    public void LoadPresetBenchmarkMap()
    {
        var graph = Engine.Graph;
        graph.Clear();
        graph.Name = "NXP-Cleanroom-Benchmark";
        graph.MinX = 0; graph.MaxX = 50;
        graph.MinY = 0; graph.MaxY = 30;

        var nStk1 = new MapNode("STK-01", new Point2D(5, 5), NodeType.JobPoint, "Stocker-01 货架");
        var nStk2 = new MapNode("STK-02", new Point2D(5, 25), NodeType.JobPoint, "Stocker-02 货架");
        var nWb1 = new MapNode("WB-01", new Point2D(45, 5), NodeType.JobPoint, "WireBonder-01 机台");
        var nWb2 = new MapNode("WB-02", new Point2D(45, 25), NodeType.JobPoint, "WireBonder-02 机台");
        var nSpur1 = new MapNode("SPUR-01", new Point2D(25, 2), NodeType.PullOverPoint, "主干道避让站 1");
        var nSpur2 = new MapNode("SPUR-02", new Point2D(25, 28), NodeType.PullOverPoint, "主干道避让站 2");
        var nPark1 = new MapNode("PARK-01", new Point2D(15, 5), NodeType.PullOverPoint, "待命停靠区 1");
        var nPark2 = new MapNode("PARK-02", new Point2D(35, 25), NodeType.PullOverPoint, "待命停靠区 2");
        var nCharge = new MapNode("CH-01", new Point2D(25, 20), NodeType.ChargePoint, "自动充电机 1");

        var nMidLeft = new MapNode("JUNCT-L", new Point2D(15, 15), NodeType.Landmark, "左分流道岔");
        var nMidCenter = new MapNode("JUNCT-C", new Point2D(25, 15), NodeType.Landmark, "核心十字路口");
        var nMidRight = new MapNode("JUNCT-R", new Point2D(35, 15), NodeType.Landmark, "右汇流道岔");

        graph.AddNode(nStk1); graph.AddNode(nStk2);
        graph.AddNode(nWb1); graph.AddNode(nWb2);
        graph.AddNode(nSpur1); graph.AddNode(nSpur2);
        graph.AddNode(nPark1); graph.AddNode(nPark2);
        graph.AddNode(nCharge);
        graph.AddNode(nMidLeft); graph.AddNode(nMidCenter); graph.AddNode(nMidRight);

        graph.AddPath(new StraightLinePath("P-STK1-L", "STK-01", "JUNCT-L", nStk1.Position, nMidLeft.Position, bidirectional: true));
        graph.AddPath(new StraightLinePath("P-STK2-L", "STK-02", "JUNCT-L", nStk2.Position, nMidLeft.Position, bidirectional: true));
        graph.AddPath(new StraightLinePath("P-PARK1-L", "PARK-01", "JUNCT-L", nPark1.Position, nMidLeft.Position, bidirectional: true));
        graph.AddPath(new StraightLinePath("P-PARK2-R", "PARK-02", "JUNCT-R", nPark2.Position, nMidRight.Position, bidirectional: true));
        graph.AddPath(new StraightLinePath("P-CH1-C", "CH-01", "JUNCT-C", nCharge.Position, nMidCenter.Position, bidirectional: true));
        graph.AddPath(new StraightLinePath("P-MAIN-1", "JUNCT-L", "JUNCT-C", nMidLeft.Position, nMidCenter.Position, bidirectional: true));
        graph.AddPath(new StraightLinePath("P-MAIN-2", "JUNCT-C", "JUNCT-R", nMidCenter.Position, nMidRight.Position, bidirectional: true));

        graph.AddPath(new CubicBezierPath("P-SPUR-1", "JUNCT-C", "SPUR-01",
            nMidCenter.Position, new Point2D(25, 8), new Point2D(25, 4), nSpur1.Position, bidirectional: true));
        graph.AddPath(new CubicBezierPath("P-SPUR-2", "JUNCT-C", "SPUR-02",
            nMidCenter.Position, new Point2D(25, 22), new Point2D(25, 26), nSpur2.Position, bidirectional: true));

        graph.AddPath(new StraightLinePath("P-R-WB1", "JUNCT-R", "WB-01", nMidRight.Position, nWb1.Position, bidirectional: true));
        graph.AddPath(new StraightLinePath("P-R-WB2", "JUNCT-R", "WB-02", nMidRight.Position, nWb2.Position, bidirectional: true));

        var crossZone = new MutexZone
        {
            Id = "ZONE-JUNCT-C",
            Name = "核心十字交叉路口防冲区",
            MaxCapacity = 1,
            TimeoutSec = 30.0,
            Polygon = new List<Point2D>
            {
                new(21, 11), new(29, 11), new(29, 19), new(21, 19)
            },
            AssociatedPathIds = new HashSet<string> { "P-MAIN-1", "P-MAIN-2", "P-SPUR-1", "P-SPUR-2", "P-CH1-C" }
        };
        graph.AddZone(crossZone);

        CurrentPointCloud.Clear();
        TotalPointCloudCount = 0;
        CurrentRaster = null;

        if (Engine.Vehicles.TryGetValue("1", out var v1)) { v1.Pose = new Pose2D(5, 5, 0); v1.CurrentNodeId = "STK-01"; v1.ClearRoute(); }
        if (Engine.Vehicles.TryGetValue("2", out var v2)) { v2.Pose = new Pose2D(15, 5, 0); v2.CurrentNodeId = "PARK-01"; v2.ClearRoute(); }
        if (Engine.Vehicles.TryGetValue("3", out var v3)) { v3.Pose = new Pose2D(25, 20, 0); v3.CurrentNodeId = "CH-01"; v3.ClearRoute(); }
    }

    public void RegisterVehicle(string id, string name, double x = 0, double y = 0, double theta = 0)
    {
        var v = new VehicleAggregate(id, name, new Pose2D(x, y, theta));
        Engine.RegisterVehicle(v);
    }

    public void SubmitOrder(string orderSerial, string targetNodeId, int priority = 50, string? bindVehicleId = null)
    {
        var order = new TransportOrderAggregate(orderSerial, priority, bindVehicleId);
        order.Steps.Add(new OrderStep
        {
            StepIndex = 0,
            Action = ActionType.Move,
            TargetNodeId = targetNodeId,
            Name = $"前往 {targetNodeId}"
        });
        Engine.SubmitOrder(order);
    }

    public void PauseSimulation() => Engine.Clock.Pause();
    public void ResumeSimulation() => Engine.Clock.Resume();
    public void SetTimeScale(double scale) => Engine.Clock.SetTimeScale(scale);
    public void StepSimulation(double dtSec = 0.1) => Engine.AdvanceTick(dtSec);

    /// <summary>
    /// 将当前内存中的拓扑地图与点云按照科聪工业标准 XML 导出为 .xmap 格式
    /// </summary>
    public string ExportCurrentMapToXmapXml()
    {
        return MapSerializer.SerializeToXmapXml(Engine.Graph, CurrentPointCloud);
    }

    /// <summary>
    /// 将当前内存中的拓扑地图（节点、航道、互斥区）持久化落库到 SQLite 数据库
    /// </summary>
    public async Task SaveCurrentMapAsync(FmsDbContext? externalDb = null)
    {
        if (externalDb != null)
        {
            await SaveCurrentMapInternalAsync(externalDb);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        await SaveCurrentMapInternalAsync(db);
    }

    private async Task SaveCurrentMapInternalAsync(FmsDbContext db)
    {
        var graph = Engine.Graph;

        // 清除旧拓扑数据
        db.MapPaths.RemoveRange(db.MapPaths);
        db.MapNodes.RemoveRange(db.MapNodes);
        db.MutexZones.RemoveRange(db.MutexZones);
        await db.SaveChangesAsync();

        // 1. 保存所有节点
        foreach (var node in graph.Nodes)
        {
            db.MapNodes.Add(new MapNodeRecord
            {
                Id = node.Id,
                Name = string.IsNullOrWhiteSpace(node.Name) ? node.Id : node.Name,
                X = node.Position.X,
                Y = node.Position.Y,
                NodeType = node.Type.ToString(),
                HeadingDeg = node.HeadingDeg,
                AllowRevolve = node.AllowRevolve,
                SpinDirection = node.SpinDirection ?? "both",
                ClassName = node.ClassName ?? (node.Type == NodeType.ChargePoint ? "ChargePoint" : node.Type == NodeType.StandbyPoint ? "ReturnPoint" : "LandMark")
            });
        }

        // 2. 保存所有路径
        foreach (var path in graph.Paths)
        {
            string cpJson = "[]";
            string pathType = "Straight";
            string curveType = "straight_line";

            if (path is CubicBezierPath bezier)
            {
                pathType = "Bezier";
                curveType = "bezier_curve";
                cpJson = JsonSerializer.Serialize(new[]
                {
                    new { x = bezier.P0.X, y = bezier.P0.Y },
                    new { x = bezier.P1.X, y = bezier.P1.Y },
                    new { x = bezier.P2.X, y = bezier.P2.Y },
                    new { x = bezier.P3.X, y = bezier.P3.Y }
                });
            }
            else if (path is StraightLinePath line)
            {
                cpJson = JsonSerializer.Serialize(new[]
                {
                    new { x = line.Start.X, y = line.Start.Y },
                    new { x = line.End.X, y = line.End.Y }
                });
            }

            db.MapPaths.Add(new MapPathRecord
            {
                Id = path.Id,
                StartNodeId = path.FromNodeId,
                EndNodeId = path.ToNodeId,
                PathType = pathType,
                ControlPointsJson = cpJson,
                MaxSpeed = path.MaxSpeed,
                Length = path.Length,
                IsBidirectional = path.Bidirectional,
                Direction = path.Direction ?? "forward",
                RoadWidth = path.RoadWidth,
                CurveType = curveType
            });
        }

        // 3. 保存所有互斥区
        foreach (var zone in graph.Zones)
        {
            var polyJson = JsonSerializer.Serialize(zone.Polygon.Select(p => new { x = p.X, y = p.Y }));
            var pathIdsJson = JsonSerializer.Serialize(zone.AssociatedPathIds);
            db.MutexZones.Add(new MutexZoneRecord
            {
                Id = zone.Id,
                Name = zone.Name,
                MaxCapacity = zone.MaxCapacity,
                TimeoutSec = zone.TimeoutSec,
                PolygonJson = polyJson,
                MemberNodeIdsJson = "[]",
                MemberPathIdsJson = pathIdsJson
            });
        }

        // 4. 保存库区与货位 (持久化 StorageManager 的业务状态)
        db.StorageZones.RemoveRange(db.StorageZones);
        db.StorageSlots.RemoveRange(db.StorageSlots);
        foreach (var zone in Engine.StorageManager.GetAllZones())
        {
            db.StorageZones.Add(new StorageZoneRecord
            {
                Id = zone.Id,
                Name = zone.Name,
                Type = zone.Type.ToString(),
                Capacity = zone.Capacity,
                AllowInbound = zone.AllowInbound,
                AllowOutbound = zone.AllowOutbound,
                PolygonJson = JsonSerializer.Serialize(zone.Polygon.Select(p => new { x = p.X, y = p.Y })),
                UpdatedAt = DateTime.UtcNow
            });
        }
        foreach (var slot in Engine.StorageManager.GetAllSlots())
        {
            db.StorageSlots.Add(new StorageSlotRecord
            {
                Id = slot.Id,
                Name = slot.Name,
                ZoneId = slot.ZoneId,
                ZoneType = slot.ZoneType.ToString(),
                BindNodeId = slot.BindNodeId,
                Status = slot.Status.ToString(),
                OccupiedCarrierId = slot.OccupiedCarrierId,
                ReservedOrderId = slot.ReservedOrderId,
                ReservedVehicleId = slot.ReservedVehicleId,
                LockReason = slot.LockReason,
                UpdatedAt = slot.UpdatedAt
            });
        }

        // 5. 保存线路视图 (RouteLines)
        db.RouteLines.RemoveRange(db.RouteLines);
        foreach (var line in Engine.RoadNetworkManager.GetAllRouteLines())
        {
            db.RouteLines.Add(new RouteLineRecord
            {
                Id = line.Id,
                Name = line.Name,
                MemberPathIdsJson = JsonSerializer.Serialize(line.MemberPathIds),
                AllowedVehicleTypesJson = JsonSerializer.Serialize(line.AllowedVehicleTypes),
                DefaultDirection = line.DefaultDirection,
                AllowBackward = line.AllowBackward,
                SpeedLimit = line.SpeedLimit,
                IsClosed = line.IsClosed,
                ClosureReason = line.ClosureReason
            });
        }

        // 6. 保存激光点云数据 (二进制紧凑打包，重启后完整复原激光层与 xmap 导出)
        var pcRecord = await db.MapPointClouds.FirstOrDefaultAsync(p => p.Id == "default");
        if (pcRecord == null)
        {
            pcRecord = new MapPointCloudRecord { Id = "default" };
            db.MapPointClouds.Add(pcRecord);
        }

        if (CurrentPointCloud.Count > 0)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(CurrentPointCloud.Count);
            foreach (var pt in CurrentPointCloud)
            {
                bw.Write((float)pt.X);
                bw.Write((float)pt.Y);
            }
            pcRecord.Data = ms.ToArray();
            pcRecord.TotalCount = CurrentPointCloud.Count;
            pcRecord.RawTotalCount = TotalPointCloudCount > 0 ? TotalPointCloudCount : CurrentPointCloud.Count;
            pcRecord.DownsampleFactor = DownsampleFactor > 0 ? DownsampleFactor : 25;
        }
        else
        {
            pcRecord.Data = Array.Empty<byte>();
            pcRecord.TotalCount = 0;
            pcRecord.RawTotalCount = 0;
            pcRecord.DownsampleFactor = 25;
        }
        pcRecord.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// 重启时从 SQLite 数据库恢复地图拓扑、车队最新位姿与未决工单
    /// </summary>
    public async Task<bool> RestoreFromDatabaseAsync(FmsDbContext? externalDb = null)
    {
        if (externalDb != null)
        {
            return await RestoreFromDatabaseInternalAsync(externalDb);
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        return await RestoreFromDatabaseInternalAsync(db);
    }

    private async Task<bool> RestoreFromDatabaseInternalAsync(FmsDbContext db)
    {
        // 1. 恢复拓扑地图
        var dbNodes = await db.MapNodes.AsNoTracking().ToListAsync();
        if (dbNodes.Count == 0)
        {
            // 数据库无地图，载入预置洁净室地图并保存到数据库
            LoadPresetBenchmarkMap();
            await SaveCurrentMapInternalAsync(db);
        }
        else
        {
            Engine.Graph.Clear();
            Engine.Graph.Name = "Restored-Topology-Map";

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            foreach (var nr in dbNodes)
            {
                minX = Math.Min(minX, nr.X);
                maxX = Math.Max(maxX, nr.X);
                minY = Math.Min(minY, nr.Y);
                maxY = Math.Max(maxY, nr.Y);

                Enum.TryParse<NodeType>(nr.NodeType, out var nt);
                var node = new MapNode(nr.Id, new Point2D(nr.X, nr.Y), nt, nr.Name)
                {
                    HeadingDeg = nr.HeadingDeg,
                    AllowRevolve = nr.AllowRevolve,
                    SpinDirection = nr.SpinDirection ?? "both",
                    ClassName = nr.ClassName ?? "LandMark"
                };
                Engine.Graph.AddNode(node);
            }

            Engine.Graph.MinX = minX == double.MaxValue ? 0 : minX;
            Engine.Graph.MaxX = maxX == double.MinValue ? 50 : maxX;
            Engine.Graph.MinY = minY == double.MaxValue ? 0 : minY;
            Engine.Graph.MaxY = maxY == double.MinValue ? 30 : maxY;

            var dbPaths = await db.MapPaths.AsNoTracking().ToListAsync();
            foreach (var pr in dbPaths)
            {
                var startNode = Engine.Graph.GetNode(pr.StartNodeId);
                var endNode = Engine.Graph.GetNode(pr.EndNodeId);
                if (startNode == null || endNode == null) continue;

                if (pr.PathType == "Bezier" || pr.CurveType == "bezier_curve")
                {
                    Point2D p0 = startNode.Position;
                    Point2D p1 = startNode.Position;
                    Point2D p2 = endNode.Position;
                    Point2D p3 = endNode.Position;

                    try
                    {
                        using var doc = JsonDocument.Parse(pr.ControlPointsJson);
                        var array = doc.RootElement;
                        if (array.GetArrayLength() >= 4)
                        {
                            p0 = new Point2D(array[0].GetProperty("x").GetDouble(), array[0].GetProperty("y").GetDouble());
                            p1 = new Point2D(array[1].GetProperty("x").GetDouble(), array[1].GetProperty("y").GetDouble());
                            p2 = new Point2D(array[2].GetProperty("x").GetDouble(), array[2].GetProperty("y").GetDouble());
                            p3 = new Point2D(array[3].GetProperty("x").GetDouble(), array[3].GetProperty("y").GetDouble());
                        }
                    }
                    catch { }

                    var bezier = new CubicBezierPath(pr.Id, pr.StartNodeId, pr.EndNodeId, p0, p1, p2, p3, pr.IsBidirectional)
                    {
                        MaxSpeed = pr.MaxSpeed > 0 ? pr.MaxSpeed : 1.5,
                        Direction = pr.Direction ?? "forward",
                        RoadWidth = pr.RoadWidth > 0 ? pr.RoadWidth : 1.2
                    };
                    Engine.Graph.AddPath(bezier);
                }
                else
                {
                    var line = new StraightLinePath(pr.Id, pr.StartNodeId, pr.EndNodeId, startNode.Position, endNode.Position, pr.IsBidirectional)
                    {
                        MaxSpeed = pr.MaxSpeed > 0 ? pr.MaxSpeed : 1.5,
                        Direction = pr.Direction ?? "forward",
                        RoadWidth = pr.RoadWidth > 0 ? pr.RoadWidth : 1.2
                    };
                    Engine.Graph.AddPath(line);
                }
            }

            var dbZones = await db.MutexZones.AsNoTracking().ToListAsync();
            foreach (var zr in dbZones)
            {
                var polyList = new List<Point2D>();
                try
                {
                    using var pDoc = JsonDocument.Parse(zr.PolygonJson);
                    foreach (var item in pDoc.RootElement.EnumerateArray())
                    {
                        polyList.Add(new Point2D(item.GetProperty("x").GetDouble(), item.GetProperty("y").GetDouble()));
                    }
                }
                catch { }

                var pathIds = new HashSet<string>();
                try
                {
                    using var pDoc = JsonDocument.Parse(zr.MemberPathIdsJson);
                    foreach (var item in pDoc.RootElement.EnumerateArray())
                    {
                        pathIds.Add(item.GetString() ?? "");
                    }
                }
                catch { }

                var zone = new MutexZone
                {
                    Id = zr.Id,
                    Name = zr.Name,
                    MaxCapacity = zr.MaxCapacity > 0 ? zr.MaxCapacity : 1,
                    TimeoutSec = zr.TimeoutSec > 0 ? zr.TimeoutSec : 30.0,
                    Polygon = polyList,
                    AssociatedPathIds = pathIds
                };
                Engine.Graph.AddZone(zone);
            }
            Engine.RoadNetworkManager.SyncDraftFrom(Engine.Graph);
        }

        // 2. 从数据库恢复激光点云
        var pcRecord = await db.MapPointClouds.AsNoTracking().FirstOrDefaultAsync(p => p.Id == "default");
        if (pcRecord != null && pcRecord.Data != null && pcRecord.Data.Length > 0)
        {
            try
            {
                using var ms = new MemoryStream(pcRecord.Data);
                using var br = new BinaryReader(ms);
                int count = br.ReadInt32();
                var points = new List<Point2D>(count);
                for (int i = 0; i < count; i++)
                {
                    float x = br.ReadSingle();
                    float y = br.ReadSingle();
                    points.Add(new Point2D(x, y));
                }
                CurrentPointCloud = points;
                TotalPointCloudCount = pcRecord.RawTotalCount > 0 ? pcRecord.RawTotalCount : points.Count;
                DownsampleFactor = pcRecord.DownsampleFactor > 0 ? pcRecord.DownsampleFactor : 25;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FmsAppService] Failed to unpack point cloud from db: {ex.Message}");
            }
        }

        // 3. 从数据库恢复车辆
        var dbVehicles = await db.Vehicles.AsNoTracking().ToListAsync();
        if (dbVehicles.Count > 0)
        {
            foreach (var vr in dbVehicles)
            {
                ApplyVehicleRecord(vr);
            }
        }
        else
        {
            if (!Engine.Vehicles.ContainsKey("1")) RegisterVehicle("1", "AGV-01", 5, 5, 0);
            if (!Engine.Vehicles.ContainsKey("2")) RegisterVehicle("2", "AGV-02", 15, 5, 0);
            if (!Engine.Vehicles.ContainsKey("3")) RegisterVehicle("3", "AGV-03", 25, 20, 0);
        }

        Engine.BindVehiclesToCurrentMapIfEmpty();

        // 4. 从数据库恢复工单：未决工单（Queued/Assigned/Running/WaitingAction）继续执行；
        //    最近的已结束工单（Done/Failed/Cancelled）作为只读历史登记，任务中心重启后仍能看到它们的最终状态
        const int historyKept = 300;
        var pendingOrders = await db.Orders
            .Include(o => o.Steps)
            .Where(o => o.Status != "Done" && o.Status != "Failed" && o.Status != "Cancelled")
            .OrderBy(o => o.Priority)
            .AsNoTracking()
            .ToListAsync();
        var finishedOrders = await db.Orders
            .Include(o => o.Steps)
            .Where(o => o.Status == "Done" || o.Status == "Failed" || o.Status == "Cancelled")
            .OrderByDescending(o => o.CreatedAt)
            .Take(historyKept)
            .AsNoTracking()
            .ToListAsync();
        pendingOrders.AddRange(finishedOrders);

        foreach (var oRec in pendingOrders)
        {
            if (Engine.Orders.ContainsKey(oRec.Id) || Engine.Orders.Values.Any(o => o.OrderSerial == oRec.OrderSerial))
            {
                continue;
            }

            var order = new TransportOrderAggregate(oRec.OrderSerial, oRec.Priority, oRec.BindVehicleId)
            {
                Id = oRec.Id,
                TaskType = oRec.TaskType,
                BulkTaskType = string.IsNullOrEmpty(oRec.BulkTaskType) ? "task" : oRec.BulkTaskType,
                SubNo = oRec.SubNo,
                TaskCodeA = oRec.TaskCodeA,
                TaskCodeB = oRec.TaskCodeB,
                FinishingAction = oRec.FinishingAction,
                CargoSerial = oRec.CargoSerial,
                ArmAction = oRec.ArmAction,
                Rfid = oRec.Rfid,
                Mark = oRec.Mark,
                OptionCode = oRec.OptionCode,
                PassPoint = oRec.PassPoint,
                PassPointToGoal = oRec.PassPointToGoal,
                RobotType = oRec.RobotType.ToString(),
                Storage = oRec.Storage,
                Target = oRec.Target,
                AssignedVehicleId = oRec.AssignedVehicleId,
                CurrentStepIndex = oRec.CurrentStepIndex,
                CreatedAt = oRec.CreatedAt,
                StartedAt = oRec.StartedAt,
                CompletedAt = oRec.CompletedAt,
                FailReason = oRec.FailReason,
                TotalWalkTimeCostSec = oRec.MovementTimeSec,
                TotalWaitingTimeCostSec = oRec.WaitTimeSec,
                TotalOdomMeter = oRec.MileageKm * 1000.0
            };

            if (Enum.TryParse<OrderStatus>(oRec.Status, out var parsedStatus))
            {
                order.RestoreStatus(parsedStatus);
            }

            // 优先从 OrderStepsJson 全量还原步骤
            bool stepsRestored = false;
            if (!string.IsNullOrEmpty(oRec.OrderStepsJson) && oRec.OrderStepsJson != "[]")
            {
                try
                {
                    var steps = JsonSerializer.Deserialize<List<OrderStep>>(oRec.OrderStepsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (steps != null && steps.Count > 0)
                    {
                        order.Steps = steps;
                        stepsRestored = true;
                    }
                }
                catch { }
            }

            if (!stepsRestored)
            {
                foreach (var s in oRec.Steps.OrderBy(x => x.StepNo))
                {
                    order.Steps.Add(new OrderStep
                    {
                        StepIndex = s.StepNo,
                        Action = ActionType.Move,
                        TargetNodeId = oRec.TargetNodeId,
                        Name = s.Name
                    });
                }

                if (order.Steps.Count == 0 && !string.IsNullOrEmpty(oRec.TargetNodeId))
                {
                    order.Steps.Add(new OrderStep
                    {
                        StepIndex = 0,
                        Action = ActionType.Move,
                        TargetNodeId = oRec.TargetNodeId,
                        Name = $"前往 {oRec.TargetNodeId}"
                    });
                }
            }

            // 将订单与已分配车辆重新绑定并恢复运行
            Engine.RestoreOrder(order);
        }

        // 5. 从数据库恢复库区与货位 (StorageManager)
        var dbStorageZones = await db.StorageZones.AsNoTracking().ToListAsync();
        var dbStorageSlots = await db.StorageSlots.AsNoTracking().ToListAsync();
        if (dbStorageZones.Count > 0 || dbStorageSlots.Count > 0)
        {
            foreach (var zr in dbStorageZones)
            {
                Enum.TryParse<StorageZoneType>(zr.Type, true, out var zt);
                var poly = new List<Point2D>();
                try
                {
                    using var pDoc = JsonDocument.Parse(zr.PolygonJson);
                    foreach (var item in pDoc.RootElement.EnumerateArray())
                        poly.Add(new Point2D(item.GetProperty("x").GetDouble(), item.GetProperty("y").GetDouble()));
                }
                catch { }
                var sz = new StorageZone(zr.Id, zr.Name, zt, zr.Capacity)
                {
                    AllowInbound = zr.AllowInbound,
                    AllowOutbound = zr.AllowOutbound,
                    Polygon = poly
                };
                Engine.StorageManager.RegisterZone(sz);
            }
            foreach (var sr in dbStorageSlots)
            {
                Enum.TryParse<StorageZoneType>(sr.ZoneType, true, out var zt);
                Enum.TryParse<StorageSlotStatus>(sr.Status, true, out var st);
                var slot = new StorageSlot
                {
                    Id = sr.Id,
                    Name = sr.Name,
                    ZoneId = sr.ZoneId,
                    ZoneType = zt,
                    BindNodeId = sr.BindNodeId,
                    Status = st,
                    OccupiedCarrierId = sr.OccupiedCarrierId,
                    ReservedOrderId = sr.ReservedOrderId,
                    ReservedVehicleId = sr.ReservedVehicleId,
                    LockReason = sr.LockReason,
                    UpdatedAt = sr.UpdatedAt
                };
                Engine.StorageManager.RegisterSlot(slot);
            }
        }

        // 6. 从数据库恢复线路 (RouteLines)
        var dbRouteLines = await db.RouteLines.AsNoTracking().ToListAsync();
        foreach (var lr in dbRouteLines)
        {
            var pathIds = new List<string>();
            var vehTypes = new List<string>();
            try { pathIds = JsonSerializer.Deserialize<List<string>>(lr.MemberPathIdsJson) ?? new(); } catch { }
            try { vehTypes = JsonSerializer.Deserialize<List<string>>(lr.AllowedVehicleTypesJson) ?? new(); } catch { }
            var rl = new RouteLine
            {
                Id = lr.Id,
                Name = lr.Name,
                MemberPathIds = pathIds,
                AllowedVehicleTypes = vehTypes,
                DefaultDirection = lr.DefaultDirection,
                AllowBackward = lr.AllowBackward,
                SpeedLimit = lr.SpeedLimit,
                IsClosed = lr.IsClosed,
                ClosureReason = lr.ClosureReason
            };
            Engine.RoadNetworkManager.RegisterRouteLine(rl);
        }

        Engine.RebindStorageToGraph();

        return true;
    }

    private void ApplyVehicleRecord(VehicleRecord vr)
    {
        if (Engine.Vehicles.TryGetValue(vr.Id, out var existing))
        {
            HydrateVehicleFromRecord(existing, vr);
            Engine.BindVehicleToCurrentMapIfEmpty(existing);
            Engine.SnapVehicleToNearestNodeIfNeeded(existing);
            Engine.SnapHomeNodeIfNeeded(existing);
            return;
        }

        var v = new VehicleAggregate(vr.Id, string.IsNullOrWhiteSpace(vr.Name) ? vr.Code : vr.Name, new Pose2D(vr.InitialX, vr.InitialY, vr.InitialTheta));
        HydrateVehicleFromRecord(v, vr);
        Engine.RegisterVehicle(v);
    }

    private static void HydrateVehicleFromRecord(VehicleAggregate v, VehicleRecord vr)
    {
        v.Name = string.IsNullOrWhiteSpace(vr.Name) ? (string.IsNullOrWhiteSpace(vr.Code) ? v.Id : vr.Code) : vr.Name;
        v.Code = string.IsNullOrWhiteSpace(vr.Code) ? v.Id : vr.Code;
        if (!string.IsNullOrWhiteSpace(vr.VehicleType)) v.VehicleType = vr.VehicleType;
        v.Pose = new Pose2D(vr.InitialX, vr.InitialY, vr.InitialTheta);
        v.Bms.SocPercent = vr.BatteryPercent;
        v.Bms.VoltageV = vr.BatteryVoltage > 0 ? vr.BatteryVoltage : v.Bms.VoltageV;
        v.IsOnline = vr.IsOnline;
        v.IsEnabled = vr.IsEnabled;
        v.IsEstop = vr.IsEstop;
        v.Kinematics.MaxSpeed = vr.MaxSpeed > 0 ? vr.MaxSpeed : v.Kinematics.MaxSpeed;
        if (vr.Length > 0) v.Kinematics.Length = vr.Length;
        if (vr.Width > 0) v.Kinematics.Width = vr.Width;
        if (!string.IsNullOrWhiteSpace(vr.DriveType) && Enum.TryParse<Fms.Domain.Shared.Enums.DriveType>(vr.DriveType, true, out var drive))
        {
            v.Kinematics.DriveType = drive;
        }
        if (!string.IsNullOrWhiteSpace(vr.MapId)) v.MapId = vr.MapId;
        if (!string.IsNullOrWhiteSpace(vr.MapName)) v.MapName = vr.MapName;
        if (!string.IsNullOrWhiteSpace(vr.HomeNodeId)) v.HomeNodeId = vr.HomeNodeId;
        if (!string.IsNullOrWhiteSpace(vr.CurrentNodeId)) v.CurrentNodeId = vr.CurrentNodeId;
        if (vr.LastHeartbeatUtc.HasValue && vr.LastHeartbeatUtc.Value != default) v.LastHeartbeatUtc = vr.LastHeartbeatUtc.Value;
    }

    public async Task PersistVehiclesAsync(FmsDbContext? externalDb = null)
    {
        if (externalDb != null)
        {
            await UpsertVehiclesAsync(externalDb);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        await UpsertVehiclesAsync(db);
    }

    public static async Task UpsertVehiclesAsync(FmsDbContext db, IEnumerable<VehicleAggregate> vehicles)
    {
        var existing = await db.Vehicles.ToDictionaryAsync(v => v.Id);
        foreach (var v in vehicles)
        {
            if (!existing.TryGetValue(v.Id, out var record))
            {
                record = new VehicleRecord { Id = v.Id };
                db.Vehicles.Add(record);
            }
            WriteVehicleRecord(v, record);
        }
        await db.SaveChangesAsync();
    }

    private async Task UpsertVehiclesAsync(FmsDbContext db) =>
        await UpsertVehiclesAsync(db, Engine.Vehicles.Values);

    public static void WriteVehicleRecord(VehicleAggregate v, VehicleRecord record)
    {
        record.Id = v.Id;
        record.Code = string.IsNullOrWhiteSpace(v.Code) ? v.Id : v.Code;
        record.Name = string.IsNullOrWhiteSpace(v.Name) ? record.Code : v.Name;
        record.VehicleType = string.IsNullOrWhiteSpace(v.VehicleType) ? "LATENT_AMR" : v.VehicleType;
        record.InitialX = v.Pose.X;
        record.InitialY = v.Pose.Y;
        record.InitialTheta = v.Pose.ThetaRad;
        record.MaxSpeed = v.Kinematics.MaxSpeed;
        record.Length = v.Kinematics.Length;
        record.Width = v.Kinematics.Width;
        record.DriveType = v.Kinematics.DriveType.ToString();
        record.BatteryPercent = Math.Round(v.Bms.SocPercent, 1);
        record.BatteryVoltage = Math.Round(v.Bms.VoltageV, 1);
        record.Status = v.Status.ToString();
        record.IsOnline = v.IsOnline;
        record.IsEnabled = v.IsEnabled;
        record.IsEstop = v.IsEstop;
        record.MapId = v.MapId ?? string.Empty;
        record.MapName = v.MapName ?? string.Empty;
        record.HomeNodeId = v.HomeNodeId;
        record.CurrentNodeId = v.CurrentNodeId;
        record.LastHeartbeatUtc = v.LastHeartbeatUtc;
        record.UpdatedAt = DateTime.UtcNow;
    }
}
