using System.Diagnostics;
using Fms.Application.Logging;
using Fms.Domain.Logging;

namespace Fms.HttpApi.Host.Logging;

public class ApiAccessLogMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogSink _sink;

    public ApiAccessLogMiddleware(RequestDelegate next, ILogSink sink)
    {
        _next = next;
        _sink = sink;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var watch = Stopwatch.StartNew();
        await _next(context);
        if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)) return;
        if (HttpMethods.IsGet(context.Request.Method) && path.StartsWith("/api/logs", StringComparison.OrdinalIgnoreCase)) return;

        var inbound = path.Contains("xinsong", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/v1", StringComparison.OrdinalIgnoreCase);
        var status = context.Response.StatusCode;
        _sink.Enqueue(new LogWrite
        {
            Category = inbound ? LogCategory.Inbound : LogCategory.Api,
            Level = status >= 500 ? "Error" : status >= 400 ? "Warning" : "Information",
            Source = inbound ? "Inbound" : "Api",
            Actor = context.User.Identity?.Name ?? "anonymous",
            Message = $"{context.Request.Method} {path} -> {status}",
            Method = context.Request.Method,
            Path = path + context.Request.QueryString,
            StatusCode = status,
            DurationMs = watch.ElapsedMilliseconds,
            TraceId = context.TraceIdentifier,
            Success = status < 400
        });
    }
}
