namespace Fms.Domain.Identity;

public enum PermissionKind
{
    Menu = 0,
    Button = 1
}

public sealed record PermissionNode(string Name, string? Parent, string DisplayName, PermissionKind Kind);

/// <summary>
/// 菜单、按钮、接口共用的权限名。父级控制菜单，子级控制按钮和写接口。
/// </summary>
public static class FmsPermissions
{
    public const string Dashboard = "Fms.Dashboard";

    public const string Orders = "Fms.Orders";
    public const string OrdersCreate = "Fms.Orders.Create";
    public const string OrdersCancel = "Fms.Orders.Cancel";
    public const string OrdersPause = "Fms.Orders.Pause";
    public const string OrdersSkip = "Fms.Orders.Skip";

    public const string Fleet = "Fms.Fleet";
    public const string FleetTeleop = "Fms.Fleet.Teleop";
    public const string FleetAutoCharge = "Fms.Fleet.AutoCharge";
    public const string FleetAutoDock = "Fms.Fleet.AutoDock";
    public const string FleetEstop = "Fms.Fleet.Estop";

    public const string Map = "Fms.Map";
    public const string MapEdit = "Fms.Map.Edit";
    public const string MapParse = "Fms.Map.Parse";
    public const string MapPublish = "Fms.Map.Publish";

    public const string Traffic = "Fms.Traffic";
    public const string TrafficForceRelease = "Fms.Traffic.ForceRelease";

    public const string Storage = "Fms.Storage";
    public const string StorageAllocate = "Fms.Storage.Allocate";
    public const string StorageLock = "Fms.Storage.Lock";

    public const string Workflow = "Fms.Workflow";
    public const string WorkflowEdit = "Fms.Workflow.Edit";
    public const string WorkflowRun = "Fms.Workflow.Run";

    public const string Simulation = "Fms.Simulation";
    public const string SimulationControl = "Fms.Simulation.Control";
    public const string SimulationInjectFault = "Fms.Simulation.InjectFault";

    public const string SignalsWrite = "Fms.Signals.Write";

    public const string System = "Fms.System";
    public const string SystemUsers = "Fms.System.Users";
    public const string SystemRoles = "Fms.System.Roles";
    public const string SystemAudit = "Fms.System.Audit";

    public const string Logs = "Fms.Logs";
    public const string LogsAnalyze = "Fms.Logs.Analyze";
    public const string LogsSettings = "Fms.Logs.Settings";
}

public static class FmsPermissionCatalog
{
    public static readonly IReadOnlyList<PermissionNode> All = new PermissionNode[]
    {
        new(FmsPermissions.Dashboard, null, "态势看板", PermissionKind.Menu),
        new(FmsPermissions.Orders, null, "任务", PermissionKind.Menu),
        new(FmsPermissions.OrdersCreate, FmsPermissions.Orders, "创建任务", PermissionKind.Button),
        new(FmsPermissions.OrdersCancel, FmsPermissions.Orders, "取消任务", PermissionKind.Button),
        new(FmsPermissions.OrdersPause, FmsPermissions.Orders, "暂停任务", PermissionKind.Button),
        new(FmsPermissions.OrdersSkip, FmsPermissions.Orders, "跳步", PermissionKind.Button),
        new(FmsPermissions.Fleet, null, "车辆", PermissionKind.Menu),
        new(FmsPermissions.FleetTeleop, FmsPermissions.Fleet, "手动控车", PermissionKind.Button),
        new(FmsPermissions.FleetAutoCharge, FmsPermissions.Fleet, "自动回充", PermissionKind.Button),
        new(FmsPermissions.FleetAutoDock, FmsPermissions.Fleet, "自动停靠", PermissionKind.Button),
        new(FmsPermissions.FleetEstop, FmsPermissions.Fleet, "急停", PermissionKind.Button),
        new(FmsPermissions.Map, null, "地图", PermissionKind.Menu),
        new(FmsPermissions.MapEdit, FmsPermissions.Map, "编辑地图", PermissionKind.Button),
        new(FmsPermissions.MapParse, FmsPermissions.Map, "解析地图", PermissionKind.Button),
        new(FmsPermissions.MapPublish, FmsPermissions.Map, "发布路网", PermissionKind.Button),
        new(FmsPermissions.Traffic, null, "交管", PermissionKind.Menu),
        new(FmsPermissions.TrafficForceRelease, FmsPermissions.Traffic, "强制释放交管区", PermissionKind.Button),
        new(FmsPermissions.Storage, null, "库区", PermissionKind.Menu),
        new(FmsPermissions.StorageAllocate, FmsPermissions.Storage, "分配货位", PermissionKind.Button),
        new(FmsPermissions.StorageLock, FmsPermissions.Storage, "锁定货位", PermissionKind.Button),
        new(FmsPermissions.Workflow, null, "流程", PermissionKind.Menu),
        new(FmsPermissions.WorkflowEdit, FmsPermissions.Workflow, "编辑流程", PermissionKind.Button),
        new(FmsPermissions.WorkflowRun, FmsPermissions.Workflow, "运行流程", PermissionKind.Button),
        new(FmsPermissions.Simulation, null, "仿真", PermissionKind.Menu),
        new(FmsPermissions.SimulationControl, FmsPermissions.Simulation, "控制仿真", PermissionKind.Button),
        new(FmsPermissions.SimulationInjectFault, FmsPermissions.Simulation, "注入故障", PermissionKind.Button),
        new(FmsPermissions.SignalsWrite, null, "写信号", PermissionKind.Button),
        new(FmsPermissions.System, null, "系统", PermissionKind.Menu),
        new(FmsPermissions.SystemUsers, FmsPermissions.System, "用户管理", PermissionKind.Button),
        new(FmsPermissions.SystemRoles, FmsPermissions.System, "角色与菜单权限", PermissionKind.Button),
        new(FmsPermissions.SystemAudit, FmsPermissions.System, "查看审计", PermissionKind.Button),
        new(FmsPermissions.Logs, null, "日志中心", PermissionKind.Menu),
        new(FmsPermissions.LogsAnalyze, FmsPermissions.Logs, "AI分析日志", PermissionKind.Button),
        new(FmsPermissions.LogsSettings, FmsPermissions.Logs, "配置接口密钥", PermissionKind.Button)
    };

    public static readonly string[] RoleNames = ["Admin", "Engineer", "Operator", "Viewer"];

    private static readonly string[] OperationalMenus =
    [
        FmsPermissions.Dashboard,
        FmsPermissions.Orders,
        FmsPermissions.Fleet,
        FmsPermissions.Map,
        FmsPermissions.Traffic,
        FmsPermissions.Storage,
        FmsPermissions.Workflow,
        FmsPermissions.Simulation,
        FmsPermissions.Logs
    ];

    private static readonly string[] OperatorButtons =
    [
        FmsPermissions.OrdersCreate,
        FmsPermissions.OrdersCancel,
        FmsPermissions.OrdersPause,
        FmsPermissions.FleetTeleop,
        FmsPermissions.FleetAutoCharge,
        FmsPermissions.FleetAutoDock
    ];

    public static IReadOnlyList<string> GrantsFor(string role)
    {
        if (role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            return All.Select(p => p.Name).ToArray();

        if (role.Equals("Engineer", StringComparison.OrdinalIgnoreCase))
        {
            return All
                .Select(p => p.Name)
                .Where(name => name != FmsPermissions.System && name != FmsPermissions.SystemUsers && name != FmsPermissions.SystemRoles && name != FmsPermissions.LogsSettings)
                .ToArray();
        }

        if (role.Equals("Operator", StringComparison.OrdinalIgnoreCase))
            return OperationalMenus.Concat(OperatorButtons).ToArray();

        if (role.Equals("Viewer", StringComparison.OrdinalIgnoreCase))
            return OperationalMenus;

        return Array.Empty<string>();
    }

    public static bool RoleHas(string role, string permission) =>
        GrantsFor(role).Contains(permission, StringComparer.OrdinalIgnoreCase);

    public static string NormalizeRole(string role) => role switch
    {
        "R_SUPER" or "SafetyOfficer" or "Supervisor" => role is "R_SUPER" ? "Admin" : "Engineer",
        "R_ENGINEER" => "Engineer",
        "R_OPERATOR" => "Operator",
        "R_VIEWER" => "Viewer",
        "Admin" or "Engineer" or "Operator" or "Viewer" => role,
        _ => role
    };
}
