using System.Globalization;
using System.Text.Json;
using System.Xml;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Topology;
using Microsoft.Extensions.Logging;

namespace Fms.Application.Map;

public class AreaDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int AreaType { get; set; }
    public string Category { get; set; } = "StorageZone"; // StorageZone, SpeedLimitZone, ForbiddenZone, MutexZone
    public List<Point2D> Polygon { get; set; } = new();
}

public class XmapParseResultDto
{
    public TopologyGraph Graph { get; set; } = new();
    public List<Point2D> PointCloud { get; set; } = new();
    public int TotalPointCloudCount { get; set; } = 0;
    public int DownsampleFactor { get; set; } = 25;
    public int PointCloudCount => PointCloud.Count;
    public double MinX { get; set; }
    public double MaxX { get; set; }
    public double MinY { get; set; }
    public double MaxY { get; set; }
    public double Resolution { get; set; } = 0.02;
    public string MapName { get; set; } = string.Empty;
    public List<AreaDto> AdvancedAreas { get; set; } = new();
}

public class XMapParserService
{
    private readonly ILogger<XMapParserService>? _logger;

    public XMapParserService(ILogger<XMapParserService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 流式高效解析科聪/工业 XMAP XML 拓扑与激光点云
    /// </summary>
    public async Task<XmapParseResultDto> ParseXmapXmlAsync(Stream stream, int pointCloudDownsampleFactor = 25)
    {
        var result = new XmapParseResultDto();
        result.PointCloud = new List<Point2D>(30000);

        var settings = new XmlReaderSettings
        {
            Async = true,
            IgnoreWhitespace = true,
            DtdProcessing = DtdProcessing.Ignore
        };

        using var bufferedStream = new BufferedStream(stream, 128 * 1024);
        using var reader = XmlReader.Create(bufferedStream, settings);

        int totalPointCounter = 0;

        while (await reader.ReadAsync())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;

            string elementName = reader.Name;

            // 1. Header 标签：获取地图元信息与空间物理包络
            if (elementName.Equals("header", StringComparison.OrdinalIgnoreCase))
            {
                result.MapName = reader.GetAttribute("name") ?? "XMap";
                result.Resolution = ParseDouble(reader.GetAttribute("resolution"), 0.02);
                result.Graph.Name = result.MapName;

                if (!reader.IsEmptyElement)
                {
                    using var subReader = reader.ReadSubtree();
                    while (await subReader.ReadAsync())
                    {
                        if (subReader.NodeType != XmlNodeType.Element) continue;
                        if (subReader.Name.Equals("min_pos", StringComparison.OrdinalIgnoreCase))
                        {
                            result.MinX = ParseDouble(subReader.GetAttribute("x"), 0);
                            result.MinY = ParseDouble(subReader.GetAttribute("y"), 0);
                        }
                        else if (subReader.Name.Equals("max_pos", StringComparison.OrdinalIgnoreCase))
                        {
                            result.MaxX = ParseDouble(subReader.GetAttribute("x"), 100);
                            result.MaxY = ParseDouble(subReader.GetAttribute("y"), 100);
                        }
                    }
                }

                result.Graph.MinX = result.MinX;
                result.Graph.MaxX = result.MaxX;
                result.Graph.MinY = result.MinY;
                result.Graph.MaxY = result.MaxY;
            }
            // 2. 激光点云 <point x="..." y="..." />（无 ID 纯 SLAM 背景激光散点）
            else if (elementName.Equals("point", StringComparison.OrdinalIgnoreCase) && reader.GetAttribute("id") == null && reader.GetAttribute("ID") == null)
            {
                totalPointCounter++;
                if (pointCloudDownsampleFactor <= 1 || totalPointCounter == 1 || totalPointCounter % pointCloudDownsampleFactor == 0)
                {
                    string? xStr = reader.GetAttribute("x") ?? reader.GetAttribute("X");
                    string? yStr = reader.GetAttribute("y") ?? reader.GetAttribute("Y");
                    if (double.TryParse(xStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double px) &&
                        double.TryParse(yStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double py))
                    {
                        result.PointCloud.Add(new Point2D(px, py));
                    }
                }
            }
            // 3. 科聪高级拓扑站点 <advanced_point ...>
            else if (elementName.Equals("advanced_point", StringComparison.OrdinalIgnoreCase))
            {
                string id = reader.GetAttribute("id") ?? reader.GetAttribute("instance_name") ?? Guid.NewGuid().ToString("N");
                string instanceName = reader.GetAttribute("instance_name") ?? id;
                string className = reader.GetAttribute("class_name") ?? "LandMark";
                double angle = ParseDouble(reader.GetAttribute("angle"), 0.0);
                bool allowRevolve = reader.GetAttribute("allow_revolve") == "1";
                string spinDir = reader.GetAttribute("spin_direction") ?? "both";

                double posX = 0.0;
                double posY = 0.0;
                var properties = new Dictionary<string, string>();

                if (!reader.IsEmptyElement)
                {
                    using var subReader = reader.ReadSubtree();
                    while (await subReader.ReadAsync())
                    {
                        if (subReader.NodeType != XmlNodeType.Element) continue;

                        if (subReader.Name.Equals("pos", StringComparison.OrdinalIgnoreCase))
                        {
                            posX = ParseDouble(subReader.GetAttribute("x"), posX);
                            posY = ParseDouble(subReader.GetAttribute("y"), posY);
                        }
                        else if (subReader.Name.Equals("property", StringComparison.OrdinalIgnoreCase))
                        {
                            string? pName = subReader.GetAttribute("name");
                            string? pVal = subReader.GetAttribute("value");
                            if (!string.IsNullOrEmpty(pName)) properties[pName] = pVal ?? "";
                        }
                    }
                }

                var nodeType = NodeType.Landmark;
                if (className.Contains("Charge", StringComparison.OrdinalIgnoreCase)) nodeType = NodeType.ChargePoint;
                else if (className.Contains("Return", StringComparison.OrdinalIgnoreCase)) nodeType = NodeType.StandbyPoint;
                else if (className.Contains("Job", StringComparison.OrdinalIgnoreCase)) nodeType = NodeType.JobPoint;

                var node = new MapNode(id, new Point2D(posX, posY), nodeType, instanceName)
                {
                    HeadingDeg = Math.Round(angle * 180.0 / Math.PI, 2),
                    AllowRevolve = allowRevolve,
                    SpinDirection = spinDir,
                    ClassName = className,
                    Properties = properties
                };

                result.Graph.AddNode(node);
            }
            // 4. 科聪高级航道曲线 <advanced_curve ...>
            else if (elementName.Equals("advanced_curve", StringComparison.OrdinalIgnoreCase))
            {
                string? secId = reader.GetAttribute("secondary_id");
                string? baseId = reader.GetAttribute("id");
                string id = !string.IsNullOrWhiteSpace(secId) ? secId : (!string.IsNullOrWhiteSpace(baseId) ? baseId : Guid.NewGuid().ToString("N"));
                string curveType = reader.GetAttribute("curve_type") ?? "straight_line";
                string direction = reader.GetAttribute("Direction") ?? "forward";
                double lineVelocity = ParseDouble(reader.GetAttribute("line_velocity"), 1.2);
                double roadWidth = ParseDouble(reader.GetAttribute("RoadWidth"), 1.2);

                string? startNodeId = null;
                string? endNodeId = null;
                Point2D? startPos = null;
                Point2D? endPos = null;
                Point2D? c1 = null;
                Point2D? c2 = null;
                var properties = new Dictionary<string, string>();

                if (!reader.IsEmptyElement)
                {
                    using var subReader = reader.ReadSubtree();
                    while (await subReader.ReadAsync())
                    {
                        if (subReader.NodeType != XmlNodeType.Element) continue;

                        if (subReader.Name.Equals("start_pos", StringComparison.OrdinalIgnoreCase))
                        {
                            startNodeId = subReader.GetAttribute("id") ?? subReader.GetAttribute("instance_name");
                        }
                        else if (subReader.Name.Equals("end_pos", StringComparison.OrdinalIgnoreCase))
                        {
                            endNodeId = subReader.GetAttribute("id") ?? subReader.GetAttribute("instance_name");
                        }
                        else if (subReader.Name.Equals("pos", StringComparison.OrdinalIgnoreCase))
                        {
                            double px = ParseDouble(subReader.GetAttribute("x"), 0);
                            double py = ParseDouble(subReader.GetAttribute("y"), 0);
                            if (startPos == null) startPos = new Point2D(px, py);
                            else if (endPos == null) endPos = new Point2D(px, py);
                        }
                        else if (subReader.Name.Equals("control1_pos", StringComparison.OrdinalIgnoreCase))
                        {
                            c1 = new Point2D(ParseDouble(subReader.GetAttribute("x"), 0), ParseDouble(subReader.GetAttribute("y"), 0));
                        }
                        else if (subReader.Name.Equals("control2_pos", StringComparison.OrdinalIgnoreCase))
                        {
                            c2 = new Point2D(ParseDouble(subReader.GetAttribute("x"), 0), ParseDouble(subReader.GetAttribute("y"), 0));
                        }
                        else if (subReader.Name.Equals("property", StringComparison.OrdinalIgnoreCase))
                        {
                            string? pName = subReader.GetAttribute("name");
                            string? pVal = subReader.GetAttribute("value");
                            if (!string.IsNullOrEmpty(pName)) properties[pName] = pVal ?? "";
                        }
                    }
                }

                if (!string.IsNullOrEmpty(startNodeId) && !string.IsNullOrEmpty(endNodeId))
                {
                    var fromNode = result.Graph.GetNode(startNodeId) ?? (startPos.HasValue ? new MapNode(startNodeId, startPos.Value) : null);
                    var toNode = result.Graph.GetNode(endNodeId) ?? (endPos.HasValue ? new MapNode(endNodeId, endPos.Value) : null);

                    if (fromNode != null && toNode != null)
                    {
                        if (result.Graph.GetNode(startNodeId) == null) result.Graph.AddNode(fromNode);
                        if (result.Graph.GetNode(endNodeId) == null) result.Graph.AddNode(toNode);

                        MapPath path;
                        if (curveType.Equals("bezier_curve", StringComparison.OrdinalIgnoreCase) && c1.HasValue && c2.HasValue)
                        {
                            path = new CubicBezierPath(id, startNodeId, endNodeId,
                                fromNode.Position, c1.Value, c2.Value, toNode.Position,
                                bidirectional: false, maxSpeed: lineVelocity);
                        }
                        else
                        {
                            path = new StraightLinePath(id, startNodeId, endNodeId,
                                fromNode.Position, toNode.Position,
                                bidirectional: false, maxSpeed: lineVelocity);
                        }

                        path.Direction = direction;
                        path.RoadWidth = roadWidth;
                        path.CurveType = curveType;
                        path.Control1 = c1;
                        path.Control2 = c2;
                        path.Properties = properties;

                        result.Graph.AddPath(path);
                    }
                }
            }
            // 5. 交通互斥交管区 <traffic_area ...>
            else if (elementName.Equals("traffic_area", StringComparison.OrdinalIgnoreCase))
            {
                string id = reader.GetAttribute("id") ?? reader.GetAttribute("instance_name") ?? Guid.NewGuid().ToString("N");
                string instanceName = reader.GetAttribute("instance_name") ?? id;
                int trafficCount = int.TryParse(reader.GetAttribute("traffic_count"), out int tc) && tc > 0 ? tc : 1;

                var polygon = new List<Point2D>();

                if (!reader.IsEmptyElement)
                {
                    using var subReader = reader.ReadSubtree();
                    while (await subReader.ReadAsync())
                    {
                        if (subReader.NodeType == XmlNodeType.Element &&
                            (subReader.Name.Equals("posgroup", StringComparison.OrdinalIgnoreCase) ||
                             subReader.Name.Equals("pos", StringComparison.OrdinalIgnoreCase)))
                        {
                            string? attrX = subReader.GetAttribute("x");
                            string? attrY = subReader.GetAttribute("y");
                            if (attrX != null && attrY != null)
                            {
                                double gx = ParseDouble(attrX, 0);
                                double gy = ParseDouble(attrY, 0);
                                polygon.Add(new Point2D(gx, gy));
                            }
                        }
                    }
                }

                if (polygon.Count >= 3)
                {
                    var zone = new MutexZone
                    {
                        Id = id,
                        Name = $"交管互斥区-{instanceName}",
                        MaxCapacity = trafficCount,
                        TimeoutSec = 45.0,
                        Polygon = polygon
                    };
                    result.Graph.AddZone(zone);
                }
            }
            // 6. 高级多边形区域 <advanced_area ...>
            else if (elementName.Equals("advanced_area", StringComparison.OrdinalIgnoreCase))
            {
                string id = reader.GetAttribute("id") ?? reader.GetAttribute("instance_name") ?? Guid.NewGuid().ToString("N");
                string instanceName = reader.GetAttribute("instance_name") ?? id;
                int areaType = int.TryParse(reader.GetAttribute("area_type"), out int at) ? at : 0;

                var polygon = new List<Point2D>();
                if (!reader.IsEmptyElement)
                {
                    using var subReader = reader.ReadSubtree();
                    while (await subReader.ReadAsync())
                    {
                        if (subReader.NodeType == XmlNodeType.Element && subReader.Name.Equals("posgroup", StringComparison.OrdinalIgnoreCase))
                        {
                            double gx = ParseDouble(subReader.GetAttribute("x"), 0);
                            double gy = ParseDouble(subReader.GetAttribute("y"), 0);
                            polygon.Add(new Point2D(gx, gy));
                        }
                    }
                }

                string category = "StorageZone";
                if (areaType == 2 || instanceName.Contains("限速")) category = "SpeedLimitZone";
                else if (areaType == 3 || instanceName.Contains("禁行")) category = "ForbiddenZone";
                else if (areaType == 4 || instanceName.Contains("交管") || instanceName.Contains("防冲")) category = "MutexZone";

                result.AdvancedAreas.Add(new AreaDto
                {
                    Id = id,
                    Name = instanceName,
                    AreaType = areaType,
                    Category = category,
                    Polygon = polygon
                });
            }
            // 7. 兼容传统带 ID 的 <Point id="..." x="..." y="..." />
            else if (elementName.Equals("Point", StringComparison.OrdinalIgnoreCase))
            {
                string? id = reader.GetAttribute("id") ?? reader.GetAttribute("ID");
                string? xStr = reader.GetAttribute("x") ?? reader.GetAttribute("X");
                string? yStr = reader.GetAttribute("y") ?? reader.GetAttribute("Y");
                string? typeStr = reader.GetAttribute("type") ?? reader.GetAttribute("Type");

                if (!string.IsNullOrEmpty(id) && double.TryParse(xStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                                             && double.TryParse(yStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                {
                    var nodeType = NodeType.Landmark;
                    if (typeStr != null)
                    {
                        if (typeStr.Contains("Job", StringComparison.OrdinalIgnoreCase)) nodeType = NodeType.JobPoint;
                        else if (typeStr.Contains("Charge", StringComparison.OrdinalIgnoreCase)) nodeType = NodeType.ChargePoint;
                        else if (typeStr.Contains("Spur", StringComparison.OrdinalIgnoreCase) || typeStr.Contains("Pull", StringComparison.OrdinalIgnoreCase)) nodeType = NodeType.PullOverPoint;
                    }

                    result.Graph.AddNode(new MapNode(id, new Point2D(x, y), nodeType));
                }
            }
            // 8. 兼容传统 <Path id="..." from="..." to="..." />
            else if (elementName.Equals("Path", StringComparison.OrdinalIgnoreCase) || elementName.Equals("Edge", StringComparison.OrdinalIgnoreCase))
            {
                string? id = reader.GetAttribute("id") ?? reader.GetAttribute("ID");
                string? fromId = reader.GetAttribute("from") ?? reader.GetAttribute("Point1");
                string? toId = reader.GetAttribute("to") ?? reader.GetAttribute("Point2");
                string? typeStr = reader.GetAttribute("type") ?? reader.GetAttribute("Type");

                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(fromId) && !string.IsNullOrEmpty(toId))
                {
                    var fromNode = result.Graph.GetNode(fromId);
                    var toNode = result.Graph.GetNode(toId);

                    if (fromNode != null && toNode != null)
                    {
                        if (typeStr != null && typeStr.Contains("bezier", StringComparison.OrdinalIgnoreCase))
                        {
                            double c1x = ParseDouble(reader.GetAttribute("c1x") ?? reader.GetAttribute("Control1_x"), fromNode.Position.X);
                            double c1y = ParseDouble(reader.GetAttribute("c1y") ?? reader.GetAttribute("Control1_y"), fromNode.Position.Y);
                            double c2x = ParseDouble(reader.GetAttribute("c2x") ?? reader.GetAttribute("Control2_x"), toNode.Position.X);
                            double c2y = ParseDouble(reader.GetAttribute("c2y") ?? reader.GetAttribute("Control2_y"), toNode.Position.Y);

                            var bezier = new CubicBezierPath(id, fromId, toId,
                                fromNode.Position, new Point2D(c1x, c1y), new Point2D(c2x, c2y), toNode.Position);
                            result.Graph.AddPath(bezier);
                        }
                        else
                        {
                            var line = new StraightLinePath(id, fromId, toId, fromNode.Position, toNode.Position);
                            result.Graph.AddPath(line);
                        }
                    }
                }
            }
        }

        result.TotalPointCloudCount = totalPointCounter;

        // 9. 自动关联互斥交管区与经过的航道
        AssociatePathsToZones(result.Graph);

        return result;
    }

    /// <summary>
    /// 从本地文件流式解析 XMAP
    /// </summary>
    public async Task<XmapParseResultDto> ParseXmapFileAsync(string filePath, int pointCloudDownsampleFactor = 25)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"XMap file not found: {filePath}");
        }

        using var fileStream = File.OpenRead(filePath);
        return await ParseXmapXmlAsync(fileStream, pointCloudDownsampleFactor);
    }

    private static void AssociatePathsToZones(TopologyGraph graph)
    {
        foreach (var zone in graph.Zones)
        {
            if (zone.Polygon == null || zone.Polygon.Count < 3) continue;

            foreach (var path in graph.Paths)
            {
                var startNode = graph.GetNode(path.FromNodeId);
                var endNode = graph.GetNode(path.ToNodeId);

                bool intersects = false;
                if (startNode != null && zone.ContainsPoint(startNode.Position)) intersects = true;
                else if (endNode != null && zone.ContainsPoint(endNode.Position)) intersects = true;
                else
                {
                    // 检查路径中点
                    var mid = path.Evaluate(0.5);
                    if (zone.ContainsPoint(mid)) intersects = true;
                }

                if (intersects)
                {
                    zone.AssociatedPathIds.Add(path.Id);
                }
            }
        }
    }

    /// <summary>
    /// 解析 SIASUNARV / 新松 mapinfo.json 格式地图
    /// </summary>
    public XmapParseResultDto ParseMapinfoJson(string jsonContent)
    {
        var result = new XmapParseResultDto();
        using var doc = JsonDocument.Parse(jsonContent);
        var root = doc.RootElement;

        if (root.TryGetProperty("Point", out var pointArray) && pointArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in pointArray.EnumerateArray())
            {
                string id = item.GetProperty("ID").ToString();
                double x = item.GetProperty("X").GetDouble();
                double y = item.GetProperty("Y").GetDouble();
                string typeStr = item.TryGetProperty("Type", out var t) ? t.GetString() ?? "" : "";

                var nodeType = NodeType.Landmark;
                if (typeStr.Contains("Job", StringComparison.OrdinalIgnoreCase)) nodeType = NodeType.JobPoint;
                else if (typeStr.Contains("Charge", StringComparison.OrdinalIgnoreCase)) nodeType = NodeType.ChargePoint;
                else if (typeStr.Contains("Return", StringComparison.OrdinalIgnoreCase)) nodeType = NodeType.StandbyPoint;

                result.Graph.AddNode(new MapNode(id, new Point2D(x, y), nodeType));
            }
        }

        if (root.TryGetProperty("Path", out var pathArray) && pathArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in pathArray.EnumerateArray())
            {
                string id = item.GetProperty("ID").ToString();
                string typeStr = item.TryGetProperty("Type", out var pt) ? pt.GetString() ?? "" : "line";

                var p1Obj = item.GetProperty("Point1");
                var p2Obj = item.GetProperty("Point2");

                string p1Id = p1Obj.GetProperty("ID").ToString();
                string p2Id = p2Obj.GetProperty("ID").ToString();

                var n1 = result.Graph.GetNode(p1Id) ?? new MapNode(p1Id, new Point2D(p1Obj.GetProperty("X").GetDouble(), p1Obj.GetProperty("Y").GetDouble()));
                var n2 = result.Graph.GetNode(p2Id) ?? new MapNode(p2Id, new Point2D(p2Obj.GetProperty("X").GetDouble(), p2Obj.GetProperty("Y").GetDouble()));

                result.Graph.AddNode(n1);
                result.Graph.AddNode(n2);

                if (typeStr.Contains("bezier", StringComparison.OrdinalIgnoreCase))
                {
                    double c1x = item.GetProperty("Control1_x").GetDouble();
                    double c1y = item.GetProperty("Control1_y").GetDouble();
                    double c2x = item.GetProperty("Control2_x").GetDouble();
                    double c2y = item.GetProperty("Control2_y").GetDouble();

                    var bezier = new CubicBezierPath(id, p1Id, p2Id, n1.Position, new Point2D(c1x, c1y), new Point2D(c2x, c2y), n2.Position);
                    result.Graph.AddPath(bezier);
                }
                else
                {
                    var line = new StraightLinePath(id, p1Id, p2Id, n1.Position, n2.Position);
                    result.Graph.AddPath(line);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 解析工作台 JSON：stations/lanes、current-map nodes/paths，或新松 Point/Path。
    /// </summary>
    public XmapParseResultDto ParseEditorJson(string jsonContent)
    {
        using var doc = JsonDocument.Parse(jsonContent);
        var root = doc.RootElement;
        if (root.TryGetProperty("Point", out var pointArray) && pointArray.ValueKind == JsonValueKind.Array)
        {
            return ParseMapinfoJson(jsonContent);
        }

        var result = new XmapParseResultDto
        {
            MapName = ReadString(root, "name") ?? "Imported-Map"
        };

        JsonElement stations = default;
        bool hasStations = (root.TryGetProperty("stations", out stations) || root.TryGetProperty("nodes", out stations))
                           && stations.ValueKind == JsonValueKind.Array;
        if (hasStations)
        {
            foreach (var item in stations.EnumerateArray())
            {
                string id = ReadString(item, "id") ?? "";
                if (string.IsNullOrWhiteSpace(id)) continue;
                double x = ReadDouble(item, "x");
                double y = ReadDouble(item, "y");
                string typeStr = ReadString(item, "type") ?? "Landmark";
                var nodeType = ParseNodeType(typeStr);
                string? name = ReadString(item, "name");
                var node = new MapNode(id, new Point2D(x, y), nodeType, name)
                {
                    AllowRevolve = item.TryGetProperty("allowRevolve", out var ar) && ar.ValueKind == JsonValueKind.True,
                    SpinDirection = ReadString(item, "spinDirection") ?? "both",
                    ClassName = ReadString(item, "className") ?? "LandMark",
                    HeadingDeg = ReadDouble(item, "headingDeg")
                };
                result.Graph.AddNode(node);
            }
        }

        JsonElement lanes = default;
        bool hasLanes = (root.TryGetProperty("lanes", out lanes) || root.TryGetProperty("paths", out lanes))
                        && lanes.ValueKind == JsonValueKind.Array;
        if (hasLanes)
        {
            foreach (var item in lanes.EnumerateArray())
            {
                string id = ReadString(item, "id") ?? $"P-{result.Graph.Paths.Count + 1}";
                string fromId = ReadString(item, "from") ?? ReadString(item, "fromNodeId") ?? ReadString(item, "startNodeId") ?? "";
                string toId = ReadString(item, "to") ?? ReadString(item, "toNodeId") ?? ReadString(item, "endNodeId") ?? "";
                if (string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(toId)) continue;

                var n1 = result.Graph.GetNode(fromId);
                var n2 = result.Graph.GetNode(toId);
                if (n1 == null || n2 == null) continue;

                string typeStr = ReadString(item, "type") ?? ReadString(item, "pathType") ?? ReadString(item, "curveType") ?? "line";
                bool bidirectional = !item.TryGetProperty("bidirectional", out var bid) || bid.ValueKind != JsonValueKind.False;
                if (typeStr.Contains("bezier", StringComparison.OrdinalIgnoreCase) || typeStr.Contains("Cubic", StringComparison.OrdinalIgnoreCase))
                {
                    var c1 = item.TryGetProperty("p1", out var p1) ? new Point2D(ReadDouble(p1, "x"), ReadDouble(p1, "y")) : n1.Position;
                    var c2 = item.TryGetProperty("p2", out var p2) ? new Point2D(ReadDouble(p2, "x"), ReadDouble(p2, "y")) : n2.Position;
                    result.Graph.AddPath(new CubicBezierPath(id, fromId, toId, n1.Position, c1, c2, n2.Position, bidirectional));
                }
                else
                {
                    result.Graph.AddPath(new StraightLinePath(id, fromId, toId, n1.Position, n2.Position, bidirectional));
                }
            }
        }

        if (root.TryGetProperty("bounds", out var bounds) && bounds.ValueKind == JsonValueKind.Object)
        {
            result.MinX = ReadDouble(bounds, "minX");
            result.MaxX = ReadDouble(bounds, "maxX");
            result.MinY = ReadDouble(bounds, "minY");
            result.MaxY = ReadDouble(bounds, "maxY");
        }
        else if (result.Graph.Nodes.Count > 0)
        {
            result.MinX = result.Graph.Nodes.Min(n => n.Position.X);
            result.MaxX = result.Graph.Nodes.Max(n => n.Position.X);
            result.MinY = result.Graph.Nodes.Min(n => n.Position.Y);
            result.MaxY = result.Graph.Nodes.Max(n => n.Position.Y);
        }

        return result;
    }

    private static NodeType ParseNodeType(string typeStr)
    {
        if (Enum.TryParse<NodeType>(typeStr, true, out var parsed)) return parsed;
        if (typeStr.Contains("Charge", StringComparison.OrdinalIgnoreCase)) return NodeType.ChargePoint;
        if (typeStr.Contains("Job", StringComparison.OrdinalIgnoreCase) || typeStr.Contains("Pick", StringComparison.OrdinalIgnoreCase) || typeStr.Contains("Drop", StringComparison.OrdinalIgnoreCase))
            return NodeType.JobPoint;
        if (typeStr.Contains("Standby", StringComparison.OrdinalIgnoreCase) || typeStr.Contains("Return", StringComparison.OrdinalIgnoreCase) || typeStr.Contains("Park", StringComparison.OrdinalIgnoreCase))
            return NodeType.StandbyPoint;
        if (typeStr.Contains("Pull", StringComparison.OrdinalIgnoreCase) || typeStr.Contains("Spur", StringComparison.OrdinalIgnoreCase))
            return NodeType.PullOverPoint;
        return NodeType.Landmark;
    }

    private static string? ReadString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
    }

    private static double ReadDouble(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) return p;
        return 0;
    }

    private static double ParseDouble(string? value, double fallback)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double val)) return val;
        return fallback;
    }
}
