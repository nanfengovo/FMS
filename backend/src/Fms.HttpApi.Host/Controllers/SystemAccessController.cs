using Fms.Application.Identity;
using Fms.Domain.Identity;
using Fms.HttpApi.Host.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fms.HttpApi.Host.Controllers;

[ApiController]
[Route("api/system/access")]
public class SystemAccessController : ControllerBase
{
    private readonly IPermissionService _permissions;

    public SystemAccessController(IPermissionService permissions)
    {
        _permissions = permissions;
    }

    [RequiresPermission(FmsPermissions.SystemRoles)]
    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog()
    {
        var nodes = await _permissions.GetCatalogAsync();
        return Ok(new { code = "0000", data = nodes });
    }

    [RequiresPermission(FmsPermissions.SystemRoles)]
    [HttpGet("roles")]
    public async Task<IActionResult> Roles()
    {
        var roles = await _permissions.GetRolesAsync();
        return Ok(new { code = "0000", data = roles });
    }

    [RequiresPermission(FmsPermissions.SystemRoles)]
    [HttpPut("roles/{roleName}/grants")]
    public async Task<IActionResult> ReplaceGrants(string roleName, [FromBody] ReplaceGrantsDto dto)
    {
        var (ok, message) = await _permissions.ReplaceRoleGrantsAsync(roleName, dto.Permissions ?? new List<string>());
        if (!ok) return BadRequest(new { code = "4000", msg = message });
        return Ok(new { code = "0000", msg = message });
    }

    [RequiresPermission(FmsPermissions.SystemUsers)]
    [HttpGet("users")]
    public async Task<IActionResult> Users()
    {
        var users = await _permissions.GetUsersAsync();
        return Ok(new { code = "0000", data = users });
    }

    [RequiresPermission(FmsPermissions.SystemUsers)]
    [HttpPut("users/{id}/roles")]
    public async Task<IActionResult> ReplaceUserRoles(string id, [FromBody] ReplaceUserRolesDto dto)
    {
        var (ok, message) = await _permissions.ReplaceUserRolesAsync(id, dto.Roles ?? new List<string>());
        if (!ok) return BadRequest(new { code = "4000", msg = message });
        return Ok(new { code = "0000", msg = message });
    }

    public sealed class ReplaceGrantsDto
    {
        public List<string>? Permissions { get; set; }
    }

    public sealed class ReplaceUserRolesDto
    {
        public List<string>? Roles { get; set; }
    }
}
