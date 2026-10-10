using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Fms.Application.Identity;
using Fms.Domain.Identity;
using Fms.HttpApi.Host.Authorization;
using Fms.Application.Logging;
using Fms.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Fms.HttpApi.Host.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IPermissionService _permissions;
    private readonly IAuditLogService _auditLogService;
    private readonly FmsDbContext _dbContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private const string JwtSecret = "Fms_Industrial_Agv_Amr_Dispatcher_Secret_Key_2026_9999!";

    public AuthController(IPermissionService permissions, IAuditLogService auditLogService, FmsDbContext dbContext, IServiceScopeFactory scopeFactory)
    {
        _permissions = permissions;
        _auditLogService = auditLogService;
        _dbContext = dbContext;
        _scopeFactory = scopeFactory;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.UserName) || string.IsNullOrWhiteSpace(req.Password))
        {
            return BadRequest(new { code = "4000", msg = "用户名和密码不能为空" });
        }

        string username = req.UserName.Trim();
        string password = req.Password;

        string userId = Guid.NewGuid().ToString("N");
        List<string> roles = new();
        bool isAuthenticated = false;

        // 1. 优先使用 EF Core SQLite 数据库的 PBKDF2 加盐安全比对
        try
        {
            var dbUser = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
            if (dbUser != null && dbUser.IsActive)
            {
                if (FmsSecurityHelper.VerifyPassword(password, dbUser.PasswordHash, dbUser.Salt))
                {
                    isAuthenticated = true;
                    userId = dbUser.Id;
                    roles = JsonSerializer.Deserialize<List<string>>(dbUser.RolesJson) ?? new List<string> { "Operator" };
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Auth] DB lookup error: {ex.Message}");
        }

        if (!isAuthenticated)
        {
            _auditLogService.Log(username, "USER_LOGIN_FAILED", "Auth", "密码错误或用户已被禁用", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1", false);
            return Unauthorized(new { code = "4001", msg = "用户名或密码错误" });
        }

        _auditLogService.Log(username, "USER_LOGIN_SUCCESS", "Auth", $"登录成功，角色: {string.Join(',', roles)}", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1", true);

        // 签发工业合规标准 JWT Bearer Token
        string token = GenerateJwtToken(userId, username, roles);
        string refreshToken = $"FMS_REFRESH_{Guid.NewGuid():N}";
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
                db.RefreshTokens.Add(new Fms.EntityFrameworkCore.Entities.RefreshTokenRecord
                {
                    Token = refreshToken,
                    UserId = userId,
                    Username = username,
                    RolesJson = JsonSerializer.Serialize(roles),
                    ExpiryUtc = DateTime.UtcNow.AddDays(7),
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthController] Failed to save refreshToken: {ex.Message}");
            }
        });

        return Ok(new
        {
            code = "0000",
            msg = "登录成功",
            data = new
            {
                token,
                refreshToken,
                userName = username,
                roles = roles.ToArray()
            }
        });
    }

    [AllowAnonymous]
    [HttpPost("refreshToken")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequestDto req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.RefreshToken))
        {
            return BadRequest(new { code = "4000", msg = "RefreshToken不能为空" });
        }

        string incomingToken = req.RefreshToken.Trim();
        var record = await _dbContext.RefreshTokens.FirstOrDefaultAsync(r => r.Token == incomingToken);
        if (record == null || record.ExpiryUtc < DateTime.UtcNow)
        {
            if (record != null)
            {
                _dbContext.RefreshTokens.Remove(record);
                await _dbContext.SaveChangesAsync();
            }
            return Unauthorized(new { code = "4001", msg = "RefreshToken无效或已过期，请重新登录" });
        }

        // 工业安全令牌轮转机制：消费即失效，防止重放盗用
        _dbContext.RefreshTokens.Remove(record);

        var userRecord = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == record.Username);
        var roles = PermissionService.ReadRoles(userRecord?.RolesJson ?? record.RolesJson);
        if (roles.Count == 0) roles.Add("Viewer");
        string newToken = GenerateJwtToken(record.UserId, record.Username, roles);
        string newRefreshToken = $"FMS_REFRESH_{Guid.NewGuid():N}";

        _dbContext.RefreshTokens.Add(new Fms.EntityFrameworkCore.Entities.RefreshTokenRecord
        {
            Token = newRefreshToken,
            UserId = record.UserId,
            Username = record.Username,
            RolesJson = JsonSerializer.Serialize(roles),
            ExpiryUtc = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            code = "0000",
            msg = "刷新成功",
            data = new
            {
                token = newToken,
                refreshToken = newRefreshToken,
                userName = record.Username,
                roles = roles.ToArray()
            }
        });
    }

    [AllowAuthenticated]
    [HttpGet("getUserInfo")]
    public async Task<IActionResult> GetUserInfo()
    {
        var user = HttpContext.User;
        if (user.Identity == null || !user.Identity.IsAuthenticated)
        {
            return Unauthorized(new { code = "4001", msg = "用户未登录或令牌已失效" });
        }

        string username = user.Identity.Name
                          ?? user.FindFirst(ClaimTypes.Name)?.Value
                          ?? user.FindFirst("sub")?.Value
                          ?? user.FindFirst("unique_name")?.Value
                          ?? "admin";

        var roles = user.FindAll(ClaimTypes.Role)
            .Concat(user.FindAll("role"))
            .Concat(user.FindAll("roles"))
            .Select(c => c.Value)
            .Distinct()
            .ToList();

        if (roles.Count == 0)
        {
            roles.Add("Viewer");
        }

        string userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? user.FindFirst("nameid")?.Value
                        ?? ("usr-" + username);

        var buttons = await _permissions.GetGrantedAsync(roles);

        return Ok(new
        {
            code = "0000",
            msg = "获取成功",
            data = new
            {
                userId,
                userName = username,
                roles = roles.ToArray(),
                buttons = buttons.ToArray()
            }
        });
    }

    private static string GenerateJwtToken(string userId, string username, IEnumerable<string> roles)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(JwtSecret);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, username),
            new(JwtRegisteredClaimNames.Sub, username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        foreach (var r in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, r));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddDays(7),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public record LoginRequestDto(string? UserName, string? Password);
    public record RefreshTokenRequestDto(string? RefreshToken);
}
