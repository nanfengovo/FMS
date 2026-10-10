using System.Collections.Concurrent;
using Fms.Domain.Shared.Enums;

namespace Fms.Domain.Storage;

/// <summary>
/// 库区与货位管理器：对外提供货位分配、状态机推进与库区策略校验
/// </summary>
public class StorageManager
{
    private readonly ConcurrentDictionary<string, StorageZone> _zones = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, StorageSlot> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public StorageManager()
    {
        SeedStandardSlots();
    }

    public void RegisterZone(StorageZone zone)
    {
        _zones[zone.Id] = zone;
    }

    public void RegisterSlot(StorageSlot slot)
    {
        _slots[slot.Id] = slot;
        if (_zones.TryGetValue(slot.ZoneId, out var zone))
        {
            if (!zone.SlotIds.Contains(slot.Id))
            {
                zone.SlotIds.Add(slot.Id);
            }
        }
    }

    public StorageSlot? GetSlot(string slotId) => _slots.GetValueOrDefault(slotId);
    public StorageZone? GetZone(string zoneId) => _zones.GetValueOrDefault(zoneId);

    public IReadOnlyList<StorageSlot> GetAllSlots() => _slots.Values.ToList();
    public IReadOnlyList<StorageZone> GetAllZones() => _zones.Values.ToList();

    /// <summary>
    /// 为工单放料自动分配一个满足库区策略的空闲货位并完成预约
    /// </summary>
    public (bool Success, string Reason, StorageSlot? Slot) AllocateSlotForDrop(
        StorageZoneType zoneType,
        string orderId,
        string vehicleId)
    {
        lock (_lock)
        {
            var validZones = _zones.Values.Where(z => z.Type == zoneType && z.AllowInbound).ToList();
            if (validZones.Count == 0)
            {
                return (false, $"库区类型 {zoneType} 不存在或根据规则不允许入库 (例如原料区只出不进)", null);
            }

            foreach (var zone in validZones)
            {
                var candidateSlots = _slots.Values
                    .Where(s => s.ZoneId == zone.Id && s.Status == StorageSlotStatus.Empty)
                    .OrderBy(s => s.Id)
                    .ToList();

                foreach (var slot in candidateSlots)
                {
                    var (success, _) = slot.ReserveForDrop(orderId, vehicleId);
                    if (success)
                    {
                        return (true, $"成功在 {zone.Name} 预约货位 {slot.Id}", slot);
                    }
                }
            }

            return (false, $"库区类型 {zoneType} 当前无可用空闲货位", null);
        }
    }

    /// <summary>释放某个订单持有的全部货位预约，返回被释放的货位编号。</summary>
    public IReadOnlyList<string> ReleaseReservationsForOrder(string orderId)
    {
        lock (_lock)
        {
            var released = new List<string>();
            foreach (var slot in _slots.Values)
            {
                if (slot.ReleaseReservation(orderId)) released.Add(slot.Id);
            }
            return released;
        }
    }

    public (bool Success, string Reason) ExecuteDrop(string slotId, string carrierId)
    {
        lock (_lock)
        {
            if (!_slots.TryGetValue(slotId, out var slot))
                return (false, $"货位 {slotId} 不存在");

            if (_zones.TryGetValue(slot.ZoneId, out var zone) && !zone.AllowInbound)
                return (false, $"库区 {zone.Name} 设置为只出不进，禁止放货入库");

            return slot.ConfirmDrop(carrierId);
        }
    }

    public (bool Success, string Reason) ExecutePick(string slotId, string? expectedCarrierId = null)
    {
        lock (_lock)
        {
            if (!_slots.TryGetValue(slotId, out var slot))
                return (false, $"货位 {slotId} 不存在");

            if (_zones.TryGetValue(slot.ZoneId, out var zone) && !zone.AllowOutbound)
                return (false, $"库区 {zone.Name} 设置为只进不出，禁止取料出库");

            // 工业条码/RFID 严格比对：若指定了预期载具编号，必须与当前货位实际停放载具一致
            if (!string.IsNullOrEmpty(expectedCarrierId) && !string.IsNullOrEmpty(slot.OccupiedCarrierId))
            {
                if (!string.Equals(slot.OccupiedCarrierId.Trim(), expectedCarrierId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"货位 {slotId} 实际载具 [{slot.OccupiedCarrierId}] 与预期取货载具 [{expectedCarrierId}] 不匹配，安全互锁阻断取货");
                }
            }

            return slot.ConfirmPick();
        }
    }

    public void SeedStandardSlots()
    {
        // 1. 原料区 (RAW) - 只出不进
        RegisterZone(new StorageZone("ZONE_RAW", "原料立库区", StorageZoneType.Raw, 10));
        RegisterSlot(new StorageSlot { Id = "A-01", Name = "演示货位 A-01", ZoneId = "ZONE_RAW", ZoneType = StorageZoneType.Raw, BindNodeId = "STK-01", Status = StorageSlotStatus.Occupied, OccupiedCarrierId = "FOUP-8801" });
        RegisterSlot(new StorageSlot { Id = "A-02", Name = "演示货位 A-02", ZoneId = "ZONE_RAW", ZoneType = StorageZoneType.Raw, BindNodeId = "STK-01", Status = StorageSlotStatus.Occupied, OccupiedCarrierId = "FOUP-8802" });
        RegisterSlot(new StorageSlot { Id = "A-03", Name = "演示货位 A-03", ZoneId = "ZONE_RAW", ZoneType = StorageZoneType.Raw, BindNodeId = "STK-01", Status = StorageSlotStatus.Reserved, OccupiedCarrierId = "FOUP-8803", ReservedOrderId = "ORD-TEST-001" });
        RegisterSlot(new StorageSlot { Id = "A-04", Name = "演示货位 A-04", ZoneId = "ZONE_RAW", ZoneType = StorageZoneType.Raw, BindNodeId = "STK-01", Status = StorageSlotStatus.Empty });
        RegisterSlot(new StorageSlot { Id = "A-05", Name = "演示货位 A-05", ZoneId = "ZONE_RAW", ZoneType = StorageZoneType.Raw, BindNodeId = "STK-01", Status = StorageSlotStatus.Empty });
        RegisterSlot(new StorageSlot { Id = "A-06", Name = "演示货位 A-06", ZoneId = "ZONE_RAW", ZoneType = StorageZoneType.Raw, BindNodeId = "STK-01", Status = StorageSlotStatus.Locked, LockReason = "传感器光电检修" });

        // 2. 线边缓存区 (WIP) - 双向流转
        RegisterZone(new StorageZone("ZONE_WIP", "线边工序缓存区", StorageZoneType.Wip, 12));
        RegisterSlot(new StorageSlot { Id = "B-01", Name = "演示货位 B-01", ZoneId = "ZONE_WIP", ZoneType = StorageZoneType.Wip, BindNodeId = "WB-01", Status = StorageSlotStatus.Occupied, OccupiedCarrierId = "WIP-TRAY-101" });
        RegisterSlot(new StorageSlot { Id = "B-02", Name = "演示货位 B-02", ZoneId = "ZONE_WIP", ZoneType = StorageZoneType.Wip, BindNodeId = "WB-01", Status = StorageSlotStatus.Empty });
        RegisterSlot(new StorageSlot { Id = "B-03", Name = "演示货位 B-03", ZoneId = "ZONE_WIP", ZoneType = StorageZoneType.Wip, BindNodeId = "WB-02", Status = StorageSlotStatus.Reserved, OccupiedCarrierId = "WIP-TRAY-103", ReservedOrderId = "ORD-TEST-002" });
        RegisterSlot(new StorageSlot { Id = "B-04", Name = "演示货位 B-04", ZoneId = "ZONE_WIP", ZoneType = StorageZoneType.Wip, BindNodeId = "WB-02", Status = StorageSlotStatus.Empty });
        RegisterSlot(new StorageSlot { Id = "B-05", Name = "演示货位 B-05", ZoneId = "ZONE_WIP", ZoneType = StorageZoneType.Wip, BindNodeId = "WB-02", Status = StorageSlotStatus.Occupied, OccupiedCarrierId = "WIP-TRAY-105" });
        RegisterSlot(new StorageSlot { Id = "B-06", Name = "演示货位 B-06", ZoneId = "ZONE_WIP", ZoneType = StorageZoneType.Wip, BindNodeId = "WB-02", Status = StorageSlotStatus.Empty });

        // 3. 成品区 (FINISHED) - 只进不出
        RegisterZone(new StorageZone("ZONE_FIN", "成品入库区", StorageZoneType.Finished, 10));
        RegisterSlot(new StorageSlot { Id = "C-01", Name = "演示货位 C-01", ZoneId = "ZONE_FIN", ZoneType = StorageZoneType.Finished, BindNodeId = "JUNCT-R", Status = StorageSlotStatus.Empty });
        RegisterSlot(new StorageSlot { Id = "C-02", Name = "演示货位 C-02", ZoneId = "ZONE_FIN", ZoneType = StorageZoneType.Finished, BindNodeId = "JUNCT-R", Status = StorageSlotStatus.Occupied, OccupiedCarrierId = "FIN-BOX-501" });
        RegisterSlot(new StorageSlot { Id = "C-03", Name = "演示货位 C-03", ZoneId = "ZONE_FIN", ZoneType = StorageZoneType.Finished, BindNodeId = "JUNCT-R", Status = StorageSlotStatus.Occupied, OccupiedCarrierId = "FIN-BOX-502" });
        RegisterSlot(new StorageSlot { Id = "C-04", Name = "演示货位 C-04", ZoneId = "ZONE_FIN", ZoneType = StorageZoneType.Finished, BindNodeId = "JUNCT-R", Status = StorageSlotStatus.Empty });

        // 4. 充电桩区 (CHARGE)
        RegisterZone(new StorageZone("ZONE_CHG", "自动充电工位", StorageZoneType.Charge, 2));
        RegisterSlot(new StorageSlot { Id = "CH-01", Name = "演示充电货位 CH-01", ZoneId = "ZONE_CHG", ZoneType = StorageZoneType.Charge, BindNodeId = "CH-01", Status = StorageSlotStatus.Empty });
        RegisterSlot(new StorageSlot { Id = "CH-02", Name = "演示充电货位 CH-02", ZoneId = "ZONE_CHG", ZoneType = StorageZoneType.Charge, BindNodeId = "CH-01", Status = StorageSlotStatus.Empty });

        // 5. 待命区 (PARK)
        RegisterZone(new StorageZone("ZONE_PARK", "待命停靠区", StorageZoneType.Park, 5));
        RegisterSlot(new StorageSlot { Id = "P-01", Name = "演示待命货位 P-01", ZoneId = "ZONE_PARK", ZoneType = StorageZoneType.Park, BindNodeId = "PARK-01", Status = StorageSlotStatus.Empty });
        RegisterSlot(new StorageSlot { Id = "P-02", Name = "演示待命货位 P-02", ZoneId = "ZONE_PARK", ZoneType = StorageZoneType.Park, BindNodeId = "PARK-01", Status = StorageSlotStatus.Occupied, OccupiedCarrierId = "AMR-102 停靠" });
    }
}
