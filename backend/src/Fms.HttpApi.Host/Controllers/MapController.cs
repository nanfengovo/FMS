using System.IO;
using System.Text;
using System.Xml.Linq;
using Fms.Application;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Topology;
using Fms.Domain.Identity;
using Fms.HttpApi.Host.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fms.HttpApi.Host.Controllers;

[ApiController]
[Route("api/fms/map")]
public class MapController : ControllerBase
{
    private readonly FmsAppService _fmsAppService;

    public MapController(FmsAppService fmsAppService)
    {
        _fmsAppService = fmsAppService;
    }

    [RequiresPermission(FmsPermissions.Map)]
    [HttpGet("current")]
    public IActionResult GetCurrentMap()
    {
        var graph = _fmsAppService.Engine.Graph;

        var nodes = graph.Nodes.Select(n => new
        {
            id = n.Id,
            name = n.Name,
            type = n.Type.ToString(),
            x = Math.Round(n.Position.X, 3),
            y = Math.Round(n.Position.Y, 3),
            headingDeg = n.HeadingDeg,
            allowRevolve = n.AllowRevolve,
            spinDirection = n.SpinDirection,
            className = n.ClassName,
            dwellSec = n.DwellSec
        });

        var paths = graph.Paths.Select(p =>
        {
            if (p is CubicBezierPath bezier)
            {
                return new
                {
                    id = bezier.Id,
                    from = bezier.FromNodeId,
                    to = bezier.ToNodeId,
                    type = "bezier",
                    curveType = "bezier_curve",
                    direction = bezier.Direction,
                    roadWidth = bezier.RoadWidth,
                    bidirectional = bezier.Bidirectional,
                    maxSpeed = bezier.MaxSpeed,
                    length = Math.Round(bezier.Length, 3),
                    p0 = new { x = Math.Round(bezier.P0.X, 3), y = Math.Round(bezier.P0.Y, 3) },
                    p1 = new { x = Math.Round(bezier.P1.X, 3), y = Math.Round(bezier.P1.Y, 3) },
                    p2 = new { x = Math.Round(bezier.P2.X, 3), y = Math.Round(bezier.P2.Y, 3) },
                    p3 = new { x = Math.Round(bezier.P3.X, 3), y = Math.Round(bezier.P3.Y, 3) }
                };
            }
            var line = (StraightLinePath)p;
            return new
            {
                id = line.Id,
                from = line.FromNodeId,
                to = line.ToNodeId,
                type = "line",
                curveType = "straight_line",
                direction = line.Direction,
                roadWidth = line.RoadWidth,
                bidirectional = line.Bidirectional,
                maxSpeed = line.MaxSpeed,
                length = Math.Round(line.Length, 3),
                p0 = new { x = Math.Round(line.Start.X, 3), y = Math.Round(line.Start.Y, 3) },
                p1 = new { x = Math.Round(line.Start.X, 3), y = Math.Round(line.Start.Y, 3) },
                p2 = new { x = Math.Round(line.End.X, 3), y = Math.Round(line.End.Y, 3) },
                p3 = new { x = Math.Round(line.End.X, 3), y = Math.Round(line.End.Y, 3) }
            };
        });

        var zones = graph.Zones.Select(z => new
        {
            id = z.Id,
            name = z.Name,
            maxCapacity = z.MaxCapacity,
            timeoutSec = z.TimeoutSec,
            polygon = z.Polygon.Select(pt => new { x = Math.Round(pt.X, 3), y = Math.Round(pt.Y, 3) }),
            isOccupied = z.IsOccupied,
            claimant = z.CurrentClaimantVehicleId,
            associatedPathIds = z.AssociatedPathIds.ToArray()
        });

        return Ok(new
        {
            name = graph.Name,
            version = graph.Version,
            bounds = new { minX = graph.MinX, maxX = graph.MaxX, minY = graph.MinY, maxY = graph.MaxY },
            totalPointCloudCount = _fmsAppService.TotalPointCloudCount,
            pointCloudCount = _fmsAppService.CurrentPointCloud.Count,
            nodes,
            paths,
            zones
        });
    }

    /// <summary>
    /// 获取采样后的激光点云数据 (用于前端 PixiJS 激光图层渲染)
    /// </summary>
    [RequiresPermission(FmsPermissions.Map)]
    [HttpGet("point-cloud")]
    public IActionResult GetPointCloud([FromQuery] int limit = 20000)
    {
        var pts = _fmsAppService.CurrentPointCloud.Take(limit).Select(p => new
        {
            x = Math.Round(p.X, 2),
            y = Math.Round(p.Y, 2)
        });

        return Ok(new
        {
            total = _fmsAppService.TotalPointCloudCount,
            count = _fmsAppService.CurrentPointCloud.Count,
            points = pts
        });
    }

    /// <summary>
    /// 一键载入本地科聪实车工业地图 (双差速轮---横移功能模型.xmap)
    /// </summary>
    [RequiresPermission(FmsPermissions.MapParse)]
    [Authorize]
    [HttpPost("load-kecong-demo")]
    public async Task<IActionResult> LoadKecongDemo()
    {
        const string demoPath = "/Users/feng/Documents/Code/研发/项目/AGV调度系统demo/双差速轮---横移功能模型.xmap";
        if (!System.IO.File.Exists(demoPath))
        {
            return NotFound(new { error = $"Demo map file not found at: {demoPath}" });
        }

        var result = await _fmsAppService.LoadXmapFromFileAsync(demoPath);
        await _fmsAppService.SaveCurrentMapAsync();

        return Ok(new
        {
            success = true,
            mapName = result.MapName,
            nodeCount = result.Graph.Nodes.Count,
            pathCount = result.Graph.Paths.Count,
            zoneCount = result.Graph.Zones.Count,
            totalPoints = result.TotalPointCloudCount,
            sampledPoints = result.PointCloud.Count,
            bounds = new { minX = result.MinX, maxX = result.MaxX, minY = result.MinY, maxY = result.MaxY }
        });
    }

    /// <summary>
    /// 一键重置/载入洁净室双向基准测试地图 (NXP Cleanroom Benchmark)
    /// </summary>
    [RequiresPermission(FmsPermissions.MapParse)]
    [Authorize]
    [HttpPost("load-benchmark")]
    public async Task<IActionResult> LoadBenchmark()
    {
        _fmsAppService.LoadPresetBenchmarkMap();
        await _fmsAppService.SaveCurrentMapAsync();
        return Ok(new
        {
            success = true,
            mapName = _fmsAppService.Engine.Graph.Name,
            nodeCount = _fmsAppService.Engine.Graph.Nodes.Count,
            pathCount = _fmsAppService.Engine.Graph.Paths.Count,
            zoneCount = _fmsAppService.Engine.Graph.Zones.Count
        });
    }

    [RequiresPermission(FmsPermissions.MapParse)]
    [Authorize]
    [HttpPost("apply-json")]
    public async Task<IActionResult> ApplyJson([FromBody] MapinfoUploadDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.JsonContent))
        {
            return BadRequest(new { success = false, error = "路网原文为空，无法应用到当前路网" });
        }

        try
        {
            var result = _fmsAppService.LoadEditorMapJson(dto.JsonContent);
            await _fmsAppService.SaveCurrentMapAsync();
            return Ok(new
            {
                success = true,
                mapName = result.MapName,
                nodeCount = result.Graph.Nodes.Count,
                pathCount = result.Graph.Paths.Count,
                zoneCount = result.Graph.Zones.Count,
                message = $"已应用到当前路网「{result.MapName}」"
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, error = $"应用到当前路网失败: {ex.Message}" });
        }
    }

    [RequiresPermission(FmsPermissions.MapParse)]
    [Authorize]
    [HttpPost("upload-mapinfo")]
    public async Task<IActionResult> UploadMapinfo([FromBody] MapinfoUploadDto dto)
    {
        var result = _fmsAppService.LoadMapinfoJsonAsDraft(dto.JsonContent);
        await _fmsAppService.SaveDraftPointCloudAsync();
        return Ok(new
        {
            success = true,
            isDraft = true,
            message = "Mapinfo JSON已成功载入路网草稿态(Draft)。当前运行路网保持受控隔离，请在工作台确认后执行安全发布。",
            nodeCount = result.Graph.Nodes.Count,
            pathCount = result.Graph.Paths.Count
        });
    }

    [RequiresPermission(FmsPermissions.MapParse)]
    [Authorize]
    [HttpPost("upload-xmap")]
    public async Task<IActionResult> UploadXmap(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file uploaded" });
        }

        using var stream = file.OpenReadStream();
        var result = await _fmsAppService.LoadXmapAsDraftAsync(stream);
        await _fmsAppService.SaveDraftPointCloudAsync();

        return Ok(new
        {
            success = true,
            isDraft = true,
            message = "地图已成功解析并导入草稿态（Draft）。当前运行路网受安全互锁保护未变更，请在路网工作台确认且所有在途车辆出清停靠后，执行安全发布以生效至运行路网。",
            nodeCount = result.Graph.Nodes.Count,
            pathCount = result.Graph.Paths.Count,
            zoneCount = result.Graph.Zones.Count,
            pointCloudCount = result.PointCloudCount,
            totalPointCloudCount = result.TotalPointCloudCount
        });
    }

    /// <summary>
    /// 将当前拓扑地图持久化保存至 SQLite 数据库
    /// </summary>
    [RequiresPermission(FmsPermissions.MapEdit)]
    [Authorize]
    [HttpPost("save")]
    public async Task<IActionResult> SaveMap()
    {
        await _fmsAppService.SaveCurrentMapAsync();
        return Ok(new
        {
            success = true,
            message = "地图拓扑已成功持久化至 SQLite 数据库",
            nodeCount = _fmsAppService.Engine.Graph.Nodes.Count,
            pathCount = _fmsAppService.Engine.Graph.Paths.Count,
            zoneCount = _fmsAppService.Engine.Graph.Zones.Count
        });
    }

    /// <summary>
    /// 导出当前拓扑地图为标准科聪 XMAP XML 格式 (含激光点云与完整高级图元)
    /// </summary>
    [RequiresPermission(FmsPermissions.Map)]
    [HttpGet("export-xmap")]
    public IActionResult ExportXmapXml()
    {
        var xmlString = _fmsAppService.ExportCurrentMapToXmapXml();
        var mapName = string.IsNullOrWhiteSpace(_fmsAppService.Engine.Graph.Name) ? "DefaultMap" : _fmsAppService.Engine.Graph.Name;
        return File(Encoding.UTF8.GetBytes(xmlString), "application/xml", $"{mapName}.xmap");
    }

    [RequiresPermission(FmsPermissions.Map)]
    [HttpGet("raster/meta")]
    public IActionResult RasterMeta()
    {
        var raster = _fmsAppService.CurrentRaster;
        if (raster == null)
            return Ok(new { code = "0000", data = new { available = false } });

        return Ok(new
        {
            code = "0000",
            data = new
            {
                available = true,
                width = raster.Width,
                height = raster.Height,
                originX = raster.OriginX,
                topY = raster.TopY,
                resolution = raster.Resolution,
                widthMeters = raster.Width * raster.Resolution,
                heightMeters = raster.Height * raster.Resolution,
                sourceName = raster.SourceName
            }
        });
    }

    [RequiresPermission(FmsPermissions.Map)]
    [HttpGet("raster")]
    public IActionResult Raster()
    {
        var raster = _fmsAppService.CurrentRaster;
        if (raster == null) return NotFound(new { error = "当前地图没有科聪底图" });
        return File(raster.Bytes, raster.ContentType);
    }

    [RequiresPermission(FmsPermissions.MapParse)]
    [Authorize]
    [HttpPost("upload-raster")]
    public IActionResult UploadRaster(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "没有底图文件" });
        using var stream = new MemoryStream();
        file.CopyTo(stream);
        _fmsAppService.ReplaceRaster(stream.ToArray(), file.FileName);
        if (_fmsAppService.CurrentRaster == null)
            return BadRequest(new { error = "只支持科聪 PNG、JPG 或 PGM 底图" });

        var raster = _fmsAppService.CurrentRaster;
        return Ok(new
        {
            success = true,
            width = raster.Width,
            height = raster.Height,
            resolution = raster.Resolution,
            sourceName = raster.SourceName
        });
    }

    public class MapinfoUploadDto
    {
        public string JsonContent { get; set; } = string.Empty;
    }
}
