using System.IO;

namespace Fms.Domain.Topology.Parsers;

/// <summary>
/// 统一的地图解析器抽象，支持解析 XMAP、DXF、CAD 或私有 JSON 格式
/// </summary>
public interface IMapParser
{
    string SupportedFormat { get; }
    
    TopologyGraph Parse(Stream fileStream);
}
