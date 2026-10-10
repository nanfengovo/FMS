using System.Text;
using Fms.Application;
using Fms.Application.Map;
using Fms.Application.Rcs;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Topology;
using Fms.Domain.Vehicles;
using Fms.Engine;
using Fms.EntityFrameworkCore;
using Fms.HttpApi.Host.Hubs;
using Fms.HttpApi.Host.Workers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// 1. 服务注入
builder.Services.AddScoped<Fms.Application.Identity.IPermissionService, Fms.Application.Identity.PermissionService>();
builder.Services.AddScoped<Fms.HttpApi.Host.Authorization.PermissionEnforcementFilter>();
builder.Services.AddControllers(options =>
{
    options.Filters.AddService<Fms.HttpApi.Host.Authorization.PermissionEnforcementFilter>();
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpClient();
builder.Services.AddSignalR();

var logSink = new Fms.Application.Logging.LogSink();
builder.Services.AddSingleton(logSink);
builder.Services.AddSingleton<Fms.Domain.Logging.ILogSink>(logSink);
builder.Logging.AddProvider(new Fms.HttpApi.Host.Logging.RuntimeLogLoggerProvider(logSink));
builder.Services.AddScoped<Fms.Application.Logging.ILogCenterService, Fms.Application.Logging.LogCenterService>();
builder.Services.AddHostedService<Fms.HttpApi.Host.Workers.LogDrainWorker>();

// 工业持久化数据库与数据播种 (EF Core SQLite)
builder.Services.AddDbContext<FmsDbContext>(options =>
{
    options.UseSqlite("Data Source=fms_industrial.db;Cache=Shared;Mode=ReadWriteCreate;Default Timeout=15;");
});
builder.Services.AddScoped<FmsDataSeeder>();

// 工业标准 JWT Bearer 身份认证与授权
var jwtKey = Encoding.UTF8.GetBytes("Fms_Industrial_Agv_Amr_Dispatcher_Secret_Key_2026_9999!");
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(jwtKey),
        ValidateIssuer = false,
        ValidateAudience = false,
        ClockSkew = TimeSpan.Zero
    };

    // WebSocket / SSE 握手无法携带 Authorization 头，SignalR 客户端把令牌放在 access_token 查询参数里
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();

// 内存态核心单例与第二轨决策日志
builder.Services.AddSingleton<Fms.Domain.Logging.IDecisionLogService, Fms.Application.Logging.DecisionLogService>();
builder.Services.AddSingleton<DispatchEngine>();
builder.Services.AddSingleton<XMapParserService>();
builder.Services.AddSingleton<XMapSerializerService>();
builder.Services.AddSingleton<RcsConfigurationService>();
builder.Services.AddSingleton<IRcsCallbackService, RcsCallbackService>();
builder.Services.AddSingleton<Fms.Application.Logging.IFlightRecorderService, Fms.Application.Logging.FlightRecorderService>();
builder.Services.AddSingleton<Fms.HttpApi.Host.Replay.ReplaySceneService>();
builder.Services.AddSingleton<Fms.Application.Logging.IAuditLogService, Fms.Application.Logging.AuditLogService>();
builder.Services.AddSingleton<Fms.Application.Identity.IIdentityService, Fms.Application.Identity.IdentityService>();
// 工业协议网关服务 (Modbus-TCP / MQTT / 虚拟 PLC)
builder.Services.AddSingleton<Fms.Application.Gateway.IndustrialGatewayService>();
builder.Services.AddSingleton<Fms.Application.Gateway.IIndustrialGatewayService>(sp => sp.GetRequiredService<Fms.Application.Gateway.IndustrialGatewayService>());

builder.Services.AddSingleton<FmsAppService>();

// 后台 100ms Tick 驱动循环与推送服务
builder.Services.AddHostedService<EngineTickWorker>();
builder.Services.AddHostedService<EnginePersistenceWorker>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// 工业种子数据自动落库初始化与调度内核状态持久化重启恢复 (从 SQLite 闭环还原)
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA synchronous=NORMAL;");
        var seeder = scope.ServiceProvider.GetRequiredService<FmsDataSeeder>();
        await seeder.SeedAsync(db);

        var appService = scope.ServiceProvider.GetRequiredService<FmsAppService>();
        await appService.RestoreFromDatabaseAsync(db);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[EF Core Seeder / Restore] Error: {ex.Message}");
    }
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<Fms.HttpApi.Host.Logging.ApiAccessLogMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapHub<FmsRealtimeHub>("/hubs/fms");
app.MapFallbackToFile("index.html");

app.Run();

