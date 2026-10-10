using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Fms.Domain.Adapters;
using Fms.Domain.Logging;
using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Vehicles;
using Microsoft.Extensions.Logging;

namespace Fms.Application.Rcs;

public interface IRcsCallbackService
{
    Task NotifyTaskStartedAsync(string taskSerial, string agvSerial, string taskType = "");
    Task NotifyTaskArrivedTargetAsync(string taskSerial, string agvSerial, string targetNodeId, string taskType = "");
    Task<bool> RequestActionPermissionAsync(string taskSerial, string agvSerial, string optionCode, string taskType = "");
    Task<bool> VerifyCargoRfidAsync(string taskSerial, string agvSerial, string cargoId, string taskType = "");
    Task NotifyTaskFinishedAsync(TransportOrderAggregate order, VehicleAggregate vehicle, int finishType = 1);
    Task<bool> RequestTaskDeleteFromWebAsync(string taskSerial, string agvSerial, string taskType = "");
    Task ReportRobotStatusAsync(VehicleAggregate vehicle);
}

public class RcsCallbackService : IRcsCallbackService
{
    private readonly HttpClient _httpClient;
    private readonly RcsConfigurationService _config;
    private readonly ILogger<RcsCallbackService>? _logger;
    private readonly ILogSink? _sink;

    public RcsCallbackService(HttpClient httpClient, RcsConfigurationService config, ILogger<RcsCallbackService>? logger = null, ILogSink? sink = null)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
        _sink = sink;
    }

    /// <summary>
    /// 3. 上报任务开始执行 (POST /api/v1/xinsong/task_info)
    /// </summary>
    public async Task NotifyTaskStartedAsync(string taskSerial, string agvSerial, string taskType = "")
    {
        string url = _config.GetEndpointUrl("task_info", taskType);
        var payload = new { AGV_serial = agvSerial, task_serial = taskSerial };
        try
        {
            await SendOutboundAsync("task_info", url, payload);
            _logger?.LogInformation("[RCS-3] Reported task start: {Task} to {Url}", taskSerial, url);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[RCS-3] Failed to report task start to RCS at {Url}", url);
        }
    }

    /// <summary>
    /// 4. 上报车辆到达目的地 (POST /api/v1/xinsong/task_arrive_target)
    /// </summary>
    public async Task NotifyTaskArrivedTargetAsync(string taskSerial, string agvSerial, string targetNodeId, string taskType = "")
    {
        string url = _config.GetEndpointUrl("task_arrive_target", taskType);
        var payload = new { AGV_serial = agvSerial, task_serial = taskSerial, target = targetNodeId };
        try
        {
            await SendOutboundAsync("task_arrive_target", url, payload);
            _logger?.LogInformation("[RCS-4] Reported arrival at {Target} for task {Task} to {Url}", targetNodeId, taskSerial, url);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[RCS-4] Failed to report arrival to RCS at {Url}", url);
        }
    }

    /// <summary>
    /// 5. 请求设备动作放行 (POST /api/v1/xinsong/robot_permiss_start_action)
    /// </summary>
    public async Task<bool> RequestActionPermissionAsync(string taskSerial, string agvSerial, string optionCode, string taskType = "")
    {
        string url = _config.GetEndpointUrl("robot_permiss_start_action", taskType);
        var payload = new { AGV_serial = agvSerial, task_serial = taskSerial, option_code = optionCode };
        try
        {
            var res = await SendOutboundAsync("robot_permiss_start_action", url, payload);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadFromJsonAsync<RcsResponseDto>();
                _logger?.LogInformation("[RCS-5] Action permission response: {Result} from {Url}", body?.Result, url);
                return body?.Result ?? false;
            }
        }
        catch (Exception ex)
        {
            if (_config.ActiveProfile.AutoPermitInSimulation)
            {
                _logger?.LogInformation("[RCS-SIM] Auto-granted action permission for {Task} (Active Profile: {Profile}, Simulation Sandbox Active)", taskSerial, _config.ActiveProfile.Name);
                return true;
            }
            _logger?.LogWarning(ex, "[RCS-5] Failed to request action permission from RCS at {Url}", url);
        }

        if (_config.ActiveProfile.AutoPermitInSimulation)
        {
            _logger?.LogInformation("[RCS-SIM] Auto-granted action permission fallback for {Task}", taskSerial);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 6. 物料 RFID 校验 (POST /api/v1/xinsong/check_cargo_rfid)
    /// </summary>
    public async Task<bool> VerifyCargoRfidAsync(string taskSerial, string agvSerial, string cargoId, string taskType = "")
    {
        string url = _config.GetEndpointUrl("check_cargo_rfid", taskType);
        var payload = new { AGV_serial = agvSerial, task_serial = taskSerial, cargo_id = cargoId };
        try
        {
            var res = await SendOutboundAsync("check_cargo_rfid", url, payload);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadFromJsonAsync<RcsRfidResponseDto>();
                bool success = body?.Result == true && body.Data?.RfidResult == "3"; // 3 成功, 4 失败
                _logger?.LogInformation("[RCS-6] RFID verify response: {Success} (ResultCode={Code}) from {Url}", success, body?.Data?.RfidResult, url);
                return success;
            }
        }
        catch (Exception ex)
        {
            if (_config.ActiveProfile.AutoRfidVerifiedInSimulation)
            {
                _logger?.LogInformation("[RCS-SIM] Auto-verified cargo RFID {Cargo} for {Task} (Active Profile: {Profile})", cargoId, taskSerial, _config.ActiveProfile.Name);
                return true;
            }
            _logger?.LogWarning(ex, "[RCS-6] Failed to verify cargo RFID from RCS at {Url}", url);
        }

        if (_config.ActiveProfile.AutoRfidVerifiedInSimulation)
        {
            _logger?.LogInformation("[RCS-SIM] Auto-verified cargo RFID {Cargo} fallback for {Task}", cargoId, taskSerial);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 7. 上报任务完成 (POST /api/v1/xinsong/task_finish)
    /// </summary>
    public async Task NotifyTaskFinishedAsync(TransportOrderAggregate order, VehicleAggregate vehicle, int finishType = 1)
    {
        string url = _config.GetEndpointUrl("task_finish", order.TaskType);
        var payload = new
        {
            AGV_serial = int.TryParse(vehicle.Id, out int vId) ? vId : 1,
            cargo_action_time_cost = (int)order.TotalCargoActionTimeCostSec,
            finish_type = finishType,
            odom_meter = (int)order.TotalOdomMeter,
            self_rotate = 0,
            task_serial = order.OrderSerial,
            waiting_time_cost = (int)order.TotalWaitingTimeCostSec,
            walk_time_cost = (int)order.TotalWalkTimeCostSec
        };

        try
        {
            await SendOutboundAsync("task_finish", url, payload);
            _logger?.LogInformation("[RCS-7] Reported task finish for {Task} to {Url}", order.OrderSerial, url);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[RCS-7] Failed to report task finish to RCS at {Url}", url);
        }
    }

    /// <summary>
    /// 9. 调度网页操作删除任务 (POST /api/v1/xinsong/task_delete)
    /// </summary>
    public async Task<bool> RequestTaskDeleteFromWebAsync(string taskSerial, string agvSerial, string taskType = "")
    {
        string url = _config.GetEndpointUrl("task_delete", taskType);
        var payload = new
        {
            task_serial = taskSerial,
            AGV_serial = agvSerial
        };

        try
        {
            var res = await SendOutboundAsync("task_delete", url, payload);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadFromJsonAsync<RcsResponseDto>();
                _logger?.LogInformation("[RCS-9] Web delete task request approved: {Result} from {Url}", body?.Result, url);
                return body?.Result ?? false;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[RCS-9] Failed to send web delete task request to RCS at {Url}", url);
        }
        return false;
    }

    /// <summary>
    /// 10. 上报车辆状态 (以 1s 周期向上位系统上报，配置于 kcfms.xml)
    /// </summary>
    public async Task ReportRobotStatusAsync(VehicleAggregate vehicle)
    {
        if (!_config.PushEnabled) return;

        string url = _config.GetStatusReportUrl();

        int statusInt = vehicle.Status switch
        {
            VehicleStatus.Idle => 0,
            VehicleStatus.Running => 1,
            VehicleStatus.Offline => 2,
            VehicleStatus.Charging => 6,
            VehicleStatus.Fault => 5,
            _ => 1
        };

        var payload = new Dictionary<string, object?>
        {
            ["AGV_serial"] = vehicle.Id,
            ["task_serial"] = vehicle.AssignedOrderId ?? "",
            ["source"] = "",
            ["target"] = vehicle.TargetNodeId ?? "",
            ["x_position"] = vehicle.Pose.X.ToString("F6"),
            ["y_position"] = vehicle.Pose.Y.ToString("F6"),
            ["theta"] = vehicle.Pose.ThetaRad.ToString("F6"),
            ["position_id"] = vehicle.CurrentNodeId ?? "1",
            ["line_velocity"] = vehicle.CurrentSpeed.ToString("F6"),
            ["angle_velocity"] = vehicle.CurrentAngularVelocity.ToString("F6"),
            ["option_code"] = "",
            ["stopped_type"] = 0.0,
            ["status"] = statusInt.ToString(),
            ["error_codes"] = vehicle.IsEstop ? new[] { "ESTOP" } : Array.Empty<string>(),
            ["error_forbidden"] = "",
            ["lift_status"] = vehicle.HoldingPayload ? "1" : "0",
            ["wait_state"] = vehicle.CurrentAvoidanceStrategy == AvoidanceStrategy.WaitInPlace ? "WAIT_LOCK" : "",
            ["AGVPosStatus"] = "0",
            ["ARMAction"] = "1",
            ["ARMActionOK"] = "0",
            ["ARVTask"] = "0",
            ["Battery"] = (vehicle.Bms.SocPercent / 100.0).ToString("F4"),
            ["BatteryLowThreshold"] = "0.2200",
            ["OccupiedVehicle"] = "",
            ["TrafficError"] = "",
            ["battery_ID1"] = "0",
            ["battery_ID2"] = "0",
            ["battery_bms"] = "0",
            ["battery_capacity"] = "880",
            ["battery_chargeA"] = "260",
            ["battery_chargeV"] = "576",
            ["battery_command"] = "0",
            ["battery_current"] = "17",
            ["battery_cycle"] = "11",
            ["battery_health"] = "99",
            ["battery_soc"] = ((int)vehicle.Bms.SocPercent).ToString(),
            ["battery_voltage"] = "530",
            ["battery_temperature"] = "250",
            ["battery_info"] = new Dictionary<string, object?>
            {
                ["ID"] = new[] { "0", "0" },
                ["bms"] = "0",
                ["capacity"] = "880",
                ["chargeA"] = "260",
                ["chargeV"] = "576",
                ["current"] = "17",
                ["cycle"] = "11",
                ["soc"] = ((int)vehicle.Bms.SocPercent).ToString(),
                ["temperature"] = "250",
                ["voltage"] = "530"
            }
        };

        try
        {
            var response = await SendOutboundAsync("robot_status", url, payload, logSuccess: false);
            if (response.IsSuccessStatusCode)
            {
                _config.RecordStatusPush();
            }
        }
        catch
        {
            // 忽略推送失败以防刷屏
        }
    }

    private async Task<HttpResponseMessage> SendOutboundAsync(string action, string url, object payload, bool logSuccess = true)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, payload);
            if (logSuccess || !response.IsSuccessStatusCode)
            {
                _sink?.Enqueue(new LogWrite
                {
                    Category = LogCategory.Outbound,
                    Level = response.IsSuccessStatusCode ? "Information" : "Warning",
                    Source = "RcsCallback",
                    Actor = "FMS",
                    Message = $"{action} {url}",
                    Method = "POST",
                    Path = url,
                    StatusCode = (int)response.StatusCode,
                    DurationMs = watch.ElapsedMilliseconds,
                    Success = response.IsSuccessStatusCode,
                    PayloadJson = JsonSerializer.Serialize(new { action })
                });
            }

            return response;
        }
        catch (Exception ex)
        {
            _sink?.Enqueue(new LogWrite
            {
                Category = LogCategory.Outbound,
                Level = "Error",
                Source = "RcsCallback",
                Actor = "FMS",
                Message = $"{action} 失败: {ex.Message}",
                Method = "POST",
                Path = url,
                DurationMs = watch.ElapsedMilliseconds,
                Success = false
            });
            throw;
        }
    }

    private class RcsResponseDto
    {
        public bool Result { get; set; }
        public string ErrMsg { get; set; } = string.Empty;
        public string? Delete_flag { get; set; }
    }

    private class RcsRfidResponseDto
    {
        public bool Result { get; set; }
        public string ErrMsg { get; set; } = string.Empty;
        public RfidData? Data { get; set; }
    }

    private class RfidData
    {
        public string RfidResult { get; set; } = string.Empty; // 3-成功, 4-失败
    }
}
