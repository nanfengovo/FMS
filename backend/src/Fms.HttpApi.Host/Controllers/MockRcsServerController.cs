using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fms.HttpApi.Host.Controllers;

[AllowAnonymous]
[ApiController]
public class MockRcsServerController : ControllerBase
{
    private readonly ILogger<MockRcsServerController> _logger;

    public MockRcsServerController(ILogger<MockRcsServerController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 3. 上报任务开始执行 (POST /api/v1/xinsong/task_info 或 /{type}/api/v1/xinsong/task_info)
    /// </summary>
    [HttpPost("api/v1/xinsong/task_info")]
    [HttpPost("{type}/api/v1/xinsong/task_info")]
    public IActionResult MockTaskInfo([FromBody] JsonObject payload, [FromRoute] string? type = null)
    {
        _logger.LogInformation("[MockRCS] Received task_info (Type={Type}): {Json}", type, payload.ToJsonString());
        return Ok(new
        {
            ErrMsg = "",
            Result = true
        });
    }

    /// <summary>
    /// 4. 上报车辆到达目的地 (POST /api/v1/xinsong/task_arrive_target 或 /{type}/api/v1/xinsong/task_arrive_target)
    /// </summary>
    [HttpPost("api/v1/xinsong/task_arrive_target")]
    [HttpPost("{type}/api/v1/xinsong/task_arrive_target")]
    public IActionResult MockTaskArriveTarget([FromBody] JsonObject payload, [FromRoute] string? type = null)
    {
        _logger.LogInformation("[MockRCS] Received task_arrive_target (Type={Type}): {Json}", type, payload.ToJsonString());
        return Ok(new
        {
            ErrMsg = "",
            Result = true
        });
    }

    /// <summary>
    /// 5. 请求设备动作放行 (POST /api/v1/xinsong/robot_permiss_start_action 或 /{type}/api/v1/xinsong/robot_permiss_start_action)
    /// </summary>
    [HttpPost("api/v1/xinsong/robot_permiss_start_action")]
    [HttpPost("{type}/api/v1/xinsong/robot_permiss_start_action")]
    public IActionResult MockPermissStartAction([FromBody] JsonObject payload, [FromRoute] string? type = null)
    {
        _logger.LogInformation("[MockRCS] Received robot_permiss_start_action (Type={Type}): {Json}", type, payload.ToJsonString());
        string optionCode = payload["option_code"]?.ToString() ?? "0,0";
        string taskSerial = payload["task_serial"]?.ToString() ?? "";

        return Ok(new
        {
            ErrMsg = "",
            Result = true,
            data = new
            {
                option_code = optionCode,
                task_serial = taskSerial
            }
        });
    }

    /// <summary>
    /// 6. 物料 RFID 校验 (POST /api/v1/xinsong/check_cargo_rfid 或 /{type}/api/v1/xinsong/check_cargo_rfid)
    /// </summary>
    [HttpPost("api/v1/xinsong/check_cargo_rfid")]
    [HttpPost("{type}/api/v1/xinsong/check_cargo_rfid")]
    public IActionResult MockCheckCargoRfid([FromBody] JsonObject payload, [FromRoute] string? type = null)
    {
        _logger.LogInformation("[MockRCS] Received check_cargo_rfid (Type={Type}): {Json}", type, payload.ToJsonString());
        return Ok(new
        {
            ErrMsg = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.ffffff"),
            Result = true,
            data = new
            {
                rfid_result = "3" // 3 成功，4-失败
            }
        });
    }

    /// <summary>
    /// 7. 上报任务完成 (POST /api/v1/xinsong/task_finish 或 /{type}/api/v1/xinsong/task_finish)
    /// </summary>
    [HttpPost("api/v1/xinsong/task_finish")]
    [HttpPost("{type}/api/v1/xinsong/task_finish")]
    public IActionResult MockTaskFinish([FromBody] JsonObject payload, [FromRoute] string? type = null)
    {
        _logger.LogInformation("[MockRCS] Received task_finish (Type={Type}): {Json}", type, payload.ToJsonString());
        return Ok(new
        {
            ErrMsg = "",
            Result = true
        });
    }

    /// <summary>
    /// 9. 调度网页操作删除任务 (POST /api/v1/xinsong/task_delete 或 /{type}/api/v1/xinsong/task_delete)
    /// </summary>
    [HttpPost("api/v1/xinsong/task_delete_from_web")]
    [HttpPost("{type}/api/v1/xinsong/task_delete_from_web")]
    public IActionResult MockTaskDeleteFromWeb([FromBody] JsonObject payload, [FromRoute] string? type = null)
    {
        _logger.LogInformation("[MockRCS] Received task_delete request from web (Type={Type}): {Json}", type, payload.ToJsonString());
        return Ok(new
        {
            ErrMsg = "",
            Result = true // true 允许删除, false 不允许删除
        });
    }

    /// <summary>
    /// 10. 上报车辆状态 (POST /api/v1/xinsong/report_robot_status)
    /// </summary>
    [HttpPost("api/v1/xinsong/report_robot_status")]
    public IActionResult MockReportRobotStatus([FromBody] JsonObject payload)
    {
        // 降低日志级别以防刷屏
        return Ok(new
        {
            ErrMsg = "",
            Result = true
        });
    }
}
