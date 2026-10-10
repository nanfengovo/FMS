using System.Text.Json.Serialization;

namespace Fms.Application.Gateway;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GatewayProtocol
{
    ModbusTcp,
    Mqtt,
    OpcUaSim
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GatewayChannelStatus
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TagDirection
{
    ReadFromPlc,
    WriteToPlc,
    Bidirectional
}

public class GatewayTagMapping
{
    public string TagAddress { get; set; } = string.Empty; // e.g. "Coil:100", "HoldingRegister:40001", "DiscreteInput:10", "Topic:line1/status"
    public string SignalName { get; set; } = string.Empty; // e.g. "DOCK_READY", "LINE_CLEAR", "MATERIAL_ARRIVED"
    public TagDirection Direction { get; set; } = TagDirection.ReadFromPlc;
    public string LastValue { get; set; } = "0";
    public string Description { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
}

public class GatewayChannelConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public GatewayProtocol Protocol { get; set; } = GatewayProtocol.ModbusTcp;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 1502;
    public byte UnitId { get; set; } = 1;
    public int PollIntervalMs { get; set; } = 200;
    public bool IsEnabled { get; set; } = true;
    public GatewayChannelStatus Status { get; set; } = GatewayChannelStatus.Disconnected;
    public DateTime? LastSyncUtc { get; set; }
    public double LatencyMs { get; set; }
    public string? LastError { get; set; }
    public List<GatewayTagMapping> TagMappings { get; set; } = new();
}

public record WriteTagRequestDto(string ChannelId, string TagAddress, string Value);
public record CreateChannelDto(
    string? Id,
    string Name,
    GatewayProtocol Protocol,
    string Host,
    int Port,
    byte UnitId,
    int PollIntervalMs,
    bool IsEnabled,
    List<GatewayTagMapping>? TagMappings);
