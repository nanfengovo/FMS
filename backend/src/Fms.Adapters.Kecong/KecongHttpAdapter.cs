using System.Net.Http.Json;
using Fms.Domain.Adapters;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Microsoft.Extensions.Logging;

namespace Fms.Adapters.Kecong;

public class KecongHttpAdapter : IVehicleCommAdapter
{
    public string AdapterType => "Kecong-HTTP";

    private readonly HttpClient _httpClient;
    private readonly ILogger<KecongHttpAdapter>? _logger;
    private readonly string _baseAddress;

    public event EventHandler<VehicleTelemetry>? TelemetryReceived;

    public KecongHttpAdapter(HttpClient httpClient, string baseAddress = "http://127.0.0.1:9000", ILogger<KecongHttpAdapter>? logger = null)
    {
        _httpClient = httpClient;
        _baseAddress = baseAddress.TrimEnd('/');
        _logger = logger;
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        _logger?.LogInformation("Kecong HTTP Adapter connected to {Address}", _baseAddress);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public async Task SendTrajectoryAsync(string vehicleId, List<Point2D> trajectoryPoints, double maxSpeed, CancellationToken ct = default)
    {
        // 向科聪/新松 FMS 下发导航点位
        var payload = new
        {
            AGV_serial = vehicleId,
            points = trajectoryPoints.Select(p => new { x = p.X, y = p.Y }).ToList(),
            max_speed = maxSpeed
        };

        try
        {
            var res = await _httpClient.PostAsJsonAsync($"{_baseAddress}/api/fms/trajectory", payload, ct);
            res.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send trajectory to Kecong vehicle {VehicleId}", vehicleId);
        }
    }

    public async Task CancelTaskAsync(string vehicleId, string taskId, CancellationToken ct = default)
    {
        var payload = new
        {
            AGV_serial = vehicleId,
            task_serial = taskId
        };
        try
        {
            await _httpClient.PostAsJsonAsync($"{_baseAddress}/api/v1/xinsong/task_delete", payload, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to cancel task {TaskId} on Kecong vehicle {VehicleId}", taskId, vehicleId);
        }
    }

    public async Task SendInstantActionAsync(string vehicleId, ActionType action, string optionCode, CancellationToken ct = default)
    {
        var payload = new
        {
            AGV_serial = vehicleId,
            action = action.ToString(),
            option_code = optionCode
        };
        try
        {
            await _httpClient.PostAsJsonAsync($"{_baseAddress}/api/fms/action", payload, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send instant action to Kecong vehicle {VehicleId}", vehicleId);
        }
    }
}
