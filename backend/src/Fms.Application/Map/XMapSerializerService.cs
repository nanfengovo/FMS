using System.Globalization;
using System.Text;
using System.Xml;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Topology;

namespace Fms.Application.Map;

public class XMapSerializerService
{
    /// <summary>
    /// 将拓扑地图与点云按照科聪工业标准 XML 规范序列化回写为 .xmap 格式
    /// </summary>
    public string SerializeToXmapXml(
        TopologyGraph graph,
        IReadOnlyList<Point2D>? pointCloud = null,
        IReadOnlyList<AreaDto>? advancedAreas = null,
        double resolution = 0.02)
    {
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "    ",
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false
        };

        using (var writer = XmlWriter.Create(sb, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("MAP");

            // 1. Header 标签：元数据与空间物理包络
            writer.WriteStartElement("header");
            writer.WriteAttributeString("type", "2D_Nature");
            writer.WriteAttributeString("name", string.IsNullOrWhiteSpace(graph.Name) ? "DefaultMap" : graph.Name);
            writer.WriteAttributeString("resolution", resolution.ToString("F6", CultureInfo.InvariantCulture));
            writer.WriteAttributeString("reflector_diameter", "0.075000");
            writer.WriteAttributeString("colum", "0");
            writer.WriteAttributeString("row", "0");
            writer.WriteAttributeString("distance", "0.000000");
            writer.WriteAttributeString("map_line_speed", "0.000000");
            writer.WriteAttributeString("map_angle_speed", "0.000000");
            writer.WriteAttributeString("map_adjust_line_speed", "0.100000");
            writer.WriteAttributeString("map_adjust_angle_speed", "0.100000");
            writer.WriteAttributeString("map_version", graph.Version.ToString());
            writer.WriteAttributeString("longitude", "0.000000000000");
            writer.WriteAttributeString("latitude", "0.000000000000");
            writer.WriteAttributeString("compress", "0.000000000000");
            writer.WriteAttributeString("nav_plan_strategy", "0");
            writer.WriteAttributeString("laseravoiding_area_id", "");
            writer.WriteAttributeString("obs_enable", "");
            writer.WriteAttributeString("support_type", "");
            writer.WriteAttributeString("uuid", Guid.NewGuid().ToString());

            // min_pos / max_pos
            writer.WriteStartElement("min_pos");
            writer.WriteAttributeString("x", graph.MinX.ToString("F6", CultureInfo.InvariantCulture));
            writer.WriteAttributeString("y", graph.MinY.ToString("F6", CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            writer.WriteStartElement("max_pos");
            writer.WriteAttributeString("x", graph.MaxX.ToString("F6", CultureInfo.InvariantCulture));
            writer.WriteAttributeString("y", graph.MaxY.ToString("F6", CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            writer.WriteEndElement(); // header

            // 2. 激光点云 <point x="..." y="..." />
            if (pointCloud != null && pointCloud.Count > 0)
            {
                foreach (var pt in pointCloud)
                {
                    writer.WriteStartElement("point");
                    writer.WriteAttributeString("x", pt.X.ToString("F6", CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("y", pt.Y.ToString("F6", CultureInfo.InvariantCulture));
                    writer.WriteEndElement();
                }
            }

            // 3. 科聪高级站点 <advanced_point ...>
            foreach (var node in graph.Nodes)
            {
                writer.WriteStartElement("advanced_point");
                string className = node.ClassName;
                if (string.IsNullOrWhiteSpace(className))
                {
                    className = node.Type switch
                    {
                        NodeType.ChargePoint => "ChargePoint",
                        NodeType.StandbyPoint or NodeType.PullOverPoint => "ReturnPoint",
                        NodeType.JobPoint => "JobPoint",
                        _ => "LandMark"
                    };
                }

                double angleRad = node.HeadingDeg * Math.PI / 180.0;
                writer.WriteAttributeString("class_name", className);
                writer.WriteAttributeString("instance_name", string.IsNullOrWhiteSpace(node.Name) ? node.Id : node.Name);
                writer.WriteAttributeString("angle", angleRad.ToString("F6", CultureInfo.InvariantCulture));
                writer.WriteAttributeString("id", node.Id);
                writer.WriteAttributeString("laseravoiding_area_id", "0");
                writer.WriteAttributeString("obs_enable", "0");
                writer.WriteAttributeString("Gyro", "0");
                writer.WriteAttributeString("need_adjust", "0");
                writer.WriteAttributeString("adjust_type", "roll_back");
                writer.WriteAttributeString("navigation_laser", "1");
                writer.WriteAttributeString("navigation_mag", "0");
                writer.WriteAttributeString("navigation_qrcode", "0");
                writer.WriteAttributeString("navigation_gnss", "0");
                writer.WriteAttributeString("navigation_qrstrip", "0");
                writer.WriteAttributeString("allow_revolve", node.AllowRevolve ? "1" : "0");
                writer.WriteAttributeString("spin_direction", string.IsNullOrWhiteSpace(node.SpinDirection) ? "both" : node.SpinDirection);

                writer.WriteStartElement("original_path");
                writer.WriteEndElement();

                writer.WriteStartElement("pos");
                writer.WriteAttributeString("x", node.Position.X.ToString("F6", CultureInfo.InvariantCulture));
                writer.WriteAttributeString("y", node.Position.Y.ToString("F6", CultureInfo.InvariantCulture));
                writer.WriteEndElement();

                // 属性字典
                if (node.Properties != null)
                {
                    foreach (var (pKey, pVal) in node.Properties)
                    {
                        writer.WriteStartElement("property");
                        writer.WriteAttributeString("name", pKey);
                        writer.WriteAttributeString("enable", "0");
                        writer.WriteAttributeString("value", pVal);
                        writer.WriteEndElement();
                    }
                }

                writer.WriteEndElement(); // advanced_point
            }

            // 4. 科聪高级航道曲线 <advanced_curve ...>
            foreach (var path in graph.Paths)
            {
                writer.WriteStartElement("advanced_curve");
                bool isBezier = path is CubicBezierPath;
                var b = path as CubicBezierPath;
                var l = path as StraightLinePath;
                var startNode = graph.GetNode(path.FromNodeId);
                var endNode = graph.GetNode(path.ToNodeId);

                Point2D p0 = isBezier ? b!.P0 : (l?.Start ?? startNode?.Position ?? Point2D.Zero);
                Point2D p3 = isBezier ? b!.P3 : (l?.End ?? endNode?.Position ?? Point2D.Zero);
                Point2D c1 = isBezier ? b!.P1 : (path.Control1 ?? Point2D.Lerp(p0, p3, 0.333));
                Point2D c2 = isBezier ? b!.P2 : (path.Control2 ?? Point2D.Lerp(p0, p3, 0.667));

                string curveType = isBezier ? "bezier_curve" : (string.IsNullOrWhiteSpace(path.CurveType) ? "straight_line" : path.CurveType);
                string dir = string.IsNullOrWhiteSpace(path.Direction) ? "forward" : path.Direction;

                writer.WriteAttributeString("curve_type", curveType);
                writer.WriteAttributeString("line_velocity", path.MaxSpeed.ToString("F2", CultureInfo.InvariantCulture));
                writer.WriteAttributeString("RoadWidth", path.RoadWidth.ToString("F2", CultureInfo.InvariantCulture));
                writer.WriteAttributeString("Direction", dir);
                writer.WriteAttributeString("secondary_id", path.Id);
                writer.WriteAttributeString("id", path.Id);

                // start_pos
                writer.WriteStartElement("start_pos");
                writer.WriteAttributeString("id", path.FromNodeId);
                writer.WriteAttributeString("instance_name", startNode?.Name ?? path.FromNodeId);
                writer.WriteStartElement("pos");
                writer.WriteAttributeString("x", p0.X.ToString("F6", CultureInfo.InvariantCulture));
                writer.WriteAttributeString("y", p0.Y.ToString("F6", CultureInfo.InvariantCulture));
                writer.WriteEndElement();
                writer.WriteEndElement();

                // end_pos
                writer.WriteStartElement("end_pos");
                writer.WriteAttributeString("id", path.ToNodeId);
                writer.WriteAttributeString("instance_name", endNode?.Name ?? path.ToNodeId);
                writer.WriteStartElement("pos");
                writer.WriteAttributeString("x", p3.X.ToString("F6", CultureInfo.InvariantCulture));
                writer.WriteAttributeString("y", p3.Y.ToString("F6", CultureInfo.InvariantCulture));
                writer.WriteEndElement();
                writer.WriteEndElement();

                // control1_pos & control2_pos
                writer.WriteStartElement("control1_pos");
                writer.WriteAttributeString("x", c1.X.ToString("F6", CultureInfo.InvariantCulture));
                writer.WriteAttributeString("y", c1.Y.ToString("F6", CultureInfo.InvariantCulture));
                writer.WriteEndElement();

                writer.WriteStartElement("control2_pos");
                writer.WriteAttributeString("x", c2.X.ToString("F6", CultureInfo.InvariantCulture));
                writer.WriteAttributeString("y", c2.Y.ToString("F6", CultureInfo.InvariantCulture));
                writer.WriteEndElement();

                // properties
                if (path.Properties != null)
                {
                    foreach (var (pKey, pVal) in path.Properties)
                    {
                        writer.WriteStartElement("property");
                        writer.WriteAttributeString("name", pKey);
                        writer.WriteAttributeString("enable", "0");
                        writer.WriteAttributeString("value", pVal);
                        writer.WriteEndElement();
                    }
                }

                writer.WriteEndElement(); // advanced_curve
            }

            // 5. 科聪交管互斥区 <traffic_area ...>
            foreach (var zone in graph.Zones)
            {
                writer.WriteStartElement("traffic_area");
                writer.WriteAttributeString("instance_name", string.IsNullOrWhiteSpace(zone.Name) ? zone.Id : zone.Name);
                writer.WriteAttributeString("traffic_count", zone.MaxCapacity.ToString());
                writer.WriteAttributeString("id", zone.Id);
                writer.WriteAttributeString("goal_enable", "0");
                writer.WriteAttributeString("schedule_version", "");

                foreach (var pt in zone.Polygon)
                {
                    writer.WriteStartElement("posgroup");
                    writer.WriteAttributeString("x", pt.X.ToString("F6", CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("y", pt.Y.ToString("F6", CultureInfo.InvariantCulture));
                    writer.WriteEndElement();
                }

                writer.WriteEndElement(); // traffic_area
            }

            // 6. 自定义多边形区域 <advanced_area ...>
            if (advancedAreas != null)
            {
                foreach (var area in advancedAreas)
                {
                    writer.WriteStartElement("advanced_area");
                    writer.WriteAttributeString("id", area.Id);
                    writer.WriteAttributeString("instance_name", area.Name);
                    writer.WriteAttributeString("area_type", area.AreaType.ToString());

                    foreach (var pt in area.Polygon)
                    {
                        writer.WriteStartElement("posgroup");
                        writer.WriteAttributeString("x", pt.X.ToString("F6", CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y", pt.Y.ToString("F6", CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement(); // advanced_area
                }
            }

            writer.WriteEndElement(); // MAP
            writer.WriteEndDocument();
        }

        return sb.ToString();
    }
}
