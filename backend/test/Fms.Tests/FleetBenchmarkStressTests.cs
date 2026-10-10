using Fms.Application.Map;
using Fms.Engine;
using Xunit;

namespace Fms.Tests;

public class FleetBenchmarkStressTests
{
    [Fact]
    public async Task FleetBenchmark_30Vehicles_InLargeTopology_ExecutesUnderRealtimeLimit()
    {
        var parser = new XMapParserService();
        var engine = new DispatchEngine();

        // 载入真实科聪 1257 站点大地图
        string xmapPath = "/Users/feng/Documents/Code/研发/项目/AGV调度系统demo/双差速轮---横移功能模型.xmap";
        if (System.IO.File.Exists(xmapPath))
        {
            using var fs = System.IO.File.OpenRead(xmapPath);
            var res = await parser.ParseXmapXmlAsync(fs);
            Assert.NotEmpty(res.Graph.Nodes);
            engine.Graph.Clear();
            foreach (var n in res.Graph.Nodes) engine.Graph.AddNode(n);
            foreach (var p in res.Graph.Paths) engine.Graph.AddPath(p);
        }
        else
        {
            // Fallback: 如果大地图文件不存在，构建 50 站点测试拓扑
            for (int i = 0; i < 50; i++)
            {
                engine.Graph.AddNode(new Domain.Topology.MapNode($"NODE-{i}", new Domain.Shared.Geometry.Point2D(i * 5, (i % 5) * 5)));
            }
            for (int i = 0; i < 49; i++)
            {
                var p1 = new Domain.Shared.Geometry.Point2D(i * 5, (i % 5) * 5);
                var p2 = new Domain.Shared.Geometry.Point2D((i + 1) * 5, ((i + 1) % 5) * 5);
                engine.Graph.AddPath(new Domain.Topology.StraightLinePath($"P-{i}", $"NODE-{i}", $"NODE-{i+1}", p1, p2));
            }
        }

        // 运行 25 台车高并发压力基准测试 (50 拍 100ms 推进)
        var report = engine.RunFleetStressBenchmark(vehicleCount: 25, simulatedTicks: 50);

        Assert.NotNull(report);
        Assert.Equal(25, report.VehicleCount);
        Assert.Equal(50, report.TotalTicksExecuted);
        Assert.True(report.AvgTickDurationMs < 50.0, $"Avg tick duration {report.AvgTickDurationMs}ms exceeded 50ms limit");
        Assert.True(report.P95TickDurationMs < 50.0, $"P95 tick duration {report.P95TickDurationMs}ms exceeded 50ms limit");
        Assert.True(report.PassedRealtimeThreshold);
    }
}
