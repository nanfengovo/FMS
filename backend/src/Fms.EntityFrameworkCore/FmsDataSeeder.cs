using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fms.Domain.Identity;
using Fms.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fms.EntityFrameworkCore;

public static class FmsSecurityHelper
{
    public static (string hash, string salt) HashPassword(string password)
    {
        byte[] saltBytes = RandomNumberGenerator.GetBytes(16);
        string salt = Convert.ToBase64String(saltBytes);

        byte[] hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            saltBytes,
            100000,
            HashAlgorithmName.SHA256,
            32
        );
        string hash = Convert.ToBase64String(hashBytes);

        return (hash, salt);
    }

    public static bool VerifyPassword(string password, string hash, string salt)
    {
        try
        {
            byte[] saltBytes = Convert.FromBase64String(salt);
            byte[] hashBytes = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                saltBytes,
                100000,
                HashAlgorithmName.SHA256,
                32
            );
            string computedHash = Convert.ToBase64String(hashBytes);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedHash),
                Encoding.UTF8.GetBytes(hash)
            );
        }
        catch
        {
            return false;
        }
    }
}

public class FmsDataSeeder
{
    public async Task SeedAsync(FmsDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        await EnsureColumnsExistAsync(db);

        // 1. Seed Users
        if (!await db.Users.AnyAsync())
        {
            var (adminHash, adminSalt) = FmsSecurityHelper.HashPassword("admin123");
            var (operatorHash, operatorSalt) = FmsSecurityHelper.HashPassword("operator123");
            var (safetyHash, safetySalt) = FmsSecurityHelper.HashPassword("safety123");
            var (viewerHash, viewerSalt) = FmsSecurityHelper.HashPassword("viewer123");

            db.Users.AddRange(
                new UserAccountRecord
                {
                    Id = "usr-01",
                    Username = "admin",
                    PasswordHash = adminHash,
                    Salt = adminSalt,
                    RolesJson = JsonSerializer.Serialize(new[] { "Admin" }),
                    IsActive = true
                },
                new UserAccountRecord
                {
                    Id = "usr-02",
                    Username = "operator",
                    PasswordHash = operatorHash,
                    Salt = operatorSalt,
                    RolesJson = JsonSerializer.Serialize(new[] { "Operator" }),
                    IsActive = true
                },
                new UserAccountRecord
                {
                    Id = "usr-03",
                    Username = "safety",
                    PasswordHash = safetyHash,
                    Salt = safetySalt,
                    RolesJson = JsonSerializer.Serialize(new[] { "Engineer" }),
                    IsActive = true
                },
                new UserAccountRecord
                {
                    Id = "usr-04",
                    Username = "viewer",
                    PasswordHash = viewerHash,
                    Salt = viewerSalt,
                    RolesJson = JsonSerializer.Serialize(new[] { "Viewer" }),
                    IsActive = true
                }
            );
            await db.SaveChangesAsync();
        }

        await NormalizeUserRolesAsync(db);
        await SeedRoleGrantsAsync(db);
        await SeedLogCenterOnceAsync(db);

        // 2. Seed Map Nodes
        if (!await db.MapNodes.AnyAsync())
        {
            var nodes = new List<MapNodeRecord>
            {
                new() { Id = "STK-01", Name = "Stocker-01 晶圆出库立库", X = 5.0, Y = 5.0, NodeType = "Pick", HeadingDeg = 0 },
                new() { Id = "STK-02", Name = "Stocker-02 载具入库立库", X = 5.0, Y = 25.0, NodeType = "Drop", HeadingDeg = 0 },
                new() { Id = "WB-01", Name = "WireBonder-01 键合机台", X = 45.0, Y = 5.0, NodeType = "Drop", HeadingDeg = 180 },
                new() { Id = "WB-02", Name = "WireBonder-02 键合机台", X = 45.0, Y = 25.0, NodeType = "Drop", HeadingDeg = 180 },
                new() { Id = "SPUR-01", Name = "主干道避让站 01 (Spur-1)", X = 25.0, Y = 2.0, NodeType = "PullOver", HeadingDeg = 270 },
                new() { Id = "SPUR-02", Name = "主干道避让站 02 (Spur-2)", X = 25.0, Y = 28.0, NodeType = "PullOver", HeadingDeg = 90 },
                new() { Id = "PARK-01", Name = "待命停靠区 01 (Park-1)", X = 15.0, Y = 5.0, NodeType = "Park", HeadingDeg = 0 },
                new() { Id = "PARK-02", Name = "待命停靠区 02 (Park-2)", X = 35.0, Y = 25.0, NodeType = "Park", HeadingDeg = 180 },
                new() { Id = "CH-01", Name = "大功率智能充电机 01", X = 25.0, Y = 20.0, NodeType = "Charge", HeadingDeg = 90 },
                new() { Id = "JUNCT-L", Name = "左分流道岔路口", X = 15.0, Y = 15.0, NodeType = "Normal", HeadingDeg = 0 },
                new() { Id = "JUNCT-C", Name = "核心十字交叉路口", X = 25.0, Y = 15.0, NodeType = "Normal", HeadingDeg = 0 },
                new() { Id = "JUNCT-R", Name = "右汇流道岔路口", X = 35.0, Y = 15.0, NodeType = "Normal", HeadingDeg = 0 }
            };
            db.MapNodes.AddRange(nodes);
            await db.SaveChangesAsync();
        }

        // 3. Seed Map Paths
        if (!await db.MapPaths.AnyAsync())
        {
            var paths = new List<MapPathRecord>
            {
                new()
                {
                    Id = "P-STK1-L",
                    StartNodeId = "STK-01",
                    EndNodeId = "JUNCT-L",
                    PathType = "Straight",
                    ControlPointsJson = "[]",
                    MaxSpeed = 1.8,
                    Length = 14.14,
                    IsBidirectional = true
                },
                new()
                {
                    Id = "P-STK2-L",
                    StartNodeId = "STK-02",
                    EndNodeId = "JUNCT-L",
                    PathType = "Straight",
                    ControlPointsJson = "[]",
                    MaxSpeed = 1.8,
                    Length = 14.14,
                    IsBidirectional = true
                },
                new()
                {
                    Id = "P-PARK1-L",
                    StartNodeId = "PARK-01",
                    EndNodeId = "JUNCT-L",
                    PathType = "Straight",
                    ControlPointsJson = "[]",
                    MaxSpeed = 1.5,
                    Length = 10.0,
                    IsBidirectional = true
                },
                new()
                {
                    Id = "P-PARK2-R",
                    StartNodeId = "PARK-02",
                    EndNodeId = "JUNCT-R",
                    PathType = "Straight",
                    ControlPointsJson = "[]",
                    MaxSpeed = 1.5,
                    Length = 10.0,
                    IsBidirectional = true
                },
                new()
                {
                    Id = "P-CH1-C",
                    StartNodeId = "CH-01",
                    EndNodeId = "JUNCT-C",
                    PathType = "Straight",
                    ControlPointsJson = "[]",
                    MaxSpeed = 1.2,
                    Length = 5.0,
                    IsBidirectional = true
                },
                new()
                {
                    Id = "P-MAIN-1",
                    StartNodeId = "JUNCT-L",
                    EndNodeId = "JUNCT-C",
                    PathType = "Straight",
                    ControlPointsJson = "[]",
                    MaxSpeed = 1.8,
                    Length = 10.0,
                    IsBidirectional = true
                },
                new()
                {
                    Id = "P-MAIN-2",
                    StartNodeId = "JUNCT-C",
                    EndNodeId = "JUNCT-R",
                    PathType = "Straight",
                    ControlPointsJson = "[]",
                    MaxSpeed = 1.8,
                    Length = 10.0,
                    IsBidirectional = true
                },
                new()
                {
                    Id = "P-SPUR-1",
                    StartNodeId = "JUNCT-C",
                    EndNodeId = "SPUR-01",
                    PathType = "CubicBezier",
                    ControlPointsJson = JsonSerializer.Serialize(new[] { new { x = 25.0, y = 8.0 }, new { x = 25.0, y = 4.0 } }),
                    MaxSpeed = 1.2,
                    Length = 13.0,
                    IsBidirectional = true
                },
                new()
                {
                    Id = "P-SPUR-2",
                    StartNodeId = "JUNCT-C",
                    EndNodeId = "SPUR-02",
                    PathType = "CubicBezier",
                    ControlPointsJson = JsonSerializer.Serialize(new[] { new { x = 25.0, y = 22.0 }, new { x = 25.0, y = 26.0 } }),
                    MaxSpeed = 1.2,
                    Length = 13.0,
                    IsBidirectional = true
                },
                new()
                {
                    Id = "P-R-WB1",
                    StartNodeId = "JUNCT-R",
                    EndNodeId = "WB-01",
                    PathType = "Straight",
                    ControlPointsJson = "[]",
                    MaxSpeed = 1.8,
                    Length = 14.14,
                    IsBidirectional = true
                },
                new()
                {
                    Id = "P-R-WB2",
                    StartNodeId = "JUNCT-R",
                    EndNodeId = "WB-02",
                    PathType = "Straight",
                    ControlPointsJson = "[]",
                    MaxSpeed = 1.8,
                    Length = 14.14,
                    IsBidirectional = true
                }
            };
            db.MapPaths.AddRange(paths);
            await db.SaveChangesAsync();
        }

        // 4. Seed Mutex Zones
        if (!await db.MutexZones.AnyAsync())
        {
            var zones = new List<MutexZoneRecord>
            {
                new()
                {
                    Id = "ZONE-JUNCT-C",
                    Name = "核心十字交叉防冲锁区 (JUNCT-C)",
                    MaxCapacity = 1,
                    TimeoutSec = 30.0,
                    PolygonJson = JsonSerializer.Serialize(new[]
                    {
                        new { x = 20.0, y = 10.0 },
                        new { x = 30.0, y = 10.0 },
                        new { x = 30.0, y = 20.0 },
                        new { x = 20.0, y = 20.0 }
                    }),
                    MemberNodeIdsJson = JsonSerializer.Serialize(new[] { "JUNCT-C" }),
                    MemberPathIdsJson = JsonSerializer.Serialize(new[] { "P-MAIN-1", "P-MAIN-2", "P-SPUR-1", "P-SPUR-2", "P-CH1-C" })
                }
            };
            db.MutexZones.AddRange(zones);
            await db.SaveChangesAsync();
        }

        // 5. Seed Vehicles
        if (!await db.Vehicles.AnyAsync())
        {
            var vehicles = new List<VehicleRecord>
            {
                new()
                {
                    Id = "1",
                    Name = "AMR-101 (潜伏顶升)",
                    Code = "AMR-101",
                    VehicleType = "LATENT_LIFT_AMR",
                    Group = "CLEANROOM_01",
                    InitialX = 5.0,
                    InitialY = 5.0,
                    InitialTheta = 0.0,
                    MaxSpeed = 1.8,
                    BatteryPercent = 92.0,
                    BatteryVoltage = 51.2,
                    Status = "Idle",
                    IsOnline = true
                },
                new()
                {
                    Id = "2",
                    Name = "AMR-102 (重载辊筒)",
                    Code = "AMR-102",
                    VehicleType = "ROLLER_TRANSFER_AMR",
                    Group = "LINE_BUFFER",
                    InitialX = 45.0,
                    InitialY = 5.0,
                    InitialTheta = 3.14159,
                    MaxSpeed = 1.5,
                    BatteryPercent = 78.5,
                    BatteryVoltage = 49.8,
                    Status = "Idle",
                    IsOnline = true
                },
                new()
                {
                    Id = "3",
                    Name = "AMR-103 (低电巡检)",
                    Code = "AMR-103",
                    VehicleType = "INSPECTION_AMR",
                    Group = "PATROL",
                    InitialX = 25.0,
                    InitialY = 28.0,
                    InitialTheta = 0.0,
                    MaxSpeed = 1.2,
                    BatteryPercent = 18.5, // 处于低电状态，方便验证自主电量规划与回充
                    BatteryVoltage = 44.1,
                    Status = "Idle",
                    IsOnline = true
                }
            };
            db.Vehicles.AddRange(vehicles);
            await db.SaveChangesAsync();
        }

        // 6. Seed Realistic Orders & 12 Detailed Steps (Matching User Images 1-4)
        if (!await db.Orders.AnyAsync())
        {
            var order1 = new TransportOrderRecord
            {
                Id = "ord-01",
                OrderSerial = "20261002145016524021_Fetch1",
                TaskType = "Transport",
                Priority = 80,
                AssignedVehicleId = "1",
                Status = "Running",
                SourceNodeId = "STK-01",
                TargetNodeId = "WB-01",
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                StartedAt = DateTime.UtcNow.AddMinutes(-8),
                MovementTimeSec = 830,
                WaitTimeSec = 0,
                MileageKm = 0.055362,
                SubNo = "20261002145016524021_Fetch1",
                TaskCodeA = "133121",
                TaskCodeB = "133635",
                FinishingAction = "Parking or charging",
                CargoSerial = "1",
                ArmAction = 1,
                Rfid = "#",
                Mark = 3,
                OptionCode = "131073,133635",
                PassPoint = "1",
                PassPointToGoal = "25",
                RobotType = 99,
                Storage = "69",
                Target = "69"
            };

            // 12 Fine-grained industrial steps matching the exact field controller screenshot
            order1.Steps = new List<TaskStepRecord>
            {
                new() { OrderId = order1.Id, StepNo = 1, Name = "UPDATA_RFID_TO_AGV", Type = "MODIFY_VEHICLE_PARAM", Status = "Finish", Parameter = "value:RFID vehicle_param:RFID", FinishedAt = DateTime.UtcNow.AddMinutes(-7) },
                new() { OrderId = order1.Id, StepNo = 2, Name = "UPDATA_TaskCodea_TO_AGV", Type = "MODIFY_VEHICLE_PARAM", Status = "Finish", Parameter = "value:TaskCodea vehicle_param:TaskCodea", FinishedAt = DateTime.UtcNow.AddMinutes(-7) },
                new() { OrderId = order1.Id, StepNo = 3, Name = "UPDATA_TaskCodeb_TO_AGV", Type = "MODIFY_VEHICLE_PARAM", Status = "Finish", Parameter = "value:TaskCodeb vehicle_param:TaskCodeb", FinishedAt = DateTime.UtcNow.AddMinutes(-6) },
                new() { OrderId = order1.Id, StepNo = 4, Name = "REPORT_TASK_BEGIN", Type = "Custom_Step", Status = "Finish", Parameter = "AbortFunc: FinishFunc: GetChangeParamFunc:ReportTaskStart PauseFunc: ResumeFunc: RunningFunc: StartFunc", FinishedAt = DateTime.UtcNow.AddMinutes(-6) },
                new() { OrderId = order1.Id, StepNo = 5, Name = "TASK_BEGIN", Type = "SET_VAR", Status = "Finish", Parameter = "datatype:UINT name:ARVTask value:1", FinishedAt = DateTime.UtcNow.AddMinutes(-5) },
                new() { OrderId = order1.Id, StepNo = 6, Name = "MOVE_TO_POINT_target", Type = "MOVE", Status = "Ongoing to Run", Parameter = "goal_id:target pass_point:pass_point pass_point_to_goal:pass_point_to_goal" },
                new() { OrderId = order1.Id, StepNo = 7, Name = "REPORT_TASK_ARRIVE", Type = "Custom_Step", Status = "Waiting to Run", Parameter = "GetChangeParamFunc:ReportTaskArrive" },
                new() { OrderId = order1.Id, StepNo = 8, Name = "REPORT_PERMISS_ACTION", Type = "Custom_Step", Status = "Waiting to Run", Parameter = "GetChangeParamFunc:PermissAction" },
                new() { OrderId = order1.Id, StepNo = 9, Name = "ARMACTION_BEGIN", Type = "SET_VAR", Status = "Waiting to Run", Parameter = "datatype:UINT name:ARMAction value:1" },
                new() { OrderId = order1.Id, StepNo = 10, Name = "CHECK_ARMACTIONOK", Type = "CHECK_VAR", Status = "Waiting to Run", Parameter = "datatype:UINT expect_value:1 name:ARMActionOK" },
                new() { OrderId = order1.Id, StepNo = 11, Name = "REPORT_FINISH", Type = "Custom_Step", Status = "Waiting to Run", Parameter = "GetChangeParamFunc:ReportFinish" },
                new() { OrderId = order1.Id, StepNo = 12, Name = "TASK_FINISH", Type = "SET_VAR", Status = "Waiting to Run", Parameter = "datatype:UINT name:ARVTask value:0" }
            };

            var order2 = new TransportOrderRecord
            {
                Id = "ord-02",
                OrderSerial = "2026100214502624021_Put1",
                TaskType = "Transport",
                Priority = 60,
                AssignedVehicleId = "2",
                Status = "Queued",
                SourceNodeId = "WB-02",
                TargetNodeId = "PARK-02",
                CreatedAt = DateTime.UtcNow.AddMinutes(-3),
                MovementTimeSec = 16,
                WaitTimeSec = 0,
                MileageKm = 0.001215,
                SubNo = "2026100214502624021_Put1",
                TaskCodeA = "133122",
                TaskCodeB = "133636",
                FinishingAction = "Parking or charging",
                CargoSerial = "2",
                ArmAction = 2,
                Rfid = "TAG-8820",
                Mark = 3,
                OptionCode = "131073,133636",
                PassPoint = "1",
                PassPointToGoal = "25",
                RobotType = 99,
                Storage = "70",
                Target = "70"
            };

            order2.Steps = new List<TaskStepRecord>
            {
                new() { OrderId = order2.Id, StepNo = 1, Name = "UPDATA_RFID_TO_AGV", Type = "MODIFY_VEHICLE_PARAM", Status = "Waiting to Run", Parameter = "value:RFID vehicle_param:RFID" },
                new() { OrderId = order2.Id, StepNo = 2, Name = "UPDATA_TaskCodea_TO_AGV", Type = "MODIFY_VEHICLE_PARAM", Status = "Waiting to Run", Parameter = "value:TaskCodea vehicle_param:TaskCodea" },
                new() { OrderId = order2.Id, StepNo = 3, Name = "MOVE_TO_POINT_target", Type = "MOVE", Status = "Waiting to Run", Parameter = "goal_id:PARK-02" },
                new() { OrderId = order2.Id, StepNo = 4, Name = "TASK_FINISH", Type = "SET_VAR", Status = "Waiting to Run", Parameter = "datatype:UINT name:ARVTask value:0" }
            };

            db.Orders.AddRange(order1, order2);
            await db.SaveChangesAsync();
        }

        // 7. Seed Audit Logs
        if (!await db.AuditLogs.AnyAsync())
        {
            var auditLogs = new List<AuditLogRecord>
            {
                new()
                {
                    Timestamp = DateTime.UtcNow.AddMinutes(-12),
                    Operator = "SYS_DAEMON",
                    ActionType = "SECURITY",
                    EntityType = "INTERLOCK_SYSTEM",
                    EntityId = "INTERLOCK_SYSTEM",
                    Details = "工业安全联锁状态自检通过: 拓扑变更防呆、急停回路及 4030 阻断已就绪",
                    Success = true
                },
                new()
                {
                    Timestamp = DateTime.UtcNow.AddMinutes(-10),
                    Operator = "VDA_BRIDGE",
                    ActionType = "NETWORK",
                    EntityType = "VDA5050_GATEWAY",
                    EntityId = "VDA5050_GATEWAY",
                    Details = "VDA 5050 & 科聪 MRC 驱动链路握手正常，10Hz 实时遥测管道通畅",
                    Success = true
                },
                new()
                {
                    Timestamp = DateTime.UtcNow.AddMinutes(-8),
                    Operator = "Engineer_Zhang",
                    ActionType = "MAP_MODIFY",
                    EntityType = "TOPOLOGY_GRAPH",
                    EntityId = "TOPOLOGY_GRAPH",
                    Details = "加载并标定高阶三次贝塞尔拓扑航道: 已就绪 10 个工位节点与 9 条引导路径",
                    Success = true
                },
                new()
                {
                    Timestamp = DateTime.UtcNow.AddMinutes(-6),
                    Operator = "SAFETY_AGENT",
                    ActionType = "FORCE_RELEASE_MUTEX",
                    EntityType = "ZONE-JUNCT-C",
                    EntityId = "ZONE-JUNCT-C",
                    Details = "注册核心十字交叉路口防冲区 (ZONE-JUNCT-C)，Ray-Casting 碰撞多边形算法就绪",
                    Success = true
                },
                new()
                {
                    Timestamp = DateTime.UtcNow.AddMinutes(-4),
                    Operator = "Admin",
                    ActionType = "CONFIG_UPDATE",
                    EntityType = "TRAFFIC_KINEMATICS",
                    EntityId = "TRAFFIC_KINEMATICS",
                    Details = "下发标准综合工况动力学包: 最大直线速度 1.8 m/s, 死锁仲裁超时 20.0 s",
                    Success = true
                },
                new()
                {
                    Timestamp = DateTime.UtcNow.AddMinutes(-2),
                    Operator = "Operator_Li",
                    ActionType = "DISPATCH_TASK",
                    EntityType = "TASK-AUTO-INIT",
                    EntityId = "TASK-AUTO-INIT",
                    Details = "调度系统初始化自检: 车队 AMR-01 与 AMR-02 处于待命就绪状态，AMR-03 处于低电待充",
                    Success = true
                }
            };
            db.AuditLogs.AddRange(auditLogs);
            await db.SaveChangesAsync();
        }
    }

    private static async Task NormalizeUserRolesAsync(FmsDbContext db)
    {
        var users = await db.Users.ToListAsync();
        foreach (var user in users)
        {
            List<string> parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<List<string>>(user.RolesJson) ?? new List<string>();
            }
            catch
            {
                parsed = new List<string>();
            }

            var roles = parsed
                .Select(FmsPermissionCatalog.NormalizeRole)
                .Where(r => FmsPermissionCatalog.RoleNames.Contains(r, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (user.Username.Equals("admin", StringComparison.OrdinalIgnoreCase))
                roles = ["Admin"];
            else if (user.Username.Equals("safety", StringComparison.OrdinalIgnoreCase))
                roles = ["Engineer"];
            else if (user.Username.Equals("operator", StringComparison.OrdinalIgnoreCase))
                roles = ["Operator"];
            else if (user.Username.Equals("viewer", StringComparison.OrdinalIgnoreCase))
                roles = ["Viewer"];

            if (roles.Count == 0) roles.Add("Viewer");
            user.RolesJson = JsonSerializer.Serialize(roles);
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedRoleGrantsAsync(FmsDbContext db)
    {
        var existing = await db.RoleGrants.ToListAsync();
        foreach (var role in FmsPermissionCatalog.RoleNames)
        {
            if (existing.Any(g => g.RoleName.Equals(role, StringComparison.OrdinalIgnoreCase)))
                continue;

            foreach (var permission in FmsPermissionCatalog.GrantsFor(role))
            {
                db.RoleGrants.Add(new RoleGrantRecord { RoleName = role, PermissionName = permission });
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedLogCenterOnceAsync(FmsDbContext db)
    {
        if (await db.AppStates.AnyAsync(s => s.Key == "log-center-grants-v1")) return;

        var existing = await db.RoleGrants.ToListAsync();
        foreach (var role in FmsPermissionCatalog.RoleNames)
        {
            foreach (var permission in FmsPermissionCatalog.GrantsFor(role).Where(p => p.StartsWith("Fms.Logs", StringComparison.Ordinal)))
            {
                if (existing.Any(g => g.RoleName.Equals(role, StringComparison.OrdinalIgnoreCase) && g.PermissionName == permission))
                    continue;
                db.RoleGrants.Add(new RoleGrantRecord { RoleName = role, PermissionName = permission });
            }
        }

        if (!await db.LogWiki.AnyAsync())
        {
            db.LogWiki.Add(new LogWikiRecord
            {
                Id = "log-guide",
                Title = "日志分类说明",
                Body = """
                    用户操作日志记录登录后的人工动作，例如改图、派任务、急停。
                    入站日志是外部系统调用本调度的接口，例如新松 RCS 的 task_add。
                    出站日志是本调度回调外部系统，例如任务开始、到达、完成。车辆状态周期上报只在失败时记录。
                    接口日志是本系统 /api 请求的方法、路径、状态码和耗时。
                    系统运行日志来自调度进程打印的文本，同时保留结构化字段。
                    AI 分析可以用 RAG 检索日志和 Wiki，也可以把问题转成只读 SQL。接口密钥按 OpenAPI securitySchemes 决定放在 Bearer、请求头或查询参数。
                    """,
                UpdatedAt = DateTime.UtcNow
            });
        }

        db.AppStates.Add(new AppStateRecord { Key = "log-center-grants-v1", Value = "1" });
        await db.SaveChangesAsync();
    }

    private static async Task EnsureColumnsExistAsync(FmsDbContext db)
    {
        var alterStatements = new[]
        {
            "CREATE TABLE IF NOT EXISTS Fms_WorkflowTemplates (Id TEXT PRIMARY KEY, Name TEXT, Description TEXT, IsBuiltIn INTEGER DEFAULT 0, Version INTEGER DEFAULT 1, VersionTag TEXT DEFAULT 'v1.0.0', ChangeLog TEXT DEFAULT '初始创建', StepsJson TEXT, CreatedAt TEXT, UpdatedAt TEXT);",
            "CREATE TABLE IF NOT EXISTS Fms_WorkflowTemplateHistories (Id TEXT PRIMARY KEY, TemplateId TEXT, Version INTEGER, VersionTag TEXT, ChangeLog TEXT, StepsJson TEXT, CreatedAt TEXT);",
            "CREATE TABLE IF NOT EXISTS Fms_MapPointCloud (Id TEXT PRIMARY KEY, TotalCount INTEGER DEFAULT 0, Data BLOB, UpdatedAt TEXT);",
            "CREATE TABLE IF NOT EXISTS Fms_RefreshTokens (Token TEXT PRIMARY KEY, UserId TEXT, Username TEXT, RolesJson TEXT, ExpiryUtc TEXT, CreatedAt TEXT);",
            "ALTER TABLE Fms_MapNodes ADD COLUMN AllowRevolve INTEGER DEFAULT 1;",
            "ALTER TABLE Fms_MapNodes ADD COLUMN SpinDirection TEXT DEFAULT 'both';",
            "ALTER TABLE Fms_MapNodes ADD COLUMN ClassName TEXT DEFAULT 'LandMark';",
            "ALTER TABLE Fms_MapPaths ADD COLUMN Direction TEXT DEFAULT 'forward';",
            "ALTER TABLE Fms_MapPaths ADD COLUMN RoadWidth REAL DEFAULT 1.2;",
            "ALTER TABLE Fms_MapPaths ADD COLUMN CurveType TEXT DEFAULT 'straight_line';",
            "ALTER TABLE Fms_Orders ADD COLUMN OrderStepsJson TEXT DEFAULT '[]';",
            "ALTER TABLE Fms_Orders ADD COLUMN CurrentStepIndex INTEGER DEFAULT 0;",
            "ALTER TABLE Fms_Orders ADD COLUMN BulkTaskType TEXT DEFAULT 'task';",
            "ALTER TABLE Fms_WorkflowTemplates ADD COLUMN VersionTag TEXT DEFAULT 'v1.0.0';",
            "ALTER TABLE Fms_WorkflowTemplates ADD COLUMN ChangeLog TEXT DEFAULT '初始创建';",
            "ALTER TABLE Fms_MapPointCloud ADD COLUMN RawTotalCount INTEGER DEFAULT 0;",
            "ALTER TABLE Fms_MapPointCloud ADD COLUMN DownsampleFactor INTEGER DEFAULT 25;",
            "CREATE TABLE IF NOT EXISTS Fms_StorageZones (Id TEXT PRIMARY KEY, Name TEXT, Type TEXT, Capacity INTEGER DEFAULT 10, AllowInbound INTEGER DEFAULT 1, AllowOutbound INTEGER DEFAULT 1, PolygonJson TEXT, UpdatedAt TEXT);",
            "CREATE TABLE IF NOT EXISTS Fms_StorageSlots (Id TEXT PRIMARY KEY, Name TEXT, ZoneId TEXT, ZoneType TEXT, BindNodeId TEXT, Status TEXT, OccupiedCarrierId TEXT, ReservedOrderId TEXT, ReservedVehicleId TEXT, LockReason TEXT, UpdatedAt TEXT);",
            "CREATE TABLE IF NOT EXISTS Fms_RouteLines (Id TEXT PRIMARY KEY, Name TEXT, MemberPathIdsJson TEXT, AllowedVehicleTypesJson TEXT, DefaultDirection TEXT, AllowBackward INTEGER DEFAULT 0, SpeedLimit REAL DEFAULT 1.5, IsClosed INTEGER DEFAULT 0, ClosureReason TEXT);",
            "CREATE TABLE IF NOT EXISTS Fms_RoleGrants (RoleName TEXT NOT NULL, PermissionName TEXT NOT NULL, PRIMARY KEY (RoleName, PermissionName));",
            "CREATE TABLE IF NOT EXISTS Fms_LogEvents (Id TEXT PRIMARY KEY, Timestamp TEXT, Category TEXT, Level TEXT, Source TEXT, Actor TEXT, Message TEXT, PayloadJson TEXT, TraceId TEXT, Method TEXT, Path TEXT, StatusCode INTEGER, DurationMs INTEGER, Success INTEGER);",
            "CREATE TABLE IF NOT EXISTS Fms_ReplayScenes (Id TEXT PRIMARY KEY, Name TEXT, MapName TEXT, TraceId TEXT, VehicleId TEXT, Status TEXT, StartSimMs INTEGER, EndSimMs INTEGER, SampleCount INTEGER, CreatedAt TEXT, FramesJson TEXT, LogsJson TEXT);",
            "CREATE TABLE IF NOT EXISTS Fms_DecisionLogs (Id INTEGER PRIMARY KEY AUTOINCREMENT, TimestampUtc TEXT, Category TEXT, SubjectId TEXT, Action TEXT, Outcome TEXT, Reason TEXT, ContextJson TEXT);",
            "CREATE TABLE IF NOT EXISTS Fms_LogWiki (Id TEXT PRIMARY KEY, Title TEXT, Body TEXT, UpdatedAt TEXT);",
            "CREATE TABLE IF NOT EXISTS Fms_AiEndpoints (Id TEXT PRIMARY KEY, BaseUrl TEXT, Model TEXT, EmbeddingModel TEXT, ApiKey TEXT, SchemeName TEXT, HeaderName TEXT, QueryName TEXT, OpenApiJson TEXT, UpdatedAt TEXT);",
            "CREATE TABLE IF NOT EXISTS Fms_AppState (Key TEXT PRIMARY KEY, Value TEXT);",
            "ALTER TABLE Fms_Vehicles ADD COLUMN IsEnabled INTEGER DEFAULT 1;",
            "ALTER TABLE Fms_Vehicles ADD COLUMN Length REAL DEFAULT 1.2;",
            "ALTER TABLE Fms_Vehicles ADD COLUMN Width REAL DEFAULT 0.8;",
            "ALTER TABLE Fms_Vehicles ADD COLUMN DriveType TEXT DEFAULT 'Differential';",
            "ALTER TABLE Fms_Vehicles ADD COLUMN MapId TEXT;",
            "ALTER TABLE Fms_Vehicles ADD COLUMN MapName TEXT;",
            "ALTER TABLE Fms_Vehicles ADD COLUMN HomeNodeId TEXT;",
            "ALTER TABLE Fms_Vehicles ADD COLUMN CurrentNodeId TEXT;",
            "ALTER TABLE Fms_Vehicles ADD COLUMN LastHeartbeatUtc TEXT;",
            "UPDATE Fms_Vehicles SET MapId = COALESCE(MapId, '') WHERE MapId IS NULL;",
            "UPDATE Fms_Vehicles SET MapName = COALESCE(MapName, '') WHERE MapName IS NULL;",
            "UPDATE Fms_Vehicles SET DriveType = COALESCE(DriveType, 'Differential') WHERE DriveType IS NULL;"
        };

        foreach (var sql in alterStatements)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(sql);
            }
            catch
            {
                // 忽略已存在列或表的报错
            }
        }
    }
}
