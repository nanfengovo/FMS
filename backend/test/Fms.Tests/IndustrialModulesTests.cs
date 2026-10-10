using System;
using System.Collections.Generic;
using System.Linq;
using Fms.Application.Identity;
using Fms.Application.Logging;
using Fms.Application.Rcs;
using Fms.Domain.Identity;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Topology;
using Fms.Domain.Vehicles;
using Fms.Engine;
using Xunit;

namespace Fms.Tests;

public class IndustrialModulesTests
{
    // ==========================================
    // 1. 日志与黑匣子模块测试 (Logging & Flight Recorder)
    // ==========================================
    [Fact]
    public void FlightRecorder_Should_Record_Frames_And_Query_Range()
    {
        using var recorder = new FlightRecorderService(maxCapacity: 1000);
        var vehicle = new VehicleAggregate("AGV-99", "AGV-99", new Pose2D(10, 20, 0.5));
        vehicle.CurrentSpeed = 1.2;

        recorder.RecordFrame(vehicle, 1000);
        recorder.RecordFrame(vehicle, 1100);
        recorder.RecordFrame(vehicle, 1200);

        // Allow async channel consumer to process
        for (int i = 0; i < 20 && recorder.FrameCount < 3; i++)
        {
            System.Threading.Thread.Sleep(25);
        }

        Assert.Equal(3, recorder.FrameCount);

        var range = recorder.GetFramesRange(1050, 1250, "AGV-99");
        Assert.Equal(2, range.Count);
        Assert.Equal(1100, range[0].TimestampMs);
        Assert.Equal(1200, range[1].TimestampMs);
    }

    [Fact]
    public void AuditLogService_Should_Filter_And_Paginate()
    {
        var audit = new AuditLogService();
        audit.Log("admin", "LOGIN", "Auth", "Success");
        audit.Log("operator", "ESTOP", "Vehicle-1", "Emergency stop triggered");
        audit.Log("admin", "MAP_EDIT", "Node-1", "Position changed");

        var (items, total) = audit.Query(action: "ESTOP");
        Assert.Equal(1, total);
        Assert.Equal("ESTOP", items[0].Action);
        Assert.Equal("operator", items[0].Operator);
    }

    // ==========================================
    // 2. 权限与安全互锁模块测试 (RBAC & Safety Interlock)
    // ==========================================
    [Fact]
    public void IdentityService_Should_Verify_Role_Permissions()
    {
        Assert.True(FmsPermissionCatalog.RoleHas("Admin", FmsPermissions.MapEdit));
        Assert.True(FmsPermissionCatalog.RoleHas("Admin", FmsPermissions.FleetTeleop));

        Assert.True(FmsPermissionCatalog.RoleHas("Engineer", FmsPermissions.MapEdit));
        Assert.True(FmsPermissionCatalog.RoleHas("Engineer", FmsPermissions.FleetEstop));
        Assert.False(FmsPermissionCatalog.RoleHas("Engineer", FmsPermissions.SystemUsers));

        Assert.False(FmsPermissionCatalog.RoleHas("Operator", FmsPermissions.MapEdit));
        Assert.False(FmsPermissionCatalog.RoleHas("Operator", FmsPermissions.FleetEstop));
        Assert.True(FmsPermissionCatalog.RoleHas("Operator", FmsPermissions.OrdersCreate));

        Assert.False(FmsPermissionCatalog.RoleHas("Viewer", FmsPermissions.MapEdit));
        Assert.True(FmsPermissionCatalog.RoleHas("Viewer", FmsPermissions.Dashboard));
        Assert.False(FmsPermissionCatalog.RoleHas("Viewer", FmsPermissions.OrdersCreate));
    }

    [Fact]
    public void SafetyInterlock_Should_Block_Map_Mutation_When_Vehicle_Running()
    {
        var identity = new IdentityService();
        var engine = new DispatchEngine();

        var v = new VehicleAggregate("AGV-1", "AGV-1", new Pose2D(0, 0, 0));
        engine.RegisterVehicle(v);

        // Idle state: safe to modify map
        v.Status = VehicleStatus.Idle;
        var (safeWhenIdle, _) = identity.CheckSafetyInterlock("MAP_MODIFY", engine);
        Assert.True(safeWhenIdle);

        // Running state: blocked by safety interlock!
        v.Status = VehicleStatus.Running;
        var (safeWhenRunning, error) = identity.CheckSafetyInterlock("MAP_MODIFY", engine);
        Assert.False(safeWhenRunning);
        Assert.Contains("严禁在线修改拓扑结构", error);
    }

    // ==========================================
    // 3. 配置模块测试 (Configuration & Validation)
    // ==========================================
    [Fact]
    public void TrafficConfig_Should_Enforce_Safety_Bounds()
    {
        var config = new FmsTrafficConfig();

        // Valid
        config.MaxSpeedLimit = 2.5;
        config.DeadlockTimeoutSec = 30;
        var (valid, _) = config.Validate();
        Assert.True(valid);

        // Invalid: speed > 5.0 m/s
        config.MaxSpeedLimit = 10.0;
        var (invalidSpeed, speedErr) = config.Validate();
        Assert.False(invalidSpeed);
        Assert.Contains("限速上限必须在 0.1 ~ 5.0", speedErr);

        // Invalid: deadlock timeout < 1.0s
        config.MaxSpeedLimit = 2.0;
        config.DeadlockTimeoutSec = 0.5;
        var (invalidTimeout, timeoutErr) = config.Validate();
        Assert.False(invalidTimeout);
        Assert.Contains("死锁超时阈值", timeoutErr);
    }

    // ==========================================
    // 4. 地图拓扑与互斥锁区模块测试 (Topology & Mutex Zone)
    // ==========================================
    [Fact]
    public void MutexZone_RayCasting_And_Claim_Timeout_Should_Work()
    {
        var zone = new MutexZone
        {
            Id = "Z1",
            Name = "Intersection",
            MaxCapacity = 1,
            TimeoutSec = 0.2, // 200ms for test
            Polygon = new List<Point2D>
            {
                new(0, 0), new(10, 0), new(10, 10), new(0, 10)
            }
        };

        // Point inside test
        Assert.True(zone.ContainsPoint(new Point2D(5, 5)));
        Assert.False(zone.ContainsPoint(new Point2D(15, 15)));

        // Claim test
        Assert.True(zone.TryClaim("AGV-1"));
        Assert.True(zone.IsOccupied);
        Assert.False(zone.TryClaim("AGV-2")); // capacity full

        // Wait for timeout alarm
        System.Threading.Thread.Sleep(250);
        Assert.False(zone.TryClaim("AGV-2")); // AGV-2 cannot preempt timed-out AGV-1, physical lock preserved
        Assert.True(zone.HasTimeoutAlarm, "Should have triggered safety timeout alarm");

        // Safe release by AGV-1 allows AGV-2 to claim
        zone.Release("AGV-1");
        Assert.True(zone.TryClaim("AGV-2"));
        Assert.Contains("AGV-2", zone.ActiveClaimantVehicleIds);
    }

    [Fact]
    public void CubicBezier_Curvature_Calculation_Should_Be_Positive_On_Curved_Path()
    {
        var p0 = new Point2D(0, 0);
        var p1 = new Point2D(5, 10);
        var p2 = new Point2D(15, 10);
        var p3 = new Point2D(20, 0);

        double kMid = BezierMath.ComputeCurvature(p0, p1, p2, p3, 0.5);
        Assert.True(kMid > 0.0, "Curvature at apex should be positive");
    }
}
