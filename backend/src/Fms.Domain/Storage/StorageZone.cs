using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;

namespace Fms.Domain.Storage;

/// <summary>
/// 库区：管理特定工艺区域的容量策略与出入库流向规则
/// </summary>
public class StorageZone
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public StorageZoneType Type { get; set; } = StorageZoneType.Raw;
    public int Capacity { get; set; } = 10;

    /// <summary>
    /// 是否允许入库（原料区只出不进 AllowInbound=false）
    /// </summary>
    public bool AllowInbound { get; set; } = true;

    /// <summary>
    /// 是否允许出库（成品区只进不出 AllowOutbound=false）
    /// </summary>
    public bool AllowOutbound { get; set; } = true;

    public List<Point2D> Polygon { get; set; } = new();
    public List<string> SlotIds { get; set; } = new();

    public StorageZone() { }

    public StorageZone(string id, string name, StorageZoneType type, int capacity = 10)
    {
        Id = id;
        Name = name;
        Type = type;
        Capacity = capacity;

        // 根据库区类型默认配置工业出入策略
        switch (type)
        {
            case StorageZoneType.Raw:
                AllowInbound = false;
                AllowOutbound = true;
                break;
            case StorageZoneType.Finished:
                AllowInbound = true;
                AllowOutbound = false;
                break;
            case StorageZoneType.Wip:
            case StorageZoneType.Charge:
            case StorageZoneType.Park:
            default:
                AllowInbound = true;
                AllowOutbound = true;
                break;
        }
    }
}
