using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Fms.Application;
using Fms.Application.Map;
using Fms.Application.Rcs;
using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Topology;
using Fms.Domain.Vehicles;
using Fms.Engine;
using Fms.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fms.Tests;

public class MapPersistenceAndExportTests
{
    [Fact]
    public void SerializeToXmapXml_ShouldGenerateValidKecongXml()
    {
        var serializer = new XMapSerializerService();
        var graph = new TopologyGraph { Name = "Test-Export-Map", MinX = 0, MaxX = 100, MinY = 0, MaxY = 80 };

        var nodeA = new MapNode("ND-01", new Point2D(10, 20), NodeType.JobPoint, "工位 1")
        {
            HeadingDeg = 90,
            AllowRevolve = true,
            SpinDirection = "both",
            ClassName = "JobPoint"
        };
        var nodeB = new MapNode("ND-02", new Point2D(50, 20), NodeType.ChargePoint, "充电站 1")
        {
            HeadingDeg = 180,
            AllowRevolve = false,
            SpinDirection = "none",
            ClassName = "ChargePoint"
        };
        graph.AddNode(nodeA);
        graph.AddNode(nodeB);

        var bezier = new CubicBezierPath("PATH-01", "ND-01", "ND-02",
            nodeA.Position, new Point2D(20, 30), new Point2D(40, 30), nodeB.Position, bidirectional: false)
        {
            Direction = "backward",
            RoadWidth = 1.5,
            MaxSpeed = 1.2
        };
        graph.AddPath(bezier);

        var zone = new MutexZone
        {
            Id = "ZONE-01",
            Name = "交叉互斥防冲区 1",
            MaxCapacity = 1,
            TimeoutSec = 45.0,
            Polygon = new List<Point2D> { new(15, 15), new(35, 15), new(35, 35), new(15, 35) },
            AssociatedPathIds = new HashSet<string> { "PATH-01" }
        };
        graph.AddZone(zone);

        var pointCloud = new List<Point2D> { new(10.5, 20.5), new(12.3, 21.8) };

        string xml = serializer.SerializeToXmapXml(graph, pointCloud);
        Assert.NotNull(xml);

        var doc = XDocument.Parse(xml);
        var root = doc.Element("MAP");
        Assert.NotNull(root);

        // Header
        var header = root.Element("header");
        Assert.NotNull(header);
        Assert.Equal("Test-Export-Map", header.Attribute("name")?.Value);

        // Points
        var pts = root.Elements("point").ToList();
        Assert.Equal(2, pts.Count);

        // Advanced Points
        var advPoints = root.Elements("advanced_point").ToList();
        Assert.Equal(2, advPoints.Count);
        Assert.Contains(advPoints, p => p.Attribute("id")?.Value == "ND-01" && p.Attribute("class_name")?.Value == "JobPoint");
        Assert.Contains(advPoints, p => p.Attribute("id")?.Value == "ND-02" && p.Attribute("class_name")?.Value == "ChargePoint");

        // Advanced Curve
        var advCurves = root.Elements("advanced_curve").ToList();
        Assert.Single(advCurves);
        var c = advCurves[0];
        Assert.Equal("PATH-01", c.Attribute("id")?.Value);
        Assert.Equal("bezier_curve", c.Attribute("curve_type")?.Value);
        Assert.Equal("backward", c.Attribute("Direction")?.Value);
        Assert.Equal("1.50", c.Attribute("RoadWidth")?.Value);

        // Traffic Area
        var areas = root.Elements("traffic_area").ToList();
        Assert.Single(areas);
        Assert.Equal("ZONE-01", areas[0].Attribute("id")?.Value);
        Assert.Equal("1", areas[0].Attribute("traffic_count")?.Value);
        Assert.Equal(4, areas[0].Elements("posgroup").Count());
    }

    [Fact]
    public async Task SaveAndRestoreFromDatabase_ShouldPersistAndReconstructFullTopology()
    {
        // Setup in-memory SQLite connection
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var db = new FmsDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
        }

        var services = new ServiceCollection();
        services.AddDbContext<FmsDbContext>(opt => opt.UseSqlite(connection));
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var engine = new DispatchEngine();
        var parser = new XMapParserService();
        var serializer = new XMapSerializerService();
        var rcsConfig = new RcsConfigurationService();
        var rcsCallback = new RcsCallbackService(new System.Net.Http.HttpClient(), rcsConfig);

        var appService = new FmsAppService(engine, parser, serializer, rcsCallback, rcsConfig, scopeFactory);

        // 1. Initial State: Empty DB -> Restore triggers preset benchmark map
        using (var db = new FmsDbContext(options))
        {
            bool restored = await appService.RestoreFromDatabaseAsync(db);
            Assert.True(restored);
        }

        Assert.Equal("NXP-Cleanroom-Benchmark", engine.Graph.Name);
        Assert.True(engine.Graph.Nodes.Count >= 10);
        Assert.True(engine.Graph.Paths.Count >= 10);
        Assert.True(engine.Graph.Zones.Count >= 1);

        // Verify it was written to SQLite
        using (var db = new FmsDbContext(options))
        {
            var dbNodeCount = await db.MapNodes.CountAsync();
            var dbPathCount = await db.MapPaths.CountAsync();
            var dbZoneCount = await db.MutexZones.CountAsync();
            Assert.Equal(engine.Graph.Nodes.Count, dbNodeCount);
            Assert.Equal(engine.Graph.Paths.Count, dbPathCount);
            Assert.Equal(engine.Graph.Zones.Count, dbZoneCount);
        }

        // 2. Modify map in memory and re-save
        var customNode = new MapNode("CUSTOM-STATION", new Point2D(99, 88), NodeType.JobPoint, "自定义测试站")
        {
            HeadingDeg = 45,
            AllowRevolve = true,
            SpinDirection = "both",
            ClassName = "CustomClass"
        };
        engine.Graph.AddNode(customNode);

        var customPath = new StraightLinePath("P-CUSTOM", "STK-01", "CUSTOM-STATION",
            new Point2D(5, 5), new Point2D(99, 88), bidirectional: true)
        {
            Direction = "backward",
            RoadWidth = 2.0,
            MaxSpeed = 2.5
        };
        engine.Graph.AddPath(customPath);

        // Also set PointCloud
        appService.CurrentPointCloud = new List<Point2D>
        {
            new(1.23, 4.56),
            new(7.89, 0.12)
        };

        using (var db = new FmsDbContext(options))
        {
            await appService.SaveCurrentMapAsync(db);
        }

        // 3. Clear engine memory completely and restore from DB
        var freshEngine = new DispatchEngine();
        var freshAppService = new FmsAppService(freshEngine, parser, serializer, rcsCallback, rcsConfig, scopeFactory);

        using (var db = new FmsDbContext(options))
        {
            await freshAppService.RestoreFromDatabaseAsync(db);
        }

        // Assert all nodes, paths and zones are restored
        Assert.NotNull(freshEngine.Graph.GetNode("CUSTOM-STATION"));
        var restoredNode = freshEngine.Graph.GetNode("CUSTOM-STATION");
        Assert.Equal(99, restoredNode!.Position.X);
        Assert.Equal(88, restoredNode.Position.Y);
        Assert.Equal(45, restoredNode.HeadingDeg);
        Assert.Equal("CustomClass", restoredNode.ClassName);

        Assert.NotNull(freshEngine.Graph.GetPath("P-CUSTOM"));
        var restoredPath = freshEngine.Graph.GetPath("P-CUSTOM");
        Assert.Equal("backward", restoredPath!.Direction);
        Assert.Equal(2.0, restoredPath.RoadWidth);
        Assert.Equal(2.5, restoredPath.MaxSpeed);
        Assert.True(restoredPath.Bidirectional);

        // Verify MutexZone restored with polygon
        var restoredZone = freshEngine.Graph.Zones.FirstOrDefault(z => z.Id == "ZONE-JUNCT-C");
        Assert.NotNull(restoredZone);
        Assert.Equal(4, restoredZone!.Polygon.Count);
        Assert.Contains("P-MAIN-1", restoredZone.AssociatedPathIds);

        // Verify PointCloud is fully restored from SQLite
        Assert.Equal(2, freshAppService.CurrentPointCloud.Count);
        Assert.Equal(1.23, freshAppService.CurrentPointCloud[0].X, 2);
        Assert.Equal(4.56, freshAppService.CurrentPointCloud[0].Y, 2);
    }

    [Fact]
    public async Task RestoreOrders_RunningAndAssigned_RebindsVehicleAndSteps()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var db = new FmsDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();

            // Insert map and vehicle
            db.MapNodes.Add(new Fms.EntityFrameworkCore.Entities.MapNodeRecord { Id = "STK-01", X = 0, Y = 0 });
            db.MapNodes.Add(new Fms.EntityFrameworkCore.Entities.MapNodeRecord { Id = "STK-02", X = 20, Y = 0 });
            db.MapPaths.Add(new Fms.EntityFrameworkCore.Entities.MapPathRecord
            {
                Id = "P1",
                StartNodeId = "STK-01",
                EndNodeId = "STK-02",
                PathType = "Straight",
                CurveType = "straight_line",
                Direction = "forward",
                IsBidirectional = true
            });
            db.Vehicles.Add(new Fms.EntityFrameworkCore.Entities.VehicleRecord
            {
                Id = "AGV-01",
                Code = "AGV-01",
                InitialX = 0,
                InitialY = 0,
                InitialTheta = 0,
                BatteryPercent = 90
            });

            // Insert active running order
            var steps = new List<OrderStep>
            {
                new() { StepIndex = 0, StepId = "S1", Name = "取料", Action = ActionType.LiftUp, FinishedAt = DateTime.UtcNow.AddMinutes(-5) },
                new() { StepIndex = 1, StepId = "S2", Name = "运送", Action = ActionType.Move, TargetNodeId = "STK-02" }
            };

            db.Orders.Add(new Fms.EntityFrameworkCore.Entities.TransportOrderRecord
            {
                Id = "ORD-RUN-001",
                OrderSerial = "ORD-RUN-001",
                Status = "Running",
                AssignedVehicleId = "AGV-01",
                CurrentStepIndex = 1,
                OrderStepsJson = System.Text.Json.JsonSerializer.Serialize(steps)
            });

            await db.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var rcsConfig = new RcsConfigurationService();
        var rcsCallback = new RcsCallbackService(new System.Net.Http.HttpClient(), rcsConfig);

        var engine = new DispatchEngine();
        var appService = new FmsAppService(
            engine,
            new XMapParserService(),
            new XMapSerializerService(),
            rcsCallback: rcsCallback,
            rcsConfig: rcsConfig,
            scopeFactory: scopeFactory
        );

        using (var db = new FmsDbContext(options))
        {
            await appService.RestoreFromDatabaseAsync(db);
        }

        // Wait a tick for command queue execution
        engine.AdvanceTick(0.1);

        // Verify the running order was restored and bound to AGV-01
        Assert.True(engine.Orders.ContainsKey("ORD-RUN-001"));
        var restoredOrder = engine.Orders["ORD-RUN-001"];
        Assert.Equal(OrderStatus.Running, restoredOrder.Status);
        Assert.Equal("AGV-01", restoredOrder.AssignedVehicleId);
        Assert.Equal(1, restoredOrder.CurrentStepIndex);
        Assert.Equal(2, restoredOrder.Steps.Count);

        // Verify vehicle is rebound
        Assert.True(engine.Vehicles.ContainsKey("AGV-01"));
        Assert.Equal("ORD-RUN-001", engine.Vehicles["AGV-01"].AssignedOrderId);
    }

    [Fact]
    public async Task UploadOrLoadMap_ShouldAutoPersistToSqlite_AndRestoreOnRestart()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var db = new FmsDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var rcsConfig = new RcsConfigurationService();
        var rcsCallback = new RcsCallbackService(new System.Net.Http.HttpClient(), rcsConfig);

        var engine1 = new DispatchEngine();
        var appService1 = new FmsAppService(
            engine1,
            new XMapParserService(),
            new XMapSerializerService(),
            rcsCallback: rcsCallback,
            rcsConfig: rcsConfig,
            scopeFactory: scopeFactory
        );

        // 模拟解析载入一份包含节点、贝塞尔路径、互斥区和点云的地图
        var testXmapXml = @"<MAP>
  <header name=""AutoPersistTestMap"" min_x=""0"" max_x=""120"" min_y=""0"" max_y=""90"" />
  <point x=""1.5"" y=""2.5"" />
  <point x=""3.5"" y=""4.5"" />
  <point x=""5.5"" y=""6.5"" />
  <advanced_point class_name=""LandMark"" instance_name=""AP-101"" angle=""1.57"" id=""AP-101"" allow_revolve=""1"" spin_direction=""both"">
    <pos x=""10.0"" y=""20.0"" />
  </advanced_point>
  <advanced_point class_name=""ChargePoint"" instance_name=""AP-102"" angle=""0.0"" id=""AP-102"" allow_revolve=""0"" spin_direction=""cw"">
    <pos x=""50.0"" y=""20.0"" />
  </advanced_point>
  <advanced_curve id=""PATH-01"" secondary_id=""PATH-01-1"" curve_type=""bezier_curve"" RoadWidth=""1.5"" Direction=""backward"" line_velocity=""1.2"">
    <start_pos id=""AP-101""><pos x=""10.0"" y=""20.0"" /></start_pos>
    <end_pos id=""AP-102""><pos x=""50.0"" y=""20.0"" /></end_pos>
    <control1_pos x=""20.0"" y=""30.0"" />
    <control2_pos x=""40.0"" y=""30.0"" />
  </advanced_curve>
  <traffic_area name=""traffic_area_99"" traffic_count=""1"">
    <posgroup>
      <pos x=""5"" y=""5"" />
      <pos x=""60"" y=""5"" />
      <pos x=""60"" y=""40"" />
      <pos x=""5"" y=""40"" />
    </posgroup>
  </traffic_area>
</MAP>";

        using (var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(testXmapXml)))
        {
            await appService1.LoadXmapAsync(ms);
        }

        // 调用落库保存
        using (var db = new FmsDbContext(options))
        {
            await appService1.SaveCurrentMapAsync(db);
        }

        // 验证 SQLite 数据库已写入节点、路径、互斥区、激光点云
        using (var db = new FmsDbContext(options))
        {
            var dbNodes = await db.MapNodes.ToListAsync();
            var dbPaths = await db.MapPaths.ToListAsync();
            var dbZones = await db.MutexZones.ToListAsync();
            var dbPointCloud = await db.MapPointClouds.FirstOrDefaultAsync(p => p.Id == "default");

            Assert.Equal(2, dbNodes.Count);
            Assert.Single(dbPaths);
            Assert.Equal("backward", dbPaths[0].Direction);
            Assert.Single(dbZones);
            Assert.NotNull(dbPointCloud);
            Assert.True(dbPointCloud.TotalCount > 0);
        }

        // 模拟重启：创建全新的 Engine2 与 AppService2，执行数据库恢复
        var engine2 = new DispatchEngine();
        var appService2 = new FmsAppService(
            engine2,
            new XMapParserService(),
            new XMapSerializerService(),
            rcsCallback: rcsCallback,
            rcsConfig: rcsConfig,
            scopeFactory: scopeFactory
        );

        using (var db = new FmsDbContext(options))
        {
            bool restored = await appService2.RestoreFromDatabaseAsync(db);
            Assert.True(restored);
        }

        // 验证 Engine2 拓扑图与点云已 100% 恢复
        Assert.Equal("Restored-Topology-Map", engine2.Graph.Name);
        Assert.Equal(2, engine2.Graph.Nodes.Count);
        Assert.Single(engine2.Graph.Paths);
        Assert.Equal("backward", engine2.Graph.Paths.First().Direction);
        Assert.Single(engine2.Graph.Zones);
        Assert.True(appService2.CurrentPointCloud.Count > 0);
    }
}
