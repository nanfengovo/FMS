using Fms.Domain.Identity;
using Fms.HttpApi.Host.Authorization;
using System.Collections.Concurrent;
using System.Text.Json;
using Fms.Application;
using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Topology;
using Fms.Domain.Workflow;
using Fms.EntityFrameworkCore;
using Fms.EntityFrameworkCore.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fms.HttpApi.Host.Controllers;

[ApiController]
[Route("api/fms/workflows")]
public class WorkflowController : ControllerBase
{
    private readonly FmsAppService _fmsAppService;
    private readonly FmsDbContext _dbContext;
    private static readonly ConcurrentDictionary<string, WorkflowTemplate> _templates = new();
    private static readonly object _fileLock = new();
    private static bool _initialized = false;
    private static readonly string _dataFile = Path.Combine(AppContext.BaseDirectory, "data", "workflows.json");

    public WorkflowController(FmsAppService fmsAppService, FmsDbContext dbContext)
    {
        _fmsAppService = fmsAppService;
        _dbContext = dbContext;
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_fileLock)
        {
            if (_initialized) return;

            // 1. 注入默认内置工业模板
            foreach (var tpl in GetBuiltInTemplates())
            {
                _templates[tpl.Id] = tpl;
            }

            // 2. 从 SQLite 恢复自定义模板，并兼容旧文件迁移
            try
            {
                var dbList = _dbContext.WorkflowTemplates.AsNoTracking().ToList();
                if (dbList.Count > 0)
                {
                    foreach (var rec in dbList)
                    {
                        var steps = string.IsNullOrEmpty(rec.StepsJson)
                            ? new List<WorkflowStepItem>()
                            : JsonSerializer.Deserialize<List<WorkflowStepItem>>(rec.StepsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<WorkflowStepItem>();

                        _templates[rec.Id] = new WorkflowTemplate
                        {
                            Id = rec.Id,
                            Name = rec.Name,
                            Desc = rec.Description,
                            IsBuiltIn = rec.IsBuiltIn,
                            Version = rec.Version,
                            VersionTag = rec.VersionTag ?? "v1.0.0",
                            ChangeLog = rec.ChangeLog ?? "初始创建",
                            CreatedAt = rec.CreatedAt,
                            UpdatedAt = rec.UpdatedAt,
                            Steps = steps
                        };
                    }
                }
                else if (System.IO.File.Exists(_dataFile))
                {
                    var json = System.IO.File.ReadAllText(_dataFile);
                    var customList = JsonSerializer.Deserialize<List<WorkflowTemplate>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (customList != null)
                    {
                        foreach (var item in customList)
                        {
                            _templates[item.Id] = item;
                            _dbContext.WorkflowTemplates.Add(new WorkflowTemplateRecord
                            {
                                Id = item.Id,
                                Name = item.Name,
                                Description = item.Desc ?? string.Empty,
                                IsBuiltIn = item.IsBuiltIn,
                                StepsJson = JsonSerializer.Serialize(item.Steps),
                                CreatedAt = item.CreatedAt,
                                UpdatedAt = item.UpdatedAt
                            });
                        }
                        _dbContext.SaveChanges();
                    }
                }

                // 确保系统内置工业模板与最新代码定义同步 (包含闭环分支目标与真假决策工步)
                var builtInList = GetBuiltInTemplates();
                foreach (var b in builtInList)
                {
                    if (!_templates.TryGetValue(b.Id, out var existing) || existing.IsBuiltIn)
                    {
                        _templates[b.Id] = b;
                        var rec = _dbContext.WorkflowTemplates.FirstOrDefault(t => t.Id == b.Id);
                        if (rec != null)
                        {
                            rec.Name = b.Name;
                            rec.Description = b.Desc;
                            rec.IsBuiltIn = true;
                            rec.StepsJson = JsonSerializer.Serialize(b.Steps);
                            rec.UpdatedAt = DateTime.UtcNow;
                        }
                        else
                        {
                            _dbContext.WorkflowTemplates.Add(new WorkflowTemplateRecord
                            {
                                Id = b.Id,
                                Name = b.Name,
                                Description = b.Desc,
                                IsBuiltIn = true,
                                StepsJson = JsonSerializer.Serialize(b.Steps),
                                CreatedAt = b.CreatedAt,
                                UpdatedAt = b.UpdatedAt
                            });
                        }
                    }
                }
                _dbContext.SaveChanges();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorkflowController] Failed to load workflows: {ex.Message}");
            }

            _initialized = true;
        }
    }

    private void SaveCustomTemplatesToFile()
    {
        try
        {
            var dir = Path.GetDirectoryName(_dataFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var customList = _templates.Values.Where(t => !t.IsBuiltIn).ToList();
            var json = JsonSerializer.Serialize(customList, new JsonSerializerOptions { WriteIndented = true });
            lock (_fileLock)
            {
                System.IO.File.WriteAllText(_dataFile, json);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WorkflowController] Failed to save workflows.json: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取全量工作流模板（系统预置 + 用户自定义）
    /// </summary>
    [RequiresPermission(FmsPermissions.Workflow)]
    [HttpGet]
    public IActionResult GetTemplates()
    {
        RefreshBuiltInTemplates();
        var list = _templates.Values.OrderByDescending(t => t.IsBuiltIn).ThenBy(t => t.Name).ToList();
        return Ok(new { success = true, data = list });
    }

    /// <summary>
    /// 获取单个工作流模板详情
    /// </summary>
    [RequiresPermission(FmsPermissions.Workflow)]
    [HttpGet("{id}")]
    public IActionResult GetTemplate(string id)
    {
        if (_templates.TryGetValue(id, out var tpl))
        {
            return Ok(new { success = true, data = tpl });
        }
        return NotFound(new { error = $"Template {id} not found" });
    }

    /// <summary>
    /// 保存或更新工作流模板 (创建 / 编辑) 并持久化至 SQLite
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.WorkflowEdit)]
    [HttpPost]
    public async Task<IActionResult> SaveTemplate([FromBody] WorkflowTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.Name))
        {
            return BadRequest(new { error = "流程名称不能为空" });
        }

        if (string.IsNullOrWhiteSpace(template.Id))
        {
            template.Id = $"wf-custom-{Guid.NewGuid().ToString("N")[..8]}";
        }

        template.UpdatedAt = DateTime.UtcNow;
        if (!_templates.ContainsKey(template.Id))
        {
            template.CreatedAt = DateTime.UtcNow;
            template.IsBuiltIn = false;
        }

        _templates[template.Id] = template;

        // 写入 SQLite 与历史版本记录
        try
        {
            var existing = await _dbContext.WorkflowTemplates.FirstOrDefaultAsync(t => t.Id == template.Id);
            int nextVersion = 1;
            if (existing == null)
            {
                nextVersion = 1;
                template.Version = nextVersion;
                template.VersionTag = string.IsNullOrWhiteSpace(template.VersionTag) ? "v1.0.0" : template.VersionTag;
                template.ChangeLog = string.IsNullOrWhiteSpace(template.ChangeLog) ? "初始创建" : template.ChangeLog;

                var record = new WorkflowTemplateRecord
                {
                    Id = template.Id,
                    Name = template.Name,
                    Description = template.Desc ?? string.Empty,
                    IsBuiltIn = template.IsBuiltIn,
                    Version = template.Version,
                    VersionTag = template.VersionTag,
                    ChangeLog = template.ChangeLog,
                    StepsJson = JsonSerializer.Serialize(template.Steps),
                    CreatedAt = template.CreatedAt,
                    UpdatedAt = template.UpdatedAt
                };
                _dbContext.WorkflowTemplates.Add(record);
            }
            else
            {
                nextVersion = existing.Version + 1;
                existing.Version = nextVersion;
                existing.Name = template.Name;
                existing.Description = template.Desc ?? string.Empty;
                existing.VersionTag = string.IsNullOrWhiteSpace(template.VersionTag) ? $"v1.{nextVersion}.0" : template.VersionTag;
                existing.ChangeLog = string.IsNullOrWhiteSpace(template.ChangeLog) ? $"工步流水线优化与变更 (v{nextVersion})" : template.ChangeLog;
                existing.StepsJson = JsonSerializer.Serialize(template.Steps);
                existing.UpdatedAt = template.UpdatedAt;

                template.Version = existing.Version;
                template.VersionTag = existing.VersionTag;
                template.ChangeLog = existing.ChangeLog;
            }

            // 写入历史快照表 Fms_WorkflowTemplateHistories
            _dbContext.WorkflowTemplateHistories.Add(new WorkflowTemplateHistoryRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                TemplateId = template.Id,
                Version = template.Version,
                VersionTag = template.VersionTag,
                ChangeLog = template.ChangeLog,
                StepsJson = JsonSerializer.Serialize(template.Steps),
                CreatedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WorkflowController] Failed to persist template to SQLite: {ex.Message}");
        }

        SaveCustomTemplatesToFile();

        return Ok(new { success = true, message = "工序模板保存成功", data = template });
    }

    /// <summary>
    /// 获取工作流模板历史版本列表 (用于版本回溯与对比)
    /// </summary>
    [RequiresPermission(FmsPermissions.Workflow)]
    [HttpGet("{id}/history")]
    public async Task<IActionResult> GetTemplateHistory(string id)
    {
        var histories = await _dbContext.WorkflowTemplateHistories
            .AsNoTracking()
            .Where(h => h.TemplateId == id)
            .OrderByDescending(h => h.Version)
            .ToListAsync();

        if (histories.Count == 0 && _templates.TryGetValue(id, out var tpl))
        {
            histories.Add(new WorkflowTemplateHistoryRecord
            {
                Id = "base-v1",
                TemplateId = tpl.Id,
                Version = tpl.Version,
                VersionTag = tpl.VersionTag ?? "v1.0.0",
                ChangeLog = tpl.ChangeLog ?? "系统内置预设基线版本",
                StepsJson = JsonSerializer.Serialize(tpl.Steps),
                CreatedAt = tpl.CreatedAt
            });
        }

        var result = histories.Select(h =>
        {
            var steps = string.IsNullOrEmpty(h.StepsJson)
                ? new List<WorkflowStepItem>()
                : JsonSerializer.Deserialize<List<WorkflowStepItem>>(h.StepsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<WorkflowStepItem>();
            return new
            {
                id = h.Id,
                templateId = h.TemplateId,
                version = h.Version,
                versionTag = h.VersionTag,
                changeLog = h.ChangeLog,
                createdAt = h.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                stepsCount = steps.Count,
                steps = steps
            };
        });

        return Ok(new { success = true, data = result });
    }

    /// <summary>
    /// 删除工作流模板 (内置模板保护不可删除) 并从 SQLite 移除
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.WorkflowEdit)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTemplate(string id)
    {
        if (!_templates.TryGetValue(id, out var tpl))
        {
            return NotFound(new { error = $"Template {id} not found" });
        }

        if (tpl.IsBuiltIn)
        {
            return BadRequest(new { error = "系统内置工业模板受写保护，不可删除" });
        }

        _templates.TryRemove(id, out _);

        try
        {
            var existing = await _dbContext.WorkflowTemplates.FirstOrDefaultAsync(t => t.Id == id);
            if (existing != null)
            {
                _dbContext.WorkflowTemplates.Remove(existing);
                await _dbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WorkflowController] Failed to delete template from SQLite: {ex.Message}");
        }

        SaveCustomTemplatesToFile();

        return Ok(new { success = true, message = $"模板 {tpl.Name} 已成功删除" });
    }

    /// <summary>
    /// 当前仿真里正在推进的天风任务实例。
    /// </summary>
    [RequiresPermission(FmsPermissions.Workflow)]
    [HttpGet("live")]
    public IActionResult ListLive()
    {
        var engine = _fmsAppService.Engine;
        var items = engine.WorkflowManager.GetAllInstances()
            .OrderByDescending(instance => instance.StartedAtUtc)
            .Take(12)
            .Select(instance =>
            {
                var def = engine.WorkflowManager.GetDefinition(instance.DefinitionId);
                var node = def?.GetNode(instance.CurrentNodeId);
                return new
                {
                    instanceId = instance.InstanceId,
                    name = def?.Name ?? instance.DefinitionId,
                    status = instance.Status.ToString(),
                    vehicleId = instance.BindVehicleId,
                    currentStep = node?.Name ?? instance.CurrentNodeId,
                    targetNodeId = node?.TargetNodeId,
                    startedAt = instance.StartedAtUtc.ToString("HH:mm:ss")
                };
            });
        return Ok(new { success = true, items });
    }

    /// <summary>
    /// 运行工序流水线 (生成真实调度订单并推入引擎主循环)
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.WorkflowRun)]
    [HttpPost("run")]
    public IActionResult RunWorkflow([FromBody] RunWorkflowRequest dto)
    {
        RefreshBuiltInTemplates();
        List<WorkflowStepItem> stepsToRun = new();

        if (dto.Steps != null && dto.Steps.Count > 0)
        {
            stepsToRun = dto.Steps;
        }
        else if (!string.IsNullOrEmpty(dto.TemplateId) && _templates.TryGetValue(dto.TemplateId, out var tpl))
        {
            stepsToRun = tpl.Steps;
        }

        if (stepsToRun.Count == 0)
        {
            return BadRequest(new { error = "工序步骤为空，无法实例化运行" });
        }

        var stationError = ValidateStepsOnCurrentMap(stepsToRun);
        if (stationError != null)
        {
            return BadRequest(new { error = stationError });
        }

        string defId = !string.IsNullOrEmpty(dto.TemplateId) ? dto.TemplateId : $"def-{Guid.NewGuid():N}"[..12];
        var def = new WorkflowDefinition
        {
            Id = defId,
            Name = !string.IsNullOrEmpty(dto.TemplateId) && _templates.TryGetValue(dto.TemplateId, out var tplFound) ? tplFound.Name : "工业编排工艺流",
            Description = "动态编排注册",
            Version = 1,
            VersionTag = "v1.0.0"
        };

        for (int i = 0; i < stepsToRun.Count; i++)
        {
            var s = stepsToRun[i];
            var nextNodeId = s.NextStepId ?? (i + 1 < stepsToRun.Count ? stepsToRun[i + 1].Id : null);
            def.Nodes.Add(new WorkflowNodeDefinition
            {
                Id = s.Id,
                Name = string.IsNullOrWhiteSpace(s.Name) ? $"{i + 1}. 工步_{s.ActionType}" : s.Name,
                ActionType = (s.ActionType ?? "MOVE_TO_POINT").ToUpperInvariant(),
                TargetNodeId = s.NodeId ?? string.Empty,
                TargetSlotId = s.SlotId ?? string.Empty,
                DwellSec = s.DwellSec > 0 ? s.DwellSec : 1.0,
                CargoId = s.CargoId,
                IoSignalName = s.IoSignalName,
                ExpectedIoValue = s.ExpectedIoValue ?? "1",
                ConditionType = s.ConditionType,
                ConditionValue = s.ConditionValue,
                TrueTargetNodeId = s.TrueTargetStepId,
                FalseTargetNodeId = s.FalseTargetStepId,
                NextNodeId = nextNodeId,
                IsTerminal = s.IsTerminal || (i == stepsToRun.Count - 1 && string.IsNullOrEmpty(s.TrueTargetStepId))
            });
        }

        _fmsAppService.Engine.WorkflowManager.RegisterDefinition(def);
        var instance = _fmsAppService.Engine.WorkflowManager.StartInstance(def.Id, dto.VehicleId);

        return Ok(new
        {
            success = true,
            instanceId = instance.InstanceId,
            orderId = instance.InstanceId,
            orderSerial = $"WF-{instance.InstanceId[..8].ToUpperInvariant()}",
            totalSteps = def.Nodes.Count,
            assignedVehicle = instance.BindVehicleId,
            status = instance.Status.ToString()
        });
    }

    /// <summary>
    /// 获取当前任务的工步实时执行进度与车辆状态
    /// </summary>
    [RequiresPermission(FmsPermissions.Workflow)]
    [HttpGet("execution/{orderId}")]
    public IActionResult GetExecutionStatus(string orderId)
    {
        var engine = _fmsAppService.Engine;

        // 1. 优先从低代码工作流实例管理器读取
        var instance = engine.WorkflowManager.GetInstance(orderId);
        if (instance != null)
        {
            var def = engine.WorkflowManager.GetDefinition(instance.DefinitionId);
            var assignedVehicle = !string.IsNullOrEmpty(instance.BindVehicleId) && engine.Vehicles.TryGetValue(instance.BindVehicleId, out var iv) ? iv : null;
            int currentIndex = def != null ? Math.Max(0, def.Nodes.FindIndex(n => n.Id == instance.CurrentNodeId)) : 0;

            var nodesList = def?.Nodes.Select((n, index) => new
            {
                stepIndex = index,
                name = n.Name,
                action = n.ActionType,
                targetNodeId = n.TargetNodeId,
                dwellSec = n.DwellSec,
                cargoId = n.CargoId,
                isCompleted = index < currentIndex || instance.Status == WorkflowInstanceStatus.Completed,
                isExecuting = index == currentIndex && instance.Status != WorkflowInstanceStatus.Completed && instance.Status != WorkflowInstanceStatus.Fault,
                startedAt = instance.StartedAtUtc.ToString("HH:mm:ss"),
                finishedAt = instance.CompletedAtUtc?.ToString("HH:mm:ss")
            }).ToList() ?? new();

            return Ok(new
            {
                success = true,
                orderId = instance.InstanceId,
                orderSerial = $"WF-{instance.InstanceId[..8].ToUpperInvariant()}",
                status = instance.Status.ToString(),
                currentStepIndex = currentIndex,
                totalSteps = def?.Nodes.Count ?? 0,
                startedAt = instance.StartedAtUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                completedAt = instance.CompletedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss"),
                failReason = instance.Status == WorkflowInstanceStatus.Fault ? "流程异常中断" : null,
                vehicle = assignedVehicle == null ? null : new
                {
                    id = assignedVehicle.Id,
                    name = assignedVehicle.Name,
                    status = assignedVehicle.Status.ToString(),
                    speed = Math.Round(assignedVehicle.CurrentSpeed, 2),
                    battery = Math.Round(assignedVehicle.Bms.SocPercent, 1),
                    pose = new
                    {
                        x = Math.Round(assignedVehicle.Pose.X, 2),
                        y = Math.Round(assignedVehicle.Pose.Y, 2),
                        thetaDeg = Math.Round(assignedVehicle.Pose.ThetaDeg, 1)
                    },
                    currentNodeId = assignedVehicle.CurrentNodeId,
                    targetNodeId = assignedVehicle.TargetNodeId,
                    claimedMutexZone = assignedVehicle.ClaimedMutexZoneId,
                    isYielding = assignedVehicle.IsYielding
                },
                steps = nodesList,
                logs = instance.ExecutionLogs.TakeLast(40).ToList(),
                targetNodeId = def?.GetNode(instance.CurrentNodeId)?.TargetNodeId
            });
        }

        // 2. 回退到直接物理搬运单查询
        var order = engine.Orders.Values.FirstOrDefault(o => o.Id == orderId || o.OrderSerial == orderId);
        if (order == null)
        {
            return NotFound(new { error = $"Order {orderId} not found" });
        }

        var vehicle = !string.IsNullOrEmpty(order.AssignedVehicleId) && engine.Vehicles.TryGetValue(order.AssignedVehicleId, out var v) ? v : null;

        var stepList = order.Steps.Select((s, index) => new
        {
            stepIndex = s.StepIndex,
            name = s.Name,
            action = s.Action.ToString(),
            targetNodeId = s.TargetNodeId,
            dwellSec = s.DwellSec,
            cargoId = s.CargoId,
            isCompleted = s.IsCompleted,
            isExecuting = (index == order.CurrentStepIndex && (order.Status == OrderStatus.Running || order.Status == OrderStatus.Assigned)),
            startedAt = s.StartedAt?.ToString("HH:mm:ss"),
            finishedAt = s.FinishedAt?.ToString("HH:mm:ss")
        }).ToList();

        return Ok(new
        {
            success = true,
            orderId = order.Id,
            orderSerial = order.OrderSerial,
            status = order.Status.ToString(),
            currentStepIndex = order.CurrentStepIndex,
            totalSteps = order.Steps.Count,
            startedAt = order.StartedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
            completedAt = order.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
            failReason = order.FailReason,
            vehicle = vehicle == null ? null : new
            {
                id = vehicle.Id,
                name = vehicle.Name,
                status = vehicle.Status.ToString(),
                speed = Math.Round(vehicle.CurrentSpeed, 2),
                battery = Math.Round(vehicle.Bms.SocPercent, 1),
                pose = new
                {
                    x = Math.Round(vehicle.Pose.X, 2),
                    y = Math.Round(vehicle.Pose.Y, 2),
                    thetaDeg = Math.Round(vehicle.Pose.ThetaDeg, 1)
                },
                currentNodeId = vehicle.CurrentNodeId,
                targetNodeId = vehicle.TargetNodeId,
                claimedMutexZone = vehicle.ClaimedMutexZoneId,
                isYielding = vehicle.IsYielding
            },
            steps = stepList
        });
    }

    /// <summary>
    /// 控制正在运行的任务 (跳步 / 取消)
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.WorkflowRun)]
    [HttpPost("execution/{orderId}/control")]
    public IActionResult ControlExecution(string orderId, [FromBody] WorkflowControlDto dto)
    {
        var engine = _fmsAppService.Engine;
        string cmd = (dto.Command ?? "").Trim().ToLowerInvariant();

        // 1. 检查是否为流程实例
        var instance = engine.WorkflowManager.GetInstance(orderId);
        if (instance != null)
        {
            if (cmd == "cancel")
            {
                instance.Status = WorkflowInstanceStatus.Cancelled;
                instance.AppendLog($"用户请求取消流程: {dto.Reason ?? "人工中止"}");
                if (!string.IsNullOrEmpty(instance.ActiveTransportOrderId) && engine.Orders.TryGetValue(instance.ActiveTransportOrderId, out var activeOrd))
                {
                    engine.CancelOrder(activeOrd.Id, dto.Reason ?? "关联流程实例已取消");
                }
                return Ok(new { success = true, message = "流程实例已成功中止" });
            }
            else if (cmd == "skip")
            {
                var def = engine.WorkflowManager.GetDefinition(instance.DefinitionId);
                var currNode = def?.GetNode(instance.CurrentNodeId);
                if (def != null && currNode != null)
                {
                    var nextNodeId = currNode.NextNodeId ?? (currNode.TrueTargetNodeId ?? currNode.FalseTargetNodeId);
                    if (!string.IsNullOrEmpty(nextNodeId))
                    {
                        instance.CurrentNodeId = nextNodeId;
                        instance.AppendLog($"用户强制跳过节点 {currNode.Name}，推进至 {nextNodeId}");
                        return Ok(new { success = true, message = "已跳过当前工步，推进至下一步" });
                    }
                }
            }
        }

        // 2. 回退到直接搬运单
        var order = engine.Orders.Values.FirstOrDefault(o => o.Id == orderId || o.OrderSerial == orderId);
        if (order == null)
        {
            return NotFound(new { error = $"Order or WorkflowInstance {orderId} not found" });
        }

        if (cmd == "skip")
        {
            engine.SkipStep(order.Id);
            return Ok(new { success = true, message = $"任务 {order.OrderSerial} 已跳过当前工步" });
        }
        else if (cmd == "cancel")
        {
            engine.CancelOrder(order.Id, dto.Reason ?? "调度员在工作流监控台手动中止");
            return Ok(new { success = true, message = $"任务 {order.OrderSerial} 已成功中止取消" });
        }

        return BadRequest(new { error = $"Unknown command: {dto.Command}" });
    }

    private void RefreshBuiltInTemplates()
    {
        foreach (var b in GetBuiltInTemplates())
        {
            _templates[b.Id] = b;
        }
    }

    private string? ValidateStepsOnCurrentMap(List<WorkflowStepItem> steps)
    {
        var engine = _fmsAppService.Engine;
        var mapName = string.IsNullOrWhiteSpace(engine.Graph.Name) ? "当前路网" : engine.Graph.Name;
        var reasons = new List<string>();
        foreach (var step in steps)
        {
            bool needsStation = NeedsMapStation(step.ActionType);
            if (needsStation && string.IsNullOrWhiteSpace(step.NodeId))
            {
                reasons.Add($"工步「{step.Name}」未指定站点");
                continue;
            }
            if (!string.IsNullOrWhiteSpace(step.NodeId) && !engine.HasNode(step.NodeId))
            {
                reasons.Add($"工步「{step.Name}」的站点 {step.NodeId} 不在路网「{mapName}」上");
            }
            if (!string.IsNullOrWhiteSpace(step.MutexZoneId) && engine.Graph.GetZone(step.MutexZoneId) == null)
            {
                reasons.Add($"工步「{step.Name}」的互斥区 {step.MutexZoneId} 不在路网「{mapName}」上");
            }
        }
        if (reasons.Count == 0) return null;
        return "禁止下发：" + string.Join("；", reasons);
    }

    private static bool NeedsMapStation(string? actionType)
    {
        string key = (actionType ?? "").ToUpperInvariant();
        return key is "MOVE_TO_POINT" or "MOVE_ALONG_PATH" or "PULL_OVER" or "AUTO_CHARGE"
            or "LIFT_UP" or "PUT_DOWN" or "FORK_PICK" or "FORK_DROP" or "ROLLER_TRANSFER"
            or "RFID_READ_VERIFY" or "BARCODE_SCAN" or "CLAIM_MUTEX_ZONE" or "RELEASE_MUTEX_ZONE";
    }

    private List<WorkflowTemplate> GetBuiltInTemplates()
    {
        var graph = _fmsAppService.Engine.Graph;
        var standby = graph.Nodes.Where(n => n.Type == NodeType.StandbyPoint).OrderBy(n => n.Id).ToList();
        var charge = graph.Nodes.Where(n => n.Type == NodeType.ChargePoint).OrderBy(n => n.Id).ToList();
        var job = graph.Nodes.Where(n => n.Type == NodeType.JobPoint).OrderBy(n => n.Id).ToList();
        var park = graph.Nodes.Where(n => n.Type == NodeType.StandbyPoint || n.Type == NodeType.PullOverPoint).OrderBy(n => n.Id).ToList();
        var all = graph.Nodes.OrderBy(n => n.Id).ToList();
        var zone = graph.Zones.OrderBy(z => z.Id).FirstOrDefault();

        string Pick(IReadOnlyList<MapNode> src, int index)
        {
            if (src.Count > 0) return src[Math.Min(index, src.Count - 1)].Id;
            if (all.Count > 0) return all[Math.Min(index, all.Count - 1)].Id;
            return "";
        }

        string s0 = Pick(standby.Count > 0 ? standby : job, 0);
        string s1 = Pick(standby.Count > 1 ? standby : (job.Count > 1 ? job : standby), 1);
        if (string.IsNullOrEmpty(s1) || s1 == s0) s1 = Pick(all, Math.Min(1, Math.Max(0, all.Count - 1)));
        if (string.IsNullOrEmpty(s1)) s1 = s0;
        string c0 = Pick(charge, 0);
        if (string.IsNullOrEmpty(c0)) c0 = s0;
        string p0 = Pick(park, 0);
        if (string.IsNullOrEmpty(p0)) p0 = s0;
        string p1 = Pick(park, 1);
        if (string.IsNullOrEmpty(p1)) p1 = p0;
        string z0 = zone?.Id ?? "";
        string mapName = string.IsNullOrWhiteSpace(graph.Name) ? "当前路网" : graph.Name;

        return new List<WorkflowTemplate>
        {
            new()
            {
                Id = "foup-flow",
                Name = "半导体晶圆盒 (FOUP) 洁净流转",
                Desc = $"在路网「{mapName}」上，从待命点 {s0} 取货，经互斥区送至 {s1}，低电量回充 {c0}",
                IsBuiltIn = true,
                CreatedAt = new DateTime(2026, 10, 1),
                UpdatedAt = DateTime.UtcNow,
                Steps = new List<WorkflowStepItem>
                {
                    new() { Id = "STEP-01", Category = "control", Name = "MES 工单到达与前置触发", ActionType = "WAIT_IO_SIGNAL", NodeId = s0, DwellSec = 0.5, CargoId = "FOUP-8801", SlotId = "A-01", IoSignalName = "DOCK_READY", ExpectedIoValue = "1", Status = "ready", Desc = $"等待机台 DOCK_READY 就绪信号（站点 {s0}）" },
                    new() { Id = "STEP-02", Category = "nav", Name = "空载驶向起点待命点", ActionType = "MOVE_TO_POINT", NodeId = s0, DwellSec = 1.0, MaxSpeed = 1.5, Status = "ready", Desc = $"规划最优路径驶向待命点 {s0}" },
                    new() { Id = "STEP-03", Category = "action", Name = "到位精准对接与顶升取货", ActionType = "LIFT_UP", NodeId = s0, DwellSec = 3.5, LiftStrokeMm = 120, CargoId = "FOUP-8801", SlotId = "A-01", Status = "ready", Desc = "激光测距对齐，机构顶升并锁死晶圆盒托盘" },
                    new() { Id = "STEP-04", Category = "verify", Name = "载具 RFID 身份防错校验", ActionType = "RFID_READ_VERIFY", NodeId = s0, DwellSec = 2.0, CargoId = "FOUP-8801", SlotId = "A-01", Status = "ready", Desc = "读取载具 RFID 电子标签，与 MES 工单严格双向校验比对" },
                    new() { Id = "STEP-05", Category = "mutex", Name = "申领核心交叉口防冲互斥锁", ActionType = "CLAIM_MUTEX_ZONE", NodeId = s1, MutexZoneId = z0, DwellSec = 1.0, TimeoutSec = 30, Status = "ready", Desc = string.IsNullOrEmpty(z0) ? "当前路网暂无互斥区，保持工步占位" : $"申领互斥区 {z0} 通行权" },
                    new() { Id = "STEP-06", Category = "nav", Name = "重载转运至目标待命点", ActionType = "MOVE_TO_POINT", NodeId = s1, DwellSec = 1.0, MaxSpeed = 1.2, CargoId = "FOUP-8801", Status = "ready", Desc = $"平稳驶向目标点 {s1}" },
                    new() { Id = "STEP-07", Category = "action", Name = "工位对接与平稳卸料落座", ActionType = "PUT_DOWN", NodeId = s1, DwellSec = 3.5, LiftStrokeMm = 0, CargoId = "FOUP-8801", SlotId = "B-02", Status = "ready", Desc = "降下顶升机构，将晶圆盒平稳交付" },
                    new() { Id = "STEP-08", Category = "mutex", Name = "释放核心交叉口互斥锁", ActionType = "RELEASE_MUTEX_ZONE", NodeId = s1, MutexZoneId = z0, DwellSec = 1.0, Status = "ready", Desc = string.IsNullOrEmpty(z0) ? "当前路网暂无互斥区，保持工步占位" : $"释放互斥区 {z0}" },
                    new() { Id = "STEP-09", Category = "control", Name = "后置决策：自动停靠或回充", ActionType = "DECISION_BRANCH", NodeId = p0, DwellSec = 1.5, Status = "ready", ConditionType = "SOC_LESS_THAN", ConditionValue = "25.0", TrueTargetStepId = "STEP-10", FalseTargetStepId = "STEP-11", Desc = $"若 SOC < 25% 驶向充电点 {c0}；否则驶向待命点 {p0}" },
                    new() { Id = "STEP-10", Category = "control", Name = $"自动驶入充电点 {c0}", ActionType = "AUTO_CHARGE", NodeId = c0, DwellSec = 10.0, IsTerminal = true, Status = "ready", Desc = $"电量低于 25%，驶入充电点 {c0} 恒流快充" },
                    new() { Id = "STEP-11", Category = "control", Name = $"驶入待命点 {p0}", ActionType = "PULL_OVER", NodeId = p0, DwellSec = 2.0, IsTerminal = true, Status = "ready", Desc = $"电量充足，驶入待命点 {p0} 出清主航道" }
                }
            },
            new()
            {
                Id = "pallet-loop",
                Name = "线边立库双向搬运循环",
                Desc = $"从待命点 {s0} 提取空托盘，卸货至 {s1} 后停靠 {p1}",
                IsBuiltIn = true,
                CreatedAt = new DateTime(2026, 10, 1),
                UpdatedAt = DateTime.UtcNow,
                Steps = new List<WorkflowStepItem>
                {
                    new() { Id = "STEP-01", Category = "control", Name = "线边呼叫盒空托盘补给召唤", ActionType = "WAIT_IO_SIGNAL", NodeId = s0, DwellSec = 0.5, CargoId = "PLT-EMPTY", Status = "ready", Desc = "线边光电开关检测料尽，呼叫空托盘搬运" },
                    new() { Id = "STEP-02", Category = "nav", Name = "驶向取货待命点", ActionType = "MOVE_TO_POINT", NodeId = s0, DwellSec = 1.0, MaxSpeed = 1.5, Status = "ready", Desc = $"最优航道驶向 {s0}" },
                    new() { Id = "STEP-03", Category = "action", Name = "双向货叉精准叉取托盘", ActionType = "FORK_PICK", NodeId = s0, DwellSec = 4.0, CargoId = "PLT-0044", SlotId = "A-02", Status = "ready", Desc = "货叉侧向伸缩叉取空托盘" },
                    new() { Id = "STEP-04", Category = "nav", Name = "转运至目标待命点", ActionType = "MOVE_TO_POINT", NodeId = s1, DwellSec = 1.0, MaxSpeed = 1.2, CargoId = "PLT-0044", Status = "ready", Desc = $"直行转运至 {s1}" },
                    new() { Id = "STEP-05", Category = "action", Name = "辊道连线对接与卸货交付", ActionType = "ROLLER_TRANSFER", NodeId = s1, DwellSec = 4.5, CargoId = "PLT-0044", SlotId = "B-04", Status = "ready", Desc = "与机台辊道信号握手回转输送托盘" },
                    new() { Id = "STEP-06", Category = "control", Name = "释放航道就近停靠待命", ActionType = "PULL_OVER", NodeId = p1, DwellSec = 2.0, Status = "ready", Desc = $"驶入待命点 {p1}，释放主廊道" }
                }
            },
            new()
            {
                Id = "charge-flow",
                Name = "智能低电量梯次快充维护流",
                Desc = $"BMS 电量巡检触发预警，预约充电点 {c0} 入桩快充，达标后停靠 {p0}",
                IsBuiltIn = true,
                CreatedAt = new DateTime(2026, 10, 1),
                UpdatedAt = DateTime.UtcNow,
                Steps = new List<WorkflowStepItem>
                {
                    new() { Id = "STEP-01", Category = "control", Name = "BMS 电池状态遥测低于 25%", ActionType = "DECISION_BRANCH", NodeId = p0, DwellSec = 0.5, Status = "ready", Desc = "遥测巡检检测到低于安全警戒线" },
                    new() { Id = "STEP-02", Category = "mutex", Name = "预约并锁定充电工位", ActionType = "CLAIM_MUTEX_ZONE", NodeId = c0, MutexZoneId = z0, DwellSec = 1.0, TimeoutSec = 60, Status = "ready", Desc = string.IsNullOrEmpty(z0) ? "当前路网暂无互斥区，保持工步占位" : $"排他性锁定充电点 {c0}" },
                    new() { Id = "STEP-03", Category = "nav", Name = "规划航网引导驶入充电点", ActionType = "MOVE_TO_POINT", NodeId = c0, DwellSec = 1.0, MaxSpeed = 0.8, Status = "ready", Desc = $"低速精确对齐充电点 {c0}" },
                    new() { Id = "STEP-04", Category = "control", Name = "伸出接触式滑块恒流快充", ActionType = "AUTO_CHARGE", NodeId = c0, DwellSec = 10.0, Status = "ready", Desc = $"在充电点 {c0} 恒流快充至 85%" },
                    new() { Id = "STEP-05", Category = "mutex", Name = "脱离触点释放充电锁", ActionType = "RELEASE_MUTEX_ZONE", NodeId = c0, MutexZoneId = z0, DwellSec = 1.0, Status = "ready", Desc = string.IsNullOrEmpty(z0) ? "当前路网暂无互斥区，保持工步占位" : "缩回滑块，归还充电点" },
                    new() { Id = "STEP-06", Category = "nav", Name = "驶入就近待命点恢复接单", ActionType = "PULL_OVER", NodeId = p0, DwellSec = 2.0, MaxSpeed = 1.5, Status = "ready", Desc = $"驶入待命点 {p0}，恢复接单" }
                }
            },
            new()
            {
                Id = "kecong-1257-flow",
                Name = "科聪实车站点巡检与跨区协同",
                Desc = $"沿当前路网「{mapName}」巡航，申领互斥区后到达作业点装载",
                IsBuiltIn = true,
                CreatedAt = new DateTime(2026, 10, 1),
                UpdatedAt = DateTime.UtcNow,
                Steps = new List<WorkflowStepItem>
                {
                    new() { Id = "STEP-01", Category = "nav", Name = "主廊道长途巡航", ActionType = "MOVE_TO_POINT", NodeId = s0, DwellSec = 1.0, MaxSpeed = 2.0, Status = "ready", Desc = $"沿当前路网驶向 {s0}" },
                    new() { Id = "STEP-02", Category = "mutex", Name = "申领互斥交管区", ActionType = "CLAIM_MUTEX_ZONE", NodeId = s0, MutexZoneId = z0, DwellSec = 1.0, TimeoutSec = 45, Status = "ready", Desc = string.IsNullOrEmpty(z0) ? "当前路网暂无互斥区，保持工步占位" : $"申领互斥区 {z0}" },
                    new() { Id = "STEP-03", Category = "nav", Name = "穿过交管核心区到达作业点", ActionType = "MOVE_TO_POINT", NodeId = s1, DwellSec = 1.0, MaxSpeed = 1.2, Status = "ready", Desc = $"安全到达作业点 {s1}" },
                    new() { Id = "STEP-04", Category = "action", Name = "工位精准对齐并装载", ActionType = "LIFT_UP", NodeId = s1, DwellSec = 4.0, LiftStrokeMm = 150, CargoId = "KC-LOT-01", Status = "ready", Desc = "激光定位对接物料载具" },
                    new() { Id = "STEP-05", Category = "verify", Name = "RFID 载具防混料校验", ActionType = "RFID_READ_VERIFY", NodeId = s1, DwellSec = 2.0, CargoId = "KC-LOT-01", Status = "ready", Desc = "多频天线读取并核实" },
                    new() { Id = "STEP-06", Category = "mutex", Name = "出清交管区并归还锁权", ActionType = "RELEASE_MUTEX_ZONE", NodeId = s1, MutexZoneId = z0, DwellSec = 1.0, Status = "ready", Desc = string.IsNullOrEmpty(z0) ? "当前路网暂无互斥区，保持工步占位" : $"释放互斥区 {z0}" }
                }
            }
        };
    }

    public class RunWorkflowRequest
    {
        public string? TemplateId { get; set; }
        public string? TaskSerial { get; set; }
        public string? VehicleId { get; set; }
        public int Priority { get; set; } = 80;
        public List<WorkflowStepItem>? Steps { get; set; }
    }

    public class WorkflowControlDto
    {
        public string Command { get; set; } = "skip"; // skip, cancel
        public string? Reason { get; set; }
    }
}
