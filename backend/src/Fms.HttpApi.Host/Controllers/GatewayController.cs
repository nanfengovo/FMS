using Fms.Domain.Identity;
using Fms.HttpApi.Host.Authorization;
using Fms.Application.Gateway;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fms.HttpApi.Host.Controllers;

[Authorize]
[ApiController]
[Route("api/fms/gateway")]
public class GatewayController : ControllerBase
{
    private readonly IIndustrialGatewayService _gatewayService;

    public GatewayController(IIndustrialGatewayService gatewayService)
    {
        _gatewayService = gatewayService;
    }

    [RequiresPermission(FmsPermissions.Simulation)]
    [HttpGet("channels")]
    public IActionResult GetChannels()
    {
        var channels = _gatewayService.GetChannels();
        return Ok(new { success = true, data = channels });
    }

    [RequiresPermission(FmsPermissions.Simulation)]
    [HttpGet("channels/{id}")]
    public IActionResult GetChannel(string id)
    {
        var channel = _gatewayService.GetChannel(id);
        if (channel == null) return NotFound(new { error = $"Channel '{id}' not found" });
        return Ok(new { success = true, data = channel });
    }

    [RequiresPermission(FmsPermissions.SignalsWrite)]
    [HttpPost("channels")]
    public async Task<IActionResult> SaveChannel([FromBody] CreateChannelDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequest(new { error = "Channel name cannot be empty" });
        }
        var channel = await _gatewayService.SaveChannelAsync(dto);
        return Ok(new { success = true, data = channel });
    }

    [RequiresPermission(FmsPermissions.SignalsWrite)]
    [HttpDelete("channels/{id}")]
    public IActionResult DeleteChannel(string id)
    {
        bool deleted = _gatewayService.DeleteChannel(id);
        if (!deleted) return NotFound(new { error = $"Channel '{id}' not found" });
        return Ok(new { success = true, message = "Channel deleted successfully" });
    }

    [RequiresPermission(FmsPermissions.SignalsWrite)]
    [HttpPost("channels/{id}/connect")]
    public async Task<IActionResult> ConnectChannel(string id)
    {
        await _gatewayService.ConnectChannelAsync(id);
        var ch = _gatewayService.GetChannel(id);
        return Ok(new { success = true, status = ch?.Status.ToString(), latencyMs = ch?.LatencyMs, error = ch?.LastError });
    }

    [RequiresPermission(FmsPermissions.SignalsWrite)]
    [HttpPost("channels/{id}/disconnect")]
    public IActionResult DisconnectChannel(string id)
    {
        _gatewayService.DisconnectChannel(id);
        return Ok(new { success = true, message = "Channel disconnected" });
    }

    [RequiresPermission(FmsPermissions.SignalsWrite)]
    [HttpPost("tags/write")]
    public async Task<IActionResult> WriteTag([FromBody] WriteTagRequestDto dto)
    {
        bool success = await _gatewayService.WriteTagAsync(dto.ChannelId, dto.TagAddress, dto.Value);
        if (!success) return BadRequest(new { error = "Failed to write tag" });
        return Ok(new { success = true, tagAddress = dto.TagAddress, value = dto.Value });
    }

    [RequiresPermission(FmsPermissions.SignalsWrite)]
    [HttpPost("simulated-server/set-coil")]
    public IActionResult SetSimulatedCoil([FromBody] SetSimulatedCoilDto dto)
    {
        _gatewayService.SimulatedServer.SetCoil(dto.Address, dto.Value);
        return Ok(new { success = true, address = dto.Address, value = dto.Value });
    }

    [RequiresPermission(FmsPermissions.Simulation)]
    [HttpGet("simulated-server/status")]
    public IActionResult GetSimulatedServerStatus()
    {
        var s = _gatewayService.SimulatedServer;
        return Ok(new
        {
            isRunning = s.IsRunning,
            port = s.Port,
            dockReadyCoil100 = s.GetCoil(100),
            lineClearCoil101 = s.GetCoil(101),
            materialArrivedCoil102 = s.GetCoil(102)
        });
    }

    public record SetSimulatedCoilDto(ushort Address, bool Value);
}
