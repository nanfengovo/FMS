using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Fms.Domain.Identity;
using Fms.Engine;
using Fms.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fms.Application.Identity;

public interface IIdentityService
{
    User? Authenticate(string username, string password);
    bool CheckPermission(string username, string resource, string action);
    bool HasActionPermission(ClaimsPrincipal? user, string action);
    bool HasActionPermission(string role, string action);
    IReadOnlyList<Role> GetAllRoles();
    IReadOnlyList<User> GetAllUsers();
    (bool IsSafe, string? ErrorReason) CheckSafetyInterlock(string action, DispatchEngine engine, string? operatorName = null, string? reason = null);
}

/// <summary>
/// 身份、授权与安全互锁服务：
/// 1. 身份认证以 EF Core 数据库 PBKDF2 加盐散列为唯一真源；
/// 2. 授权按操作细分（发布路网、强释交管区、改流程、急停、写信号）；
/// 3. 安全互锁校验物理运行风险，守住防撞防误操作底线。
/// </summary>
public class IdentityService : IIdentityService
{
    private readonly ConcurrentDictionary<string, Role> _roles = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ConcurrentDictionary<string, (User User, string Salt)> _offlinePbkdf2Users = new(StringComparer.OrdinalIgnoreCase);

    public IdentityService(IServiceScopeFactory? scopeFactory = null)
    {
        _scopeFactory = scopeFactory;
        SeedDefaultRolesAndOfflineUsers();
    }

    private void SeedDefaultRolesAndOfflineUsers()
    {
        // 1. 标准工业角色定义
        var rSuper = new Role
        {
            Name = "Admin",
            Description = "超级管理员，拥有全系统最高控制权限",
            Permissions = new List<Permission>
            {
                new() { Resource = "*", Action = "*" }
            }
        };

        var rEngineer = new Role
        {
            Name = "Engineer",
            Description = "工艺工程师，负责路网发布、工艺流编排与算法压测",
            Permissions = new List<Permission>
            {
                new() { Resource = "map", Action = "read" },
                new() { Resource = "map", Action = "write" },
                new() { Resource = "map", Action = "publish" },
                new() { Resource = "simulation", Action = "run" },
                new() { Resource = "workflow", Action = "write" },
                new() { Resource = "mutex", Action = "force_release" },
                new() { Resource = "order", Action = "read" },
                new() { Resource = "signal", Action = "write" }
            }
        };

        var rSupervisor = new Role
        {
            Name = "Supervisor",
            Description = "现场值班主管，拥有交管强释、急停与任务调度特权",
            Permissions = new List<Permission>
            {
                new() { Resource = "mutex", Action = "force_release" },
                new() { Resource = "vehicle", Action = "control" },
                new() { Resource = "order", Action = "create" },
                new() { Resource = "order", Action = "cancel" },
                new() { Resource = "signal", Action = "write" }
            }
        };

        var rOperator = new Role
        {
            Name = "Operator",
            Description = "现场操作员，负责日常监控、任务派发与常规复位",
            Permissions = new List<Permission>
            {
                new() { Resource = "vehicle", Action = "control" },
                new() { Resource = "order", Action = "create" },
                new() { Resource = "order", Action = "cancel" },
                new() { Resource = "monitor", Action = "read" },
                new() { Resource = "signal", Action = "write" }
            }
        };

        var rViewer = new Role
        {
            Name = "Viewer",
            Description = "只读看板，仅可查看数字孪生大屏与状态看板",
            Permissions = new List<Permission>
            {
                new() { Resource = "dashboard", Action = "read" },
                new() { Resource = "monitor", Action = "read" }
            }
        };

        _roles["Admin"] = rSuper;
        _roles["R_SUPER"] = rSuper;
        _roles["Engineer"] = rEngineer;
        _roles["R_ENGINEER"] = rEngineer;
        _roles["Supervisor"] = rSupervisor;
        _roles["Operator"] = rOperator;
        _roles["R_OPERATOR"] = rOperator;
        _roles["Viewer"] = rViewer;
        _roles["R_VIEWER"] = rViewer;

        // 离线单元测试沙箱用户（仅采用 PBKDF2 工业标准加盐散列，彻底杜绝 SHA256 弱算法）
        CreateOfflineUser("admin", "admin123", rSuper);
        CreateOfflineUser("engineer", "engineer123", rEngineer);
        CreateOfflineUser("operator", "operator123", rOperator);
        CreateOfflineUser("viewer", "viewer123", rViewer);
    }

    private void CreateOfflineUser(string username, string password, Role role)
    {
        var (hash, salt) = FmsSecurityHelper.HashPassword(password);
        var user = new User
        {
            Username = username,
            PasswordHash = hash,
            Roles = new List<Role> { role }
        };
        _offlinePbkdf2Users[username] = (user, salt);
    }

    public User? Authenticate(string username, string password)
    {
        if (_scopeFactory != null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
                var dbUser = db.Users.AsNoTracking().FirstOrDefault(u => u.Username.ToLower() == username.ToLower());
                if (dbUser != null && dbUser.IsActive && FmsSecurityHelper.VerifyPassword(password, dbUser.PasswordHash, dbUser.Salt))
                {
                    var roleNames = JsonSerializer.Deserialize<List<string>>(dbUser.RolesJson) ?? new List<string> { "Operator" };
                    return new User
                    {
                        Username = dbUser.Username,
                        PasswordHash = dbUser.PasswordHash,
                        IsActive = dbUser.IsActive,
                        Roles = roleNames.Select(r => _roles.TryGetValue(r, out var roleObj) ? roleObj : new Role { Name = r }).ToList()
                    };
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        // 离线无库环境：使用 PBKDF2 验证
        if (_offlinePbkdf2Users.TryGetValue(username, out var record))
        {
            if (record.User.IsActive && FmsSecurityHelper.VerifyPassword(password, record.User.PasswordHash, record.Salt))
            {
                return record.User;
            }
        }
        return null;
    }

    public bool HasActionPermission(ClaimsPrincipal? user, string action)
    {
        if (user == null) return false;

        // 超级管理员通行
        if (user.IsInRole("Admin") || user.IsInRole("R_SUPER") || user.Identity?.Name?.Equals("admin", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        var roles = user.Claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "role").Select(c => c.Value);
        foreach (var role in roles)
        {
            if (HasActionPermission(role, action)) return true;
        }

        return false;
    }

    public bool HasActionPermission(string role, string action)
    {
        if (_roles.TryGetValue(role, out var roleObj))
        {
            foreach (var perm in roleObj.Permissions)
            {
                if (perm.Action == "*" || perm.Action.Equals(action, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        // 动作权限矩阵规范
        switch (action.ToUpperInvariant())
        {
            case "MAP_PUBLISH":
            case "MAP_MODIFY":
                return role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                       role.Equals("Engineer", StringComparison.OrdinalIgnoreCase) ||
                       role.Equals("R_ENGINEER", StringComparison.OrdinalIgnoreCase);

            case "FORCE_RELEASE_MUTEX":
                return role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                       role.Equals("Supervisor", StringComparison.OrdinalIgnoreCase) ||
                       role.Equals("Engineer", StringComparison.OrdinalIgnoreCase);

            case "WORKFLOW_MODIFY":
                return role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                       role.Equals("Engineer", StringComparison.OrdinalIgnoreCase);

            case "ESTOP":
            case "SIGNAL_MODIFY":
            case "TASK_DISPATCH":
                return !role.Equals("Viewer", StringComparison.OrdinalIgnoreCase) &&
                       !role.Equals("R_VIEWER", StringComparison.OrdinalIgnoreCase);

            case "SYSTEM_VIEW":
                return true;

            default:
                return role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool CheckPermission(string username, string resource, string action)
    {
        List<Role> userRoles = new();
        if (_scopeFactory != null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
                var dbUser = db.Users.AsNoTracking().FirstOrDefault(u => u.Username.ToLower() == username.ToLower());
                if (dbUser != null && dbUser.IsActive)
                {
                    var roleNames = JsonSerializer.Deserialize<List<string>>(dbUser.RolesJson) ?? new List<string>();
                    userRoles = roleNames.Select(r => _roles.TryGetValue(r, out var roleObj) ? roleObj : new Role { Name = r }).ToList();
                }
            }
            catch { }
        }

        if (userRoles.Count == 0 && _offlinePbkdf2Users.TryGetValue(username, out var offline))
        {
            userRoles = offline.User.Roles.ToList();
        }

        if (userRoles.Count == 0)
        {
            if (username.Equals("admin", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        foreach (var role in userRoles)
        {
            foreach (var perm in role.Permissions)
            {
                if (perm.Resource == "*" || perm.Resource.Equals(resource, StringComparison.OrdinalIgnoreCase))
                {
                    if (perm.Action == "*" || perm.Action.Equals(action, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    public IReadOnlyList<Role> GetAllRoles() => _roles.Values.DistinctBy(r => r.Name).ToList();

    public IReadOnlyList<User> GetAllUsers()
    {
        if (_scopeFactory != null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
                var dbUsers = db.Users.AsNoTracking().ToList();
                return dbUsers.Select(u =>
                {
                    var roleNames = JsonSerializer.Deserialize<List<string>>(u.RolesJson) ?? new List<string>();
                    return new User
                    {
                        Username = u.Username,
                        PasswordHash = u.PasswordHash,
                        IsActive = u.IsActive,
                        Roles = roleNames.Select(r => _roles.TryGetValue(r, out var roleObj) ? roleObj : new Role { Name = r }).ToList()
                    };
                }).ToList();
            }
            catch { }
        }
        return _offlinePbkdf2Users.Values.Select(v => v.User).ToList();
    }

    /// <summary>
    /// 工业级安全互锁校验：防止在物理危险状态下执行不可逆破坏性操作
    /// </summary>
    public (bool IsSafe, string? ErrorReason) CheckSafetyInterlock(
        string action,
        DispatchEngine engine,
        string? operatorName = null,
        string? reason = null)
    {
        switch (action.ToUpperInvariant())
        {
            case "MAP_MODIFY":
            case "MAP_DELETE":
            case "MAP_PUBLISH":
                // 检查是否有车辆处于 Running 状态
                bool hasRunningVehicles = engine.Vehicles.Values.Any(v =>
                    v.Status == Domain.Shared.Enums.VehicleStatus.Running);
                if (hasRunningVehicles)
                {
                    return (false, "工业安全互锁触发：当前有车辆正在巡航作业中，严禁在线修改拓扑结构！请先急停或暂停车队。");
                }
                break;

            case "EMERGENCY_STOP_ALL":
                // 急停始终允许
                return (true, null);

            case "FORCE_RELEASE_MUTEX":
                // 强制解锁互斥区必须登记操作员实名与明确故障原因，杜绝匿名盲解
                if (string.IsNullOrWhiteSpace(operatorName) || string.IsNullOrWhiteSpace(reason))
                {
                    return (false, "工业安全互锁触发：强制解除互斥区锁必须实名登记操作人员与明确现场故障原因！");
                }
                return (true, null);
        }

        return (true, null);
    }
}
