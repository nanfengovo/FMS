using System.Text.Json;
using Fms.Domain.Adapters;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Microsoft.Extensions.Logging;
using MQTTnet;

namespace Fms.Adapters.Vda5050;

public class Vda5050MqttAdapter : IVehicleCommAdapter
{
    public string AdapterType => "VDA-5050-MQTT";

    private readonly IMqttClient _mqttClient;
    private readonly MqttClientOptions _options;
    private readonly ILogger<Vda5050MqttAdapter>? _logger;
    private readonly string _manufacturer = "VDA_AMR";

    public event EventHandler<VehicleTelemetry>? TelemetryReceived;

    public Vda5050MqttAdapter(string brokerHost = "127.0.0.1", int brokerPort = 1883, ILogger<Vda5050MqttAdapter>? logger = null)
    {
        _logger = logger;
        var factory = new MqttClientFactory();
        _mqttClient = factory.CreateMqttClient();
        _options = new MqttClientOptionsBuilder()
            .WithTcpServer(brokerHost, brokerPort)
            .WithClientId($"FmsDispatch_{Guid.NewGuid():N}")
            .Build();

        _mqttClient.ApplicationMessageReceivedAsync += HandleIncomingMqttMessageAsync;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        try
        {
            if (!_mqttClient.IsConnected)
            {
                await _mqttClient.ConnectAsync(_options, ct);
                // 订阅所有车辆的状态上报: uagv/v2/+/+/state
                await _mqttClient.SubscribeAsync("uagv/v2/+/+/state", cancellationToken: ct);
                _logger?.LogInformation("VDA 5050 MQTT Adapter connected and subscribed.");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to connect to MQTT broker, running in offline mode.");
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_mqttClient.IsConnected)
        {
            await _mqttClient.DisconnectAsync(cancellationToken: ct);
        }
    }

    public async Task SendTrajectoryAsync(string vehicleId, List<Point2D> trajectoryPoints, double maxSpeed, CancellationToken ct = default)
    {
        if (!_mqttClient.IsConnected) return;

        // 构造标准 VDA 5050 Order 消息
        var orderMessage = new
        {
            headerId = 1,
            timestamp = DateTime.UtcNow.ToString("O"),
            version = "2.0.0",
            manufacturer = _manufacturer,
            serialNumber = vehicleId,
            orderId = $"ORD-{Guid.NewGuid():N}",
            orderUpdateId = 0,
            nodes = trajectoryPoints.Select((pt, idx) => new
            {
                nodeId = $"node_{idx}",
                sequenceId = idx * 2,
                released = true,
                nodePosition = new { x = pt.X, y = pt.Y, mapId = "default" }
            }).ToList()
        };

        var topic = $"uagv/v2/{_manufacturer}/{vehicleId}/order";
        var payload = JsonSerializer.Serialize(orderMessage);

        var msg = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .Build();

        await _mqttClient.PublishAsync(msg, ct);
    }

    public async Task CancelTaskAsync(string vehicleId, string taskId, CancellationToken ct = default)
    {
        if (!_mqttClient.IsConnected) return;

        var instantAction = new
        {
            headerId = 2,
            timestamp = DateTime.UtcNow.ToString("O"),
            version = "2.0.0",
            manufacturer = _manufacturer,
            serialNumber = vehicleId,
            actions = new[]
            {
                new { actionType = "cancelOrder", actionId = $"act_{Guid.NewGuid():N}" }
            }
        };

        var topic = $"uagv/v2/{_manufacturer}/{vehicleId}/instantActions";
        var msg = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(JsonSerializer.Serialize(instantAction))
            .Build();

        await _mqttClient.PublishAsync(msg, ct);
    }

    public async Task SendInstantActionAsync(string vehicleId, ActionType action, string optionCode, CancellationToken ct = default)
    {
        if (!_mqttClient.IsConnected) return;

        var instantAction = new
        {
            headerId = 3,
            timestamp = DateTime.UtcNow.ToString("O"),
            version = "2.0.0",
            manufacturer = _manufacturer,
            serialNumber = vehicleId,
            actions = new[]
            {
                new { actionType = action.ToString(), actionId = $"act_{Guid.NewGuid():N}", actionParameters = new[] { optionCode } }
            }
        };

        var topic = $"uagv/v2/{_manufacturer}/{vehicleId}/instantActions";
        var msg = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(JsonSerializer.Serialize(instantAction))
            .Build();

        await _mqttClient.PublishAsync(msg, ct);
    }

    private Task HandleIncomingMqttMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        // 解析来自车辆的 VDA 5050 state 消息
        try
        {
            string json = e.ApplicationMessage.ConvertPayloadToString() ?? "";
            if (string.IsNullOrEmpty(json)) return Task.CompletedTask;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string vehicleId = root.GetProperty("serialNumber").GetString() ?? "";
            var posElem = root.GetProperty("agvPosition");
            double x = posElem.GetProperty("x").GetDouble();
            double y = posElem.GetProperty("y").GetDouble();
            double theta = posElem.GetProperty("theta").GetDouble();

            double battery = 90.0;
            if (root.TryGetProperty("batteryState", out var batElem) && batElem.TryGetProperty("batteryCharge", out var chargeElem))
            {
                battery = chargeElem.GetDouble();
            }

            var telemetry = new VehicleTelemetry(
                vehicleId,
                new Pose2D(x, y, theta),
                0.0,
                0.0,
                battery,
                VehicleStatus.Running,
                null,
                null,
                new(),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );

            TelemetryReceived?.Invoke(this, telemetry);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to parse incoming VDA 5050 state message.");
        }

        return Task.CompletedTask;
    }
}
