using Fms.Domain.Identity;
using Fms.HttpApi.Host.Authorization;
using Fms.Application;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fms.HttpApi.Host.Controllers;

public class UpdateSlotStatusRequest
{
    public string Status { get; set; } = "Empty"; // Empty, Reserved, Occupied, Locked
    public string? CarrierId { get; set; }
    public string? LockReason { get; set; }
}

public class LockSlotRequest
{
    public string? Reason { get; set; } = "人工指令锁定";
}

[ApiController]
[Route("api/fms/storage")]
[Authorize]
public class StorageController : ControllerBase
{
    private readonly FmsAppService _appService;

    public StorageController(FmsAppService appService)
    {
        _appService = appService;
    }

    /// <summary>
    /// 获取所有库区定义及其出入库准入策略
    /// </summary>
    [RequiresPermission(FmsPermissions.Storage)]
    [HttpGet("zones")]
    public IActionResult GetZones()
    {
        var zones = _appService.Engine.StorageManager.GetAllZones();
        return Ok(new
        {
            code = "0000",
            msg = "获取成功",
            data = zones.Select(z => new
            {
                id = z.Id,
                name = z.Name,
                type = z.Type.ToString(),
                capacity = z.Capacity,
                allowInbound = z.AllowInbound,
                allowOutbound = z.AllowOutbound,
                polygon = z.Polygon,
                slotCount = z.SlotIds.Count,
                slotIds = z.SlotIds
            })
        });
    }

    /// <summary>
    /// 获取所有货位状态与物料载体信息（支持库区与状态过滤）
    /// </summary>
    [RequiresPermission(FmsPermissions.Storage)]
    [HttpGet("slots")]
    public IActionResult GetSlots([FromQuery] string? zoneId, [FromQuery] string? status)
    {
        var slots = _appService.Engine.StorageManager.GetAllSlots();

        if (!string.IsNullOrEmpty(zoneId))
        {
            slots = slots.Where(s => s.ZoneId.Equals(zoneId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<StorageSlotStatus>(status, true, out var parsedStatus))
        {
            slots = slots.Where(s => s.Status == parsedStatus).ToList();
        }

        return Ok(new
        {
            code = "0000",
            msg = "获取成功",
            data = slots.Select(s => new
            {
                id = s.Id,
                name = s.Name,
                zoneId = s.ZoneId,
                zoneType = s.ZoneType.ToString(),
                bindNodeId = s.BindNodeId,
                status = s.Status.ToString(),
                occupiedCarrierId = s.OccupiedCarrierId,
                reservedOrderId = s.ReservedOrderId,
                reservedVehicleId = s.ReservedVehicleId,
                lockReason = s.LockReason,
                updatedAt = s.UpdatedAt
            })
        });
    }

    /// <summary>
    /// 修改货位业务状态与载具编号
    /// </summary>
    [RequiresPermission(FmsPermissions.StorageAllocate)]
    [HttpPost("slots/{id}/status")]
    public async Task<IActionResult> UpdateSlotStatus(string id, [FromBody] UpdateSlotStatusRequest req)
    {
        var slot = _appService.Engine.StorageManager.GetSlot(id);
        if (slot == null)
        {
            return NotFound(new { code = "404", msg = $"货位 {id} 不存在" });
        }

        if (!Enum.TryParse<StorageSlotStatus>(req.Status, true, out var targetStatus))
        {
            return BadRequest(new { code = "400", msg = $"无效的货位状态: {req.Status}" });
        }

        switch (targetStatus)
        {
            case StorageSlotStatus.Locked:
                slot.Lock(req.LockReason ?? "人工调度指令锁定");
                break;

            case StorageSlotStatus.Empty:
                if (slot.Status == StorageSlotStatus.Locked)
                {
                    slot.Unlock();
                }
                slot.Status = StorageSlotStatus.Empty;
                slot.OccupiedCarrierId = null;
                slot.ReservedOrderId = null;
                slot.ReservedVehicleId = null;
                slot.UpdatedAt = DateTime.UtcNow;
                break;

            case StorageSlotStatus.Occupied:
                if (slot.Status == StorageSlotStatus.Locked)
                {
                    slot.Unlock();
                }
                slot.Status = StorageSlotStatus.Occupied;
                slot.OccupiedCarrierId = string.IsNullOrWhiteSpace(req.CarrierId) ? "MANUAL-CARRIER" : req.CarrierId;
                slot.ReservedOrderId = null;
                slot.ReservedVehicleId = null;
                slot.UpdatedAt = DateTime.UtcNow;
                break;

            case StorageSlotStatus.Reserved:
                slot.Status = StorageSlotStatus.Reserved;
                slot.ReservedOrderId = req.CarrierId ?? "MANUAL-RESERVE";
                slot.UpdatedAt = DateTime.UtcNow;
                break;
        }

        await _appService.SaveCurrentMapAsync();

        return Ok(new
        {
            code = "0000",
            msg = "货位状态更新成功",
            data = new
            {
                id = slot.Id,
                status = slot.Status.ToString(),
                occupiedCarrierId = slot.OccupiedCarrierId,
                lockReason = slot.LockReason
            }
        });
    }

    /// <summary>
    /// 锁定货位
    /// <summary>
    /// 锁定货位
    /// </summary>
    [RequiresPermission(FmsPermissions.StorageLock)]
    [HttpPost("slots/{id}/lock")]
    public async Task<IActionResult> LockSlot(string id, [FromBody] LockSlotRequest req)
    {
        var slot = _appService.Engine.StorageManager.GetSlot(id);
        if (slot == null) return NotFound(new { code = "404", msg = $"货位 {id} 不存在" });

        slot.Lock(req.Reason ?? "人工指令锁定");
        await _appService.SaveCurrentMapAsync();
        return Ok(new { code = "0000", msg = "锁定成功", data = new { id = slot.Id, status = slot.Status.ToString(), lockReason = slot.LockReason } });
    }

    /// <summary>
    /// 解锁货位
    /// </summary>
    [RequiresPermission(FmsPermissions.StorageLock)]
    [HttpPost("slots/{id}/unlock")]
    public async Task<IActionResult> UnlockSlot(string id)
    {
        var slot = _appService.Engine.StorageManager.GetSlot(id);
        if (slot == null) return NotFound(new { code = "404", msg = $"货位 {id} 不存在" });

        slot.Unlock();
        await _appService.SaveCurrentMapAsync();
        return Ok(new { code = "0000", msg = "解锁成功", data = new { id = slot.Id, status = slot.Status.ToString() } });
    }
}
