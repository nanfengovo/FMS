using Fms.Application.Identity;
using Fms.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Fms.HttpApi.Host.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequiresPermissionAttribute : Attribute
{
    public RequiresPermissionAttribute(string permission)
    {
        Permission = permission;
    }

    public string Permission { get; }
}

/// <summary>
/// 已登录即可访问，不要求权限名。只用于读取当前用户自己的权限。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AllowAuthenticatedAttribute : Attribute;

public sealed class PermissionEnforcementFilter : IAsyncAuthorizationFilter
{
    private readonly IPermissionService _permissions;

    public PermissionEnforcementFilter(IPermissionService permissions)
    {
        _permissions = permissions;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var metadata = context.ActionDescriptor.EndpointMetadata;
        if (metadata.Any(m => m is IAllowAnonymous)) return;

        var authenticated = context.HttpContext.User.Identity?.IsAuthenticated == true;
        if (metadata.Any(m => m is AllowAuthenticatedAttribute))
        {
            if (!authenticated)
            {
                context.Result = new UnauthorizedObjectResult(new { code = "4001", msg = "未登录" });
            }
            return;
        }

        var requirement = metadata.OfType<RequiresPermissionAttribute>().LastOrDefault();
        if (requirement == null)
        {
            context.Result = new ObjectResult(new { code = "4003", msg = "接口未绑定权限" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        if (!authenticated)
        {
            context.Result = new UnauthorizedObjectResult(new { code = "4001", msg = "未登录" });
            return;
        }

        if (!await _permissions.HasAsync(context.HttpContext.User, requirement.Permission, context.HttpContext.RequestAborted))
        {
            context.Result = new ObjectResult(new
            {
                code = "4003",
                msg = $"当前角色没有权限 {requirement.Permission}"
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
