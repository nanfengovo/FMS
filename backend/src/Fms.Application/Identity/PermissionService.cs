using System.Security.Claims;
using System.Text.Json;
using Fms.Domain.Identity;
using Fms.EntityFrameworkCore;
using Fms.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fms.Application.Identity;

public interface IPermissionService
{
    Task<IReadOnlyList<string>> GetGrantedAsync(IEnumerable<string> roles, CancellationToken cancellationToken = default);
    Task<bool> HasAsync(ClaimsPrincipal? user, string permission, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PermissionNode>> GetCatalogAsync();
    Task<IReadOnlyList<RoleGrantView>> GetRolesAsync(CancellationToken cancellationToken = default);
    Task<(bool Ok, string Message)> ReplaceRoleGrantsAsync(string roleName, IReadOnlyList<string> permissions, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserAccessView>> GetUsersAsync(CancellationToken cancellationToken = default);
    Task<(bool Ok, string Message)> ReplaceUserRolesAsync(string userId, IReadOnlyList<string> roles, CancellationToken cancellationToken = default);
}

public sealed record RoleGrantView(string RoleName, string Description, IReadOnlyList<string> Permissions);
public sealed record UserAccessView(string Id, string Username, bool IsActive, IReadOnlyList<string> Roles);

public class PermissionService : IPermissionService
{
    private static readonly Dictionary<string, string> RoleDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Admin"] = "超级管理员，拥有全部菜单和按钮",
        ["Engineer"] = "调度工程师，可改图、急停、强释交管区并查看审计",
        ["Operator"] = "现场操作员，可派任务和控车，不能改图或急停",
        ["Viewer"] = "只读，只能查看运行画面"
    };

    private readonly FmsDbContext _db;

    public PermissionService(FmsDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<string>> GetGrantedAsync(IEnumerable<string> roles, CancellationToken cancellationToken = default)
    {
        var normalized = roles
            .Select(FmsPermissionCatalog.NormalizeRole)
            .Where(r => FmsPermissionCatalog.RoleNames.Contains(r, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0) return Array.Empty<string>();

        var grants = await _db.RoleGrants.AsNoTracking().ToListAsync(cancellationToken);
        return grants
            .Where(g => normalized.Contains(g.RoleName, StringComparer.OrdinalIgnoreCase))
            .Select(g => g.PermissionName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<bool> HasAsync(ClaimsPrincipal? user, string permission, CancellationToken cancellationToken = default)
    {
        if (user?.Identity?.IsAuthenticated != true) return false;
        var roles = user.FindAll(ClaimTypes.Role).Concat(user.FindAll("role")).Select(c => c.Value);
        var granted = await GetGrantedAsync(roles, cancellationToken);
        return granted.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }

    public Task<IReadOnlyList<PermissionNode>> GetCatalogAsync() =>
        Task.FromResult(FmsPermissionCatalog.All);

    public async Task<IReadOnlyList<RoleGrantView>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        var grants = await _db.RoleGrants.AsNoTracking().ToListAsync(cancellationToken);
        return FmsPermissionCatalog.RoleNames.Select(role => new RoleGrantView(
            role,
            RoleDescriptions[role],
            grants.Where(g => g.RoleName.Equals(role, StringComparison.OrdinalIgnoreCase)).Select(g => g.PermissionName).OrderBy(n => n).ToArray()
        )).ToArray();
    }

    public async Task<(bool Ok, string Message)> ReplaceRoleGrantsAsync(string roleName, IReadOnlyList<string> permissions, CancellationToken cancellationToken = default)
    {
        var role = FmsPermissionCatalog.RoleNames.FirstOrDefault(r => r.Equals(roleName, StringComparison.OrdinalIgnoreCase));
        if (role == null) return (false, "未知角色");

        var known = FmsPermissionCatalog.All.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = permissions.Where(known.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var existing = await _db.RoleGrants.Where(g => g.RoleName == role).ToListAsync(cancellationToken);
        _db.RoleGrants.RemoveRange(existing);
        foreach (var permission in selected)
        {
            _db.RoleGrants.Add(new RoleGrantRecord { RoleName = role, PermissionName = permission });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return (true, "角色权限已更新，下一个请求即按新授予生效");
    }

    public async Task<IReadOnlyList<UserAccessView>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var users = await _db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(cancellationToken);
        return users.Select(u => new UserAccessView(u.Id, u.Username, u.IsActive, ReadRoles(u.RolesJson))).ToArray();
    }

    public async Task<(bool Ok, string Message)> ReplaceUserRolesAsync(string userId, IReadOnlyList<string> roles, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null) return (false, "用户不存在");

        var normalized = roles
            .Select(FmsPermissionCatalog.NormalizeRole)
            .Where(r => FmsPermissionCatalog.RoleNames.Contains(r, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0) return (false, "至少绑定一个角色");

        if (user.Username.Equals("admin", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("Admin", StringComparer.OrdinalIgnoreCase))
        {
            return (false, "admin 账号必须保留 Admin 角色");
        }

        user.RolesJson = JsonSerializer.Serialize(normalized);
        await _db.SaveChangesAsync(cancellationToken);
        return (true, "用户角色已更新，重新登录后生效");
    }

    public static List<string> ReadRoles(string? rolesJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(rolesJson ?? "[]") ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}
