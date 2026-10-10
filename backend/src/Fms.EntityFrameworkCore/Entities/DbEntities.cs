using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fms.EntityFrameworkCore.Entities;

[Table("Fms_Vehicles")]
public class VehicleRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(64)]
    public string VehicleType { get; set; } = "LATENT_AMR";

    [MaxLength(64)]
    public string Group { get; set; } = "ALL";

    public double InitialX { get; set; }
    public double InitialY { get; set; }
    public double InitialTheta { get; set; }

    public double MaxSpeed { get; set; } = 1.8;
    public double Length { get; set; } = 1.2;
    public double Width { get; set; } = 0.8;
    public double BatteryPercent { get; set; } = 100.0;
    public double BatteryVoltage { get; set; } = 48.0;

    [MaxLength(32)]
    public string Status { get; set; } = "Idle";

    [MaxLength(32)]
    public string? DriveType { get; set; } = "Differential";

    public bool IsOnline { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public bool IsEstop { get; set; } = false;

    [MaxLength(128)]
    public string? MapId { get; set; }

    [MaxLength(128)]
    public string? MapName { get; set; }

    [MaxLength(64)]
    public string? HomeNodeId { get; set; }

    [MaxLength(64)]
    public string? CurrentNodeId { get; set; }

    public DateTime? LastHeartbeatUtc { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("Fms_MapNodes")]
public class MapNodeRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    public double X { get; set; }
    public double Y { get; set; }

    [MaxLength(32)]
    public string NodeType { get; set; } = "Normal";

    public double HeadingDeg { get; set; } = 0.0;
    public bool AllowRevolve { get; set; } = true;
    public string SpinDirection { get; set; } = "both";
    public string ClassName { get; set; } = "LandMark";
}

[Table("Fms_MapPaths")]
public class MapPathRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [MaxLength(64)]
    public string StartNodeId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string EndNodeId { get; set; } = string.Empty;

    [MaxLength(32)]
    public string PathType { get; set; } = "Straight";

    public string ControlPointsJson { get; set; } = "[]";

    public double MaxSpeed { get; set; } = 1.5;
    public double Length { get; set; } = 0.0;
    public bool IsBidirectional { get; set; } = true;

    [MaxLength(32)]
    public string Direction { get; set; } = "forward";
    public double RoadWidth { get; set; } = 1.2;
    [MaxLength(32)]
    public string CurveType { get; set; } = "straight_line";
}

[Table("Fms_MutexZones")]
public class MutexZoneRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    public int MaxCapacity { get; set; } = 1;
    public double TimeoutSec { get; set; } = 60.0;

    public string PolygonJson { get; set; } = "[]";
    public string MemberNodeIdsJson { get; set; } = "[]";
    public string MemberPathIdsJson { get; set; } = "[]";
}

[Table("Fms_Orders")]
public class TransportOrderRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [MaxLength(128)]
    public string OrderSerial { get; set; } = string.Empty;

    [MaxLength(64)]
    public string TaskType { get; set; } = "Transport";

    /// <summary>任务来源：internal 内部创建 / system_auto 系统自动 / 其余为外部系统下发。</summary>
    [MaxLength(32)]
    public string BulkTaskType { get; set; } = "task";

    public int Priority { get; set; } = 50;

    [MaxLength(64)]
    public string? BindVehicleId { get; set; }

    [MaxLength(64)]
    public string? AssignedVehicleId { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = "Queued";

    [MaxLength(64)]
    public string? SourceNodeId { get; set; }

    [MaxLength(64)]
    public string TargetNodeId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public double MovementTimeSec { get; set; }
    public double WaitTimeSec { get; set; }
    public double MileageKm { get; set; }
    public string? FailReason { get; set; }

    // Industrial Detailed Parameters (Matching Field Displays)
    [MaxLength(128)]
    public string SubNo { get; set; } = string.Empty;

    [MaxLength(64)]
    public string TaskCodeA { get; set; } = string.Empty;

    [MaxLength(64)]
    public string TaskCodeB { get; set; } = string.Empty;

    [MaxLength(64)]
    public string FinishingAction { get; set; } = "Parking or charging";

    [MaxLength(64)]
    public string CargoSerial { get; set; } = "1";

    public int ArmAction { get; set; } = 1;

    [MaxLength(64)]
    public string Rfid { get; set; } = "#";

    public int Mark { get; set; } = 3;

    [MaxLength(128)]
    public string OptionCode { get; set; } = "131073,133635";

    [MaxLength(64)]
    public string PassPoint { get; set; } = "1";

    [MaxLength(64)]
    public string PassPointToGoal { get; set; } = "25";

    public int RobotType { get; set; } = 99;

    [MaxLength(64)]
    public string Storage { get; set; } = "69";

    [MaxLength(64)]
    public string Target { get; set; } = "69";

    public string OrderStepsJson { get; set; } = "[]";
    public int CurrentStepIndex { get; set; } = 0;

    public List<TaskStepRecord> Steps { get; set; } = new();
}

[Table("Fms_TaskSteps")]
public class TaskStepRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [MaxLength(64)]
    public string OrderId { get; set; } = string.Empty;

    public int StepNo { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Type { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Status { get; set; } = "Waiting to Run";

    public string Parameter { get; set; } = string.Empty;

    public DateTime? FinishedAt { get; set; }
}

[Table("Fms_AuditLogs")]
public class AuditLogRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [MaxLength(64)]
    public string Operator { get; set; } = string.Empty;

    [MaxLength(64)]
    public string ActionType { get; set; } = string.Empty;

    [MaxLength(64)]
    public string EntityType { get; set; } = string.Empty;

    [MaxLength(128)]
    public string EntityId { get; set; } = string.Empty;

    public string Details { get; set; } = string.Empty;

    public bool Success { get; set; } = true;
}

[Table("Fms_RoleGrants")]
public class RoleGrantRecord
{
    [MaxLength(64)]
    public string RoleName { get; set; } = string.Empty;

    [MaxLength(128)]
    public string PermissionName { get; set; } = string.Empty;
}

[Table("Fms_Users")]
public class UserAccountRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [MaxLength(64)]
    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string Salt { get; set; } = string.Empty;

    public string RolesJson { get; set; } = "[\"Operator\"]";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("Fms_WorkflowTemplates")]
public class WorkflowTemplateRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsBuiltIn { get; set; } = false;

    public int Version { get; set; } = 1;

    [MaxLength(32)]
    public string VersionTag { get; set; } = "v1.0.0";

    public string ChangeLog { get; set; } = "初始创建";

    public string StepsJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("Fms_WorkflowTemplateHistories")]
public class WorkflowTemplateHistoryRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [MaxLength(64)]
    public string TemplateId { get; set; } = string.Empty;

    public int Version { get; set; } = 1;

    [MaxLength(32)]
    public string VersionTag { get; set; } = "v1.0.0";

    [MaxLength(512)]
    public string ChangeLog { get; set; } = "初始创建";

    public string StepsJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("Fms_MapPointCloud")]
public class MapPointCloudRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = "default";

    public int TotalCount { get; set; }
    public int RawTotalCount { get; set; }
    public int DownsampleFactor { get; set; } = 25;

    public byte[] Data { get; set; } = Array.Empty<byte>();

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("Fms_RefreshTokens")]
public class RefreshTokenRecord
{
    [Key]
    [MaxLength(128)]
    public string Token { get; set; } = string.Empty;

    [MaxLength(64)]
    public string UserId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Username { get; set; } = string.Empty;

    public string RolesJson { get; set; } = "[]";

    public DateTime ExpiryUtc { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("Fms_StorageZones")]
public class StorageZoneRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Type { get; set; } = "RAW";

    public int Capacity { get; set; } = 10;
    public bool AllowInbound { get; set; } = true;
    public bool AllowOutbound { get; set; } = true;

    public string PolygonJson { get; set; } = "[]";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("Fms_StorageSlots")]
public class StorageSlotRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(64)]
    public string ZoneId { get; set; } = string.Empty;

    [MaxLength(32)]
    public string ZoneType { get; set; } = "RAW";

    [MaxLength(64)]
    public string BindNodeId { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Status { get; set; } = "Empty";

    [MaxLength(128)]
    public string? OccupiedCarrierId { get; set; }

    [MaxLength(64)]
    public string? ReservedOrderId { get; set; }

    [MaxLength(64)]
    public string? ReservedVehicleId { get; set; }

    [MaxLength(256)]
    public string? LockReason { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("Fms_RouteLines")]
public class RouteLineRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    public string MemberPathIdsJson { get; set; } = "[]";
    public string AllowedVehicleTypesJson { get; set; } = "[\"ALL\"]";

    [MaxLength(32)]
    public string DefaultDirection { get; set; } = "forward";

    public bool AllowBackward { get; set; } = false;
    public double SpeedLimit { get; set; } = 1.5;
    public bool IsClosed { get; set; } = false;

    [MaxLength(256)]
    public string? ClosureReason { get; set; }
}

[Table("Fms_DecisionLogs")]
public class DecisionLogRecord
{
    [Key]
    public long Id { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(64)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(128)]
    public string SubjectId { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Outcome { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string? ContextJson { get; set; }
}

[Table("Fms_LogEvents")]
public class LogEventRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [MaxLength(32)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Level { get; set; } = "Information";

    [MaxLength(128)]
    public string Source { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Actor { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? PayloadJson { get; set; }

    [MaxLength(64)]
    public string? TraceId { get; set; }

    [MaxLength(16)]
    public string? Method { get; set; }

    [MaxLength(512)]
    public string? Path { get; set; }

    public int? StatusCode { get; set; }

    public long? DurationMs { get; set; }

    public bool Success { get; set; } = true;
}

[Table("Fms_LogWiki")]
public class LogWikiRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("Fms_AiEndpoints")]
public class AiEndpointRecord
{
    [Key]
    [MaxLength(32)]
    public string Id { get; set; } = "default";

    [MaxLength(512)]
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    [MaxLength(128)]
    public string Model { get; set; } = "gpt-4o-mini";

    [MaxLength(128)]
    public string EmbeddingModel { get; set; } = "";

    public string ApiKey { get; set; } = "";

    [MaxLength(128)]
    public string SchemeName { get; set; } = "";

    [MaxLength(128)]
    public string HeaderName { get; set; } = "Authorization";

    [MaxLength(128)]
    public string QueryName { get; set; } = "";

    public string OpenApiJson { get; set; } = "";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("Fms_ReplayScenes")]
public class ReplaySceneRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(160)]
    public string MapName { get; set; } = string.Empty;

    [MaxLength(64)]
    public string TraceId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string VehicleId { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Status { get; set; } = string.Empty;

    public long StartSimMs { get; set; }
    public long EndSimMs { get; set; }
    public int SampleCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string FramesJson { get; set; } = "[]";
    public string LogsJson { get; set; } = "[]";
}

[Table("Fms_AppState")]
public class AppStateRecord
{
    [Key]
    [MaxLength(64)]
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}


