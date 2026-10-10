using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fms.Domain.Logging;
using Fms.EntityFrameworkCore;
using Fms.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fms.Application.Logging;

public interface ILogCenterService
{
    Task<LogQueryPage> QueryAsync(
        string? category,
        string? keyword,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        string? source = null,
        string? level = null,
        string? taskSerial = null,
        string? vehicleId = null);
    Task<IReadOnlyList<LogWikiRecord>> GetWikiAsync(CancellationToken cancellationToken = default);
    Task SaveWikiAsync(string id, string title, string body, CancellationToken cancellationToken = default);
    Task<AiEndpointView> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(AiEndpointInput input, CancellationToken cancellationToken = default);
    Task<AiTestResult> TestEndpointAsync(AiEndpointInput? tempInput = null, CancellationToken cancellationToken = default);
    Task<AiModelListResult> ListAvailableModelsAsync(AiEndpointInput? tempInput = null, CancellationToken cancellationToken = default);
    Task<LogAnalysisResult> AnalyzeAsync(string question, string mode, CancellationToken cancellationToken = default);
}

public sealed record LogQueryPage(IReadOnlyList<LogEventView> Items, int Total, IReadOnlyList<string> Lines);
public sealed record LogEventView(
    string Id,
    DateTime Timestamp,
    string Category,
    string Level,
    string Source,
    string Actor,
    string Message,
    string? PayloadJson,
    string? TraceId,
    string? Method,
    string? Path,
    int? StatusCode,
    long? DurationMs,
    bool Success,
    string? TaskSerial,
    string? VehicleId);
public sealed record AiEndpointView(string BaseUrl, string Model, string EmbeddingModel, bool HasKey, string ApiKeyMask, string SchemeName, string HeaderName, string QueryName, string OpenApiJson);
public sealed record AiEndpointInput(string? BaseUrl, string? Model, string? EmbeddingModel, string? ApiKey, string? SchemeName, string? HeaderName, string? QueryName, string? OpenApiJson);
public sealed record AiTestResult(bool Success, string Message, long LatencyMs, string Reply);
public sealed record AiModelOption(string Id, string DisplayName, string Description, bool Recommended);
public sealed record AiModelListResult(bool Success, string Message, IReadOnlyList<AiModelOption> Models);
public sealed record LogPassage(string Source, string Text, int Score);
public sealed record LogAnalysisResult(string Mode, string Answer, string? Sql, IReadOnlyList<Dictionary<string, object?>>? Rows, IReadOnlyList<LogPassage> Passages);

public class LogCenterService : ILogCenterService
{
    private readonly FmsDbContext _db;

    public LogCenterService(FmsDbContext db)
    {
        _db = db;
    }

    public async Task<LogQueryPage> QueryAsync(
        string? category,
        string? keyword,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        string? source = null,
        string? level = null,
        string? taskSerial = null,
        string? vehicleId = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = _db.LogEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(item => item.Category == category);
        if (!string.IsNullOrWhiteSpace(source))
        {
            if (source.Equals("Audit", StringComparison.OrdinalIgnoreCase))
                query = query.Where(item => item.Source == "Audit");
            else
                query = query.Where(item => item.Category == source || item.Source == source);
        }
        if (from.HasValue)
            query = query.Where(item => item.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(item => item.Timestamp <= to.Value);
        if (!string.IsNullOrWhiteSpace(level))
        {
            var aliases = LogCorrelation.LevelAliases(level);
            if (aliases.Length > 0)
                query = query.Where(item => aliases.Contains(item.Level));
        }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var word = keyword.Trim();
            query = query.Where(item =>
                item.Message.Contains(word) ||
                item.Actor.Contains(word) ||
                (item.Path != null && item.Path.Contains(word)) ||
                (item.PayloadJson != null && item.PayloadJson.Contains(word)) ||
                (item.TraceId != null && item.TraceId.Contains(word)) ||
                item.Source.Contains(word) ||
                item.Category.Contains(word));
        }
        if (!string.IsNullOrWhiteSpace(taskSerial))
        {
            var serial = taskSerial.Trim();
            query = query.Where(item =>
                (item.TraceId != null && item.TraceId.Contains(serial)) ||
                item.Message.Contains(serial) ||
                (item.Path != null && item.Path.Contains(serial)) ||
                (item.PayloadJson != null && item.PayloadJson.Contains(serial)));
        }
        if (!string.IsNullOrWhiteSpace(vehicleId))
        {
            var vehicle = vehicleId.Trim();
            query = query.Where(item =>
                item.Actor == vehicle ||
                item.Actor.Contains(vehicle) ||
                item.Message.Contains(vehicle) ||
                (item.Path != null && item.Path.Contains(vehicle)) ||
                (item.PayloadJson != null && item.PayloadJson.Contains(vehicle)));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(item => item.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var items = rows.Select(ToView).ToArray();
        var lines = rows.Select(item => LogText.Format(item.Timestamp, item.Category, item.Level, item.Source, item.Actor, item.Method, item.Path, item.StatusCode, item.DurationMs, item.Message)).ToArray();
        return new LogQueryPage(items, total, lines);
    }

    private static LogEventView ToView(LogEventRecord item) =>
        new(
            item.Id,
            item.Timestamp,
            item.Category,
            item.Level,
            item.Source,
            item.Actor,
            item.Message,
            item.PayloadJson,
            item.TraceId,
            item.Method,
            item.Path,
            item.StatusCode,
            item.DurationMs,
            item.Success,
            LogCorrelation.TaskSerialOf(item.TraceId, item.Actor, item.Message, item.Path, item.PayloadJson),
            LogCorrelation.VehicleIdOf(item.TraceId, item.Actor, item.Message, item.Path, item.PayloadJson));

    public Task<IReadOnlyList<LogWikiRecord>> GetWikiAsync(CancellationToken cancellationToken = default) =>
        _db.LogWiki.AsNoTracking().OrderBy(item => item.Title).ToListAsync(cancellationToken).ContinueWith(task => (IReadOnlyList<LogWikiRecord>)task.Result, cancellationToken);

    public async Task SaveWikiAsync(string id, string title, string body, CancellationToken cancellationToken = default)
    {
        var page = await _db.LogWiki.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (page == null)
        {
            page = new LogWikiRecord { Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id };
            _db.LogWiki.Add(page);
        }

        page.Title = title.Trim();
        page.Body = body;
        page.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AiEndpointView> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var row = await _db.AiEndpoints.AsNoTracking().FirstOrDefaultAsync(item => item.Id == "default", cancellationToken);
        if (row == null)
            return new AiEndpointView("https://api.openai.com/v1", "gpt-4o-mini", "", false, "", "", "Authorization", "", "");

        return new AiEndpointView(row.BaseUrl, row.Model, row.EmbeddingModel, !string.IsNullOrWhiteSpace(row.ApiKey), Mask(row.ApiKey), row.SchemeName, row.HeaderName, row.QueryName, row.OpenApiJson);
    }

    public async Task SaveSettingsAsync(AiEndpointInput input, CancellationToken cancellationToken = default)
    {
        var row = await _db.AiEndpoints.FirstOrDefaultAsync(item => item.Id == "default", cancellationToken);
        if (row == null)
        {
            row = new AiEndpointRecord { Id = "default" };
            _db.AiEndpoints.Add(row);
        }

        row.BaseUrl = string.IsNullOrWhiteSpace(input.BaseUrl) ? row.BaseUrl : input.BaseUrl.Trim();
        row.Model = string.IsNullOrWhiteSpace(input.Model) ? row.Model : input.Model.Trim();
        row.EmbeddingModel = input.EmbeddingModel?.Trim() ?? "";
        row.SchemeName = input.SchemeName?.Trim() ?? "";
        row.HeaderName = string.IsNullOrWhiteSpace(input.HeaderName) ? "Authorization" : input.HeaderName.Trim();
        row.QueryName = input.QueryName?.Trim() ?? "";
        row.OpenApiJson = input.OpenApiJson ?? "";
        if (!string.IsNullOrWhiteSpace(input.ApiKey) && input.ApiKey != "******")
            row.ApiKey = input.ApiKey.Trim();
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AiTestResult> TestEndpointAsync(AiEndpointInput? tempInput = null, CancellationToken cancellationToken = default)
    {
        var settings = await _db.AiEndpoints.AsNoTracking().FirstOrDefaultAsync(item => item.Id == "default", cancellationToken);
        var target = new AiEndpointRecord
        {
            Id = "default",
            BaseUrl = settings?.BaseUrl ?? "https://generativelanguage.googleapis.com/v1beta/openai",
            Model = settings?.Model ?? "gemini-2.5-flash",
            ApiKey = settings?.ApiKey ?? "",
            HeaderName = settings?.HeaderName ?? "Authorization",
            QueryName = settings?.QueryName ?? "",
            SchemeName = settings?.SchemeName ?? "",
            OpenApiJson = settings?.OpenApiJson ?? ""
        };

        if (tempInput != null)
        {
            if (!string.IsNullOrWhiteSpace(tempInput.BaseUrl)) target.BaseUrl = tempInput.BaseUrl.Trim();
            if (!string.IsNullOrWhiteSpace(tempInput.Model)) target.Model = tempInput.Model.Trim();
            if (!string.IsNullOrWhiteSpace(tempInput.ApiKey) && tempInput.ApiKey != "******") target.ApiKey = tempInput.ApiKey.Trim();
            if (!string.IsNullOrWhiteSpace(tempInput.HeaderName)) target.HeaderName = tempInput.HeaderName.Trim();
            if (tempInput.QueryName != null) target.QueryName = tempInput.QueryName.Trim();
            if (tempInput.SchemeName != null) target.SchemeName = tempInput.SchemeName.Trim();
            if (tempInput.OpenApiJson != null) target.OpenApiJson = tempInput.OpenApiJson;
        }

        if (string.IsNullOrWhiteSpace(target.ApiKey))
            return new AiTestResult(false, "API Key 尚未配置，请先填写密钥后测试", 0, "");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var answer = await ChatAsync(target, "你是一个工业调度智能助手的连通性测试程序。请简短确认并只回复：连接成功，调度智能诊断就绪。", "连通性测试PING", cancellationToken);
            sw.Stop();
            if (answer.StartsWith("模型调用失败："))
                return new AiTestResult(false, answer, sw.ElapsedMilliseconds, "");

            return new AiTestResult(true, "模型连通成功！", sw.ElapsedMilliseconds, answer);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new AiTestResult(false, $"网络或调用异常: {ex.Message}", sw.ElapsedMilliseconds, "");
        }
    }

    public async Task<AiModelListResult> ListAvailableModelsAsync(AiEndpointInput? tempInput = null, CancellationToken cancellationToken = default)
    {
        var settings = await _db.AiEndpoints.AsNoTracking().FirstOrDefaultAsync(item => item.Id == "default", cancellationToken);
        var baseUrl = (!string.IsNullOrWhiteSpace(tempInput?.BaseUrl))
            ? tempInput.BaseUrl.Trim()
            : settings?.BaseUrl ?? "https://generativelanguage.googleapis.com/v1beta/openai";

        var apiKey = (!string.IsNullOrWhiteSpace(tempInput?.ApiKey) && tempInput.ApiKey != "******")
            ? tempInput.ApiKey.Trim()
            : settings?.ApiKey ?? "";

        if (string.IsNullOrWhiteSpace(apiKey))
            return new AiModelListResult(false, "API Key 未填写或未保存，请先输入密钥后再拉取", []);

        using var http = CreateHttpClient();

        // 1. Google Gemini 官方端点
        if (baseUrl.Contains("googleapis.com", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var googleUrl = $"https://generativelanguage.googleapis.com/v1beta/models?key={Uri.EscapeDataString(apiKey)}";
                using var req = new HttpRequestMessage(HttpMethod.Get, googleUrl);
                using var resp = await http.SendAsync(req, cancellationToken);
                var body = await resp.Content.ReadAsStringAsync(cancellationToken);
                if (!resp.IsSuccessStatusCode)
                    return new AiModelListResult(false, $"Google API 返回错误: {(int)resp.StatusCode} {Trim(body, 200)}", []);

                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("models", out var modelsArr))
                    return new AiModelListResult(false, "Google API 未返回 models 数组", []);

                var list = new List<AiModelOption>();
                foreach (var item in modelsArr.EnumerateArray())
                {
                    var methods = new List<string>();
                    if (item.TryGetProperty("supportedGenerationMethods", out var mNode) && mNode.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in mNode.EnumerateArray())
                        {
                            var s = m.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) methods.Add(s);
                        }
                    }

                    if (methods.Contains("generateContent"))
                    {
                        var rawName = item.TryGetProperty("name", out var nNode) ? nNode.GetString() ?? "" : "";
                        var id = rawName.StartsWith("models/") ? rawName[7..] : rawName;
                        var displayName = item.TryGetProperty("displayName", out var dNode) ? dNode.GetString() ?? id : id;
                        var desc = item.TryGetProperty("description", out var descNode) ? descNode.GetString() ?? "" : "";

                        bool recommended = id.Equals("gemini-flash-lite-latest", StringComparison.OrdinalIgnoreCase) ||
                                           id.Equals("gemini-flash-latest", StringComparison.OrdinalIgnoreCase) ||
                                           id.Contains("3.8-flash") ||
                                           id.Equals("gemini-pro-latest", StringComparison.OrdinalIgnoreCase);

                        list.Add(new AiModelOption(id, displayName, Trim(desc, 100), recommended));
                    }
                }

                list = list.OrderByDescending(x => x.Recommended)
                           .ThenBy(x => x.Id)
                           .ToList();

                return new AiModelListResult(true, $"成功获取 {list.Count} 个可用模型", list);
            }
            catch (Exception ex)
            {
                return new AiModelListResult(false, $"获取模型列表异常: {ex.Message}", []);
            }
        }

        // 2. OpenAI / DeepSeek / 通用兼容端点 (GET /models)
        try
        {
            var modelsUrl = baseUrl.TrimEnd('/');
            if (modelsUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                modelsUrl = modelsUrl[..^17];
            if (modelsUrl.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
                modelsUrl = modelsUrl[..^7];
            
            modelsUrl = modelsUrl.TrimEnd('/') + "/models";

            using var req = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var resp = await http.SendAsync(req, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
                return new AiModelListResult(false, $"拉取模型列表失败: {(int)resp.StatusCode} {Trim(body, 200)}", []);

            using var doc = JsonDocument.Parse(body);
            var list = new List<AiModelOption>();
            if (doc.RootElement.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataArr.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        bool recommended = id.Contains("flash") || id.Contains("chat") || id.Contains("mini");
                        list.Add(new AiModelOption(id, id, "", recommended));
                    }
                }
            }

            list = list.OrderByDescending(x => x.Recommended).ThenBy(x => x.Id).ToList();
            return new AiModelListResult(true, $"成功获取 {list.Count} 个模型", list);
        }
        catch (Exception ex)
        {
            return new AiModelListResult(false, $"请求异常: {ex.Message}", []);
        }
    }

    public async Task<LogAnalysisResult> AnalyzeAsync(string question, string mode, CancellationToken cancellationToken = default)
    {
        var normalized = (mode ?? "rag").ToLowerInvariant();
        if (normalized == "textsql")
            return await AnalyzeSqlAsync(question, cancellationToken);
        return await AnalyzeRetrievalAsync(question, normalized, cancellationToken);
    }

    private async Task<LogAnalysisResult> AnalyzeRetrievalAsync(string question, string mode, CancellationToken cancellationToken)
    {
        var passages = new List<LogPassage>();
        if (mode != "wiki")
        {
            var logs = await _db.LogEvents.AsNoTracking().OrderByDescending(item => item.Timestamp).Take(400).ToListAsync(cancellationToken);
            var ranked = logs
                .Select(item => (item, score: LogText.Score(LogText.Tokenize(question), item.Message + " " + item.PayloadJson + " " + item.Path)))
                .Where(item => item.score > 0)
                .OrderByDescending(item => item.score)
                .Take(6)
                .Select(item => new LogPassage(item.item.Category, LogText.Format(item.item.Timestamp, item.item.Category, item.item.Level, item.item.Source, item.item.Actor, item.item.Method, item.item.Path, item.item.StatusCode, item.item.DurationMs, item.item.Message), item.score));
            passages.AddRange(ranked);
        }

        var wiki = await _db.LogWiki.AsNoTracking().ToListAsync(cancellationToken);
        passages.AddRange(wiki
            .Select(page => (page, score: LogText.Score(LogText.Tokenize(question), page.Title + " " + page.Body)))
            .Where(item => item.score > 0 || mode == "wiki")
            .OrderByDescending(item => item.score)
            .Take(4)
            .Select(item => new LogPassage("Wiki:" + item.page.Title, item.page.Body, item.score)));

        var settings = await _db.AiEndpoints.AsNoTracking().FirstOrDefaultAsync(item => item.Id == "default", cancellationToken);
        if (settings == null || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            var local = passages.Count == 0
                ? "没有检索到相关日志或 Wiki。配置接口密钥后可以用模型归纳。"
                : "未配置模型，以下是检索到的原文：\n" + string.Join("\n", passages.Select(item => item.Text));
            return new LogAnalysisResult(mode, local, null, null, passages);
        }

        var context = string.Join("\n---\n", passages.Select(item => item.Source + "\n" + Trim(item.Text, 800)));
        var answer = await ChatAsync(settings, "只根据给出的日志和 Wiki 回答。没有依据时说明不知道。", $"问题：{question}\n\n资料：\n{context}", cancellationToken);
        return new LogAnalysisResult(mode, answer, null, null, passages);
    }

    private async Task<LogAnalysisResult> AnalyzeSqlAsync(string question, CancellationToken cancellationToken)
    {
        var settings = await _db.AiEndpoints.AsNoTracking().FirstOrDefaultAsync(item => item.Id == "default", cancellationToken);
        var sql = LogText.KeywordSelect(question);
        if (settings != null && !string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            var drafted = await ChatAsync(settings,
                "你是 SQLite 助手。只输出一条 SELECT，查询表 Fms_LogEvents(Timestamp, Category, Level, Source, Actor, Message, Path, StatusCode, DurationMs, Success) 或 Fms_LogWiki(Id, Title, Body)。不要解释，不要分号。",
                question,
                cancellationToken);
            var candidate = drafted.Replace("```sql", "", StringComparison.OrdinalIgnoreCase).Replace("```", "").Trim();
            if (LogSqlGuard.TryAccept(candidate, out var accepted, out _))
                sql = accepted;
        }

        if (!LogSqlGuard.TryAccept(sql, out var safe, out var error))
            return new LogAnalysisResult("textsql", error, sql, [], []);

        var rows = await RunSelectAsync(safe, cancellationToken);
        var answer = rows.Count == 0 ? "查询没有返回行。" : $"返回 {rows.Count} 行。";
        return new LogAnalysisResult("textsql", answer, safe, rows, []);
    }

    private async Task<List<Dictionary<string, object?>>> RunSelectAsync(string sql, CancellationToken cancellationToken)
    {
        var rows = new List<Dictionary<string, object?>>();
        var connection = _db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }

        return rows;
    }

    private static async Task<string> ChatAsync(AiEndpointRecord settings, string system, string user, CancellationToken cancellationToken)
    {
        var binding = OpenApiKeyParser.Resolve(settings.ApiKey, settings.OpenApiJson, settings.SchemeName, settings.HeaderName, settings.QueryName);
        var url = ChatUrl(settings.BaseUrl, binding);
        using var http = CreateHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyKey(request, binding);
        request.Content = JsonContent.Create(new
        {
            model = settings.Model,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        });
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return $"模型调用失败：{(int)response.StatusCode} {Trim(body, 300)}";

        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or JsonException)
        {
            return Trim(body, 500);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var proxyUri = GetProxyUri();
        if (proxyUri != null)
        {
            var handler = new HttpClientHandler
            {
                Proxy = new WebProxy(proxyUri),
                UseProxy = true
            };
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(40) };
        }
        return new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
    }

    private static Uri? GetProxyUri()
    {
        var env = Environment.GetEnvironmentVariable("HTTPS_PROXY") 
               ?? Environment.GetEnvironmentVariable("https_proxy")
               ?? Environment.GetEnvironmentVariable("ALL_PROXY")
               ?? Environment.GetEnvironmentVariable("all_proxy");
        if (!string.IsNullOrWhiteSpace(env) && Uri.TryCreate(env, UriKind.Absolute, out var envUri))
            return envUri;

        int[] commonPorts = [7890, 7897, 10809];
        foreach (var port in commonPorts)
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                if (client.ConnectAsync("127.0.0.1", port).Wait(TimeSpan.FromMilliseconds(150)))
                {
                    return new Uri($"http://127.0.0.1:{port}");
                }
            }
            catch { }
        }

        return null;
    }

    private static string ChatUrl(string baseUrl, ApiKeyBinding binding)
    {
        var url = (baseUrl ?? "").TrimEnd('/');
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return WithQuery(url, binding);
        return WithQuery(url + "/chat/completions", binding);
    }

    private static string WithQuery(string url, ApiKeyBinding binding)
    {
        if (binding.Placement != ApiKeyPlacement.Query) return url;
        var separator = url.Contains('?') ? "&" : "?";
        return url + separator + Uri.EscapeDataString(binding.Name) + "=" + Uri.EscapeDataString(binding.Value);
    }

    private static void ApplyKey(HttpRequestMessage request, ApiKeyBinding binding)
    {
        if (binding.Placement == ApiKeyPlacement.BearerHeader)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", binding.Value);
        else if (binding.Placement == ApiKeyPlacement.Header)
            request.Headers.TryAddWithoutValidation(binding.Name, binding.Value);
    }

    private static string Mask(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey)) return "";
        if (apiKey.Length <= 4) return "****";
        return new string('*', Math.Min(8, apiKey.Length - 4)) + apiKey[^4..];
    }

    private static string Trim(string text, int length) =>
        text.Length <= length ? text : text[..length];
}
