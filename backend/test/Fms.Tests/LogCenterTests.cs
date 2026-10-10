using Fms.Domain.Logging;

namespace Fms.Tests;

public class LogCenterTests
{
    [Fact]
    public void OpenApi_document_places_api_key_in_the_named_header()
    {
        const string document = """
            { "components": { "securitySchemes": { "WarehouseKey": { "type": "apiKey", "in": "header", "name": "X-Warehouse-Key" } } } }
            """;

        var binding = OpenApiKeyParser.Resolve("secret-key", document, "WarehouseKey", null, null);

        Assert.Equal(ApiKeyPlacement.Header, binding.Placement);
        Assert.Equal("X-Warehouse-Key", binding.Name);
        Assert.Equal("secret-key", binding.Value);
    }

    [Fact]
    public void Missing_OpenApi_document_defaults_to_bearer()
    {
        var binding = OpenApiKeyParser.Resolve("sk-test", null, null, null, null);

        Assert.Equal(ApiKeyPlacement.BearerHeader, binding.Placement);
        Assert.Equal("Authorization", binding.Name);
    }

    [Fact]
    public void Sql_guard_rejects_writes_and_caps_limit()
    {
        Assert.False(LogSqlGuard.TryAccept("DELETE FROM Fms_LogEvents", out _, out var error));
        Assert.Contains("SELECT", error);

        Assert.True(LogSqlGuard.TryAccept("SELECT Message FROM Fms_LogEvents WHERE Success = 0 LIMIT 9999", out var safe, out _));
        Assert.EndsWith("LIMIT 200", safe);
    }

    [Fact]
    public void Keyword_question_selects_the_matching_category()
    {
        var sql = LogText.KeywordSelect("最近的入站失败");

        Assert.Contains("Category = 'Inbound'", sql);
        Assert.Contains("Success = 0", sql);
        Assert.True(LogSqlGuard.TryAccept(sql, out _, out _));
    }

    [Fact]
    public void Correlation_reads_task_serial_and_vehicle_from_payload()
    {
        var payload = """{"task_serial":"EXT-MES-10086","vehicleId":"AMR-02"}""";

        Assert.Equal("EXT-MES-10086", LogCorrelation.TaskSerialOf(null, "FMS", "出站回调", "/api/v1/xinsong/task", payload));
        Assert.Equal("AMR-02", LogCorrelation.VehicleIdOf(null, "FMS", "出站回调", null, payload));
    }

    [Fact]
    public void Correlation_uses_trace_id_and_actor_when_payload_is_empty()
    {
        Assert.Equal("wfinstance01", LogCorrelation.TaskSerialOf("wfinstance01", "AMR-01", "工步完成", null, null));
        Assert.Equal("AMR-01", LogCorrelation.VehicleIdOf("wfinstance01", "AMR-01", "工步完成", null, null));
        Assert.Null(LogCorrelation.TaskSerialOf("0HN8ABC:00000001", "SYSTEM", "HTTP 200", "/api/logs", null));
    }
}
