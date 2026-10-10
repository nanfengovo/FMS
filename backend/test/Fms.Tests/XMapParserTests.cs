using System.IO;
using System.Text;
using Fms.Application.Map;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Topology;
using Xunit;

namespace Fms.Tests;

public class XMapParserTests
{
    private const string SampleMapinfoJson = @"{
       ""ErrMsg"" : """",
       ""Max_x"" : 170,
       ""Max_y"" : 170,
       ""Min_x"" : 0,
       ""Min_y"" : 0,
       ""Point"" : [
          { ""ID"" : 1, ""Type"" : ""JobPoint"", ""X"" : 10, ""Y"" : 20.5 },
          { ""ID"" : 2, ""Type"" : ""JobPoint"", ""X"" : 19, ""Y"" : 16 },
          { ""ID"" : 3, ""Type"" : ""ReturnPoint"", ""X"" : 0, ""Y"" : 0 }
       ],
       ""Path"" : [
          {
             ""ID"" : 1,
             ""Type"" : ""bezier_curve"",
             ""Control1_x"" : 11,
             ""Control1_y"" : 19,
             ""Control2_x"" : 11,
             ""Control2_y"" : 17.5,
             ""Point1"" : { ""ID"" : 1, ""Type"" : ""JobPoint"", ""X"" : 10, ""Y"" : 20.5 },
             ""Point2"" : { ""ID"" : 2, ""Type"" : ""JobPoint"", ""X"" : 19, ""Y"" : 16 }
          },
          {
             ""ID"" : 2,
             ""Type"" : ""line"",
             ""Point1"" : { ""ID"" : 2, ""Type"" : ""JobPoint"", ""X"" : 19, ""Y"" : 16 },
             ""Point2"" : { ""ID"" : 3, ""Type"" : ""ReturnPoint"", ""X"" : 0, ""Y"" : 0 }
          }
       ]
    }";

    private const string SampleKecongXmap = @"<MAP>
    <header type=""2D_Nature"" name=""双差速轮---横移功能模型"" resolution=""0.020000"">
        <min_pos x=""-12.56"" y=""-85.78""></min_pos>
        <max_pos x=""275.90"" y=""26.01""></max_pos>
    </header>
    <point x=""10.0"" y=""20.0""></point>
    <point x=""10.5"" y=""20.5""></point>
    <advanced_point class_name=""LandMark"" instance_name=""2067"" angle=""3.141592"" id=""2067"" allow_revolve=""1"" spin_direction=""both"">
        <pos x=""105.165"" y=""-77.350""></pos>
    </advanced_point>
    <advanced_point class_name=""ChargePoint"" instance_name=""CH-01"" angle=""0.0"" id=""CH-01"" allow_revolve=""0"" spin_direction=""cw"">
        <pos x=""25.0"" y=""20.0""></pos>
    </advanced_point>
    <advanced_curve id=""2003"" secondary_id=""2003-1"" curve_type=""bezier_curve"" RoadWidth=""1.2"" Direction=""forward"" line_velocity=""0.8"">
        <start_pos id=""2067""><pos x=""105.165"" y=""-77.350"" /></start_pos>
        <end_pos id=""CH-01""><pos x=""25.0"" y=""20.0"" /></end_pos>
        <control1_pos x=""80.0"" y=""-50.0"" />
        <control2_pos x=""40.0"" y=""-10.0"" />
    </advanced_curve>
    <traffic_area instance_name=""180"" traffic_count=""1"" id=""ZONE-180"">
        <posgroup x=""20.0"" y=""15.0""></posgroup>
        <posgroup x=""30.0"" y=""15.0""></posgroup>
        <posgroup x=""30.0"" y=""25.0""></posgroup>
        <posgroup x=""20.0"" y=""25.0""></posgroup>
    </traffic_area>
</MAP>";

    [Fact]
    public void ParseMapinfoJson_LoadsBezierAndStraightPaths()
    {
        var parser = new XMapParserService();
        var result = parser.ParseMapinfoJson(SampleMapinfoJson);

        Assert.Equal(3, result.Graph.Nodes.Count);
        Assert.Equal(2, result.Graph.Paths.Count);

        var bezier = result.Graph.GetPath("1") as CubicBezierPath;
        Assert.NotNull(bezier);
        Assert.Equal(11, bezier.P1.X);
        Assert.Equal(19, bezier.P1.Y);

        var line = result.Graph.GetPath("2") as StraightLinePath;
        Assert.NotNull(line);
        Assert.Equal(PathType.StraightLine, line.Type);
    }

    [Fact]
    public async Task ParseKecongXmap_LoadsTopologyPointsCurvesAndTrafficAreas()
    {
        var parser = new XMapParserService();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(SampleKecongXmap));

        var result = await parser.ParseXmapXmlAsync(stream, pointCloudDownsampleFactor: 1);

        Assert.Equal(2, result.Graph.Nodes.Count);
        Assert.Single(result.Graph.Paths);
        Assert.Single(result.Graph.Zones);
        Assert.Equal(2, result.PointCloud.Count);
        Assert.Equal("双差速轮---横移功能模型", result.MapName);

        var node2067 = result.Graph.GetNode("2067");
        Assert.NotNull(node2067);
        Assert.Equal(NodeType.Landmark, node2067.Type);
        Assert.True(node2067.AllowRevolve);
        Assert.Equal("both", node2067.SpinDirection);

        var nodeCh = result.Graph.GetNode("CH-01");
        Assert.NotNull(nodeCh);
        Assert.Equal(NodeType.ChargePoint, nodeCh.Type);

        var path = result.Graph.GetPath("2003-1") as CubicBezierPath;
        Assert.NotNull(path);
        Assert.Equal("forward", path.Direction);
        Assert.Equal(1.2, path.RoadWidth);
        Assert.Equal(0.8, path.MaxSpeed);

        var zone = result.Graph.GetZone("ZONE-180");
        Assert.NotNull(zone);
        Assert.Equal(1, zone.MaxCapacity);
        Assert.Contains("2003-1", zone.AssociatedPathIds);
    }

    [Fact]
    public async Task ParseReal52MbKecongXmapFile_SuccessIfPresent()
    {
        const string realPath = "/Users/feng/Documents/Code/研发/项目/AGV调度系统demo/双差速轮---横移功能模型.xmap";
        if (!File.Exists(realPath)) return;

        var parser = new XMapParserService();
        var result = await parser.ParseXmapFileAsync(realPath, pointCloudDownsampleFactor: 30);

        Assert.True(result.Graph.Nodes.Count >= 1250, $"Expected >= 1250 nodes, got {result.Graph.Nodes.Count}");
        Assert.True(result.Graph.Paths.Count >= 2500, $"Expected >= 2500 paths, got {result.Graph.Paths.Count}");
        Assert.True(result.Graph.Zones.Count >= 30, $"Expected >= 30 zones, got {result.Graph.Zones.Count}");
        Assert.True(result.TotalPointCloudCount > 600000, $"Expected > 600,000 points, got {result.TotalPointCloudCount}");
        Assert.True(result.PointCloud.Count > 15000, $"Expected sampled point cloud > 15,000, got {result.PointCloud.Count}");
    }
}
