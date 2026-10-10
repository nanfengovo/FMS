using Fms.Domain.Shared.Enums;

namespace Fms.Domain.Storage;

/// <summary>
/// 货位实体：具有确定状态机（Empty -> Reserved -> Occupied / Locked）的物理仓位
/// </summary>
public class StorageSlot
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    public StorageZoneType ZoneType { get; set; } = StorageZoneType.Raw;

    /// <summary>
    /// 绑定的地图停靠站点 ID（车停在哪个节点来取放料）
    /// </summary>
    public string BindNodeId { get; set; } = string.Empty;

    public StorageSlotStatus Status { get; set; } = StorageSlotStatus.Empty;
    public bool IsLocked => Status == StorageSlotStatus.Locked;
    public string? OccupiedCarrierId { get; set; } // 托盘/料盒/FOUP RFID 码
    public string? ReservedOrderId { get; set; }
    public string? ReservedVehicleId { get; set; }
    public string? LockReason { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public StorageSlot() { }

    public StorageSlot(string id, string name, string zoneId, StorageZoneType zoneType, string bindNodeId)
    {
        Id = id;
        Name = name;
        ZoneId = zoneId;
        ZoneType = zoneType;
        BindNodeId = bindNodeId;
    }

    public (bool Success, string Reason) ReserveForDrop(string orderId, string vehicleId)
    {
        if (Status == StorageSlotStatus.Locked)
            return (false, $"货位 {Id} 处于锁定状态，无法预约入库");
        if (Status == StorageSlotStatus.Occupied)
            return (false, $"货位 {Id} 已有货物 ({OccupiedCarrierId})，无法预约入库");
        if (Status == StorageSlotStatus.Reserved && ReservedOrderId != orderId)
            return (false, $"货位 {Id} 已被订单 {ReservedOrderId} 预约入库");

        Status = StorageSlotStatus.Reserved;
        ReservedOrderId = orderId;
        ReservedVehicleId = vehicleId;
        UpdatedAt = DateTime.UtcNow;
        return (true, "预约入库成功");
    }

    public (bool Success, string Reason) ReserveForPick(string orderId, string vehicleId, string? expectedCarrierId = null)
    {
        if (Status == StorageSlotStatus.Locked)
            return (false, $"货位 {Id} 处于锁定状态，无法预约出库");
        if (Status == StorageSlotStatus.Empty)
            return (false, $"货位 {Id} 为空，无可取物料");
        if (!string.IsNullOrEmpty(expectedCarrierId) && !string.IsNullOrEmpty(OccupiedCarrierId) &&
            !OccupiedCarrierId.Equals(expectedCarrierId, StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"货位 {Id} 物料码不匹配 (期望: {expectedCarrierId}, 实际: {OccupiedCarrierId})");
        }
        if (Status == StorageSlotStatus.Reserved && ReservedOrderId != orderId)
            return (false, $"货位 {Id} 已被订单 {ReservedOrderId} 预约出库");

        Status = StorageSlotStatus.Reserved;
        ReservedOrderId = orderId;
        ReservedVehicleId = vehicleId;
        UpdatedAt = DateTime.UtcNow;
        return (true, "预约出库成功");
    }

    public (bool Success, string Reason) ConfirmDrop(string carrierId)
    {
        Status = StorageSlotStatus.Occupied;
        OccupiedCarrierId = carrierId;
        ReservedOrderId = null;
        ReservedVehicleId = null;
        UpdatedAt = DateTime.UtcNow;
        return (true, "放货入库完成");
    }

    public (bool Success, string Reason) ConfirmPick()
    {
        Status = StorageSlotStatus.Empty;
        OccupiedCarrierId = null;
        ReservedOrderId = null;
        ReservedVehicleId = null;
        UpdatedAt = DateTime.UtcNow;
        return (true, "取货出库完成");
    }

    /// <summary>
    /// 订单被取消或失败时撤销它占用的预约：预约入库的空位恢复为空闲，预约出库的货位恢复为占用。
    /// 预约属于其他订单时不做任何改动。
    /// </summary>
    public bool ReleaseReservation(string orderId)
    {
        if (Status != StorageSlotStatus.Reserved || ReservedOrderId != orderId) return false;

        Status = string.IsNullOrEmpty(OccupiedCarrierId) ? StorageSlotStatus.Empty : StorageSlotStatus.Occupied;
        ReservedOrderId = null;
        ReservedVehicleId = null;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    public void Lock(string reason = "运维人工锁定")
    {
        Status = StorageSlotStatus.Locked;
        LockReason = reason;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Unlock()
    {
        if (Status == StorageSlotStatus.Locked)
        {
            Status = string.IsNullOrEmpty(OccupiedCarrierId) ? StorageSlotStatus.Empty : StorageSlotStatus.Occupied;
            LockReason = null;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
