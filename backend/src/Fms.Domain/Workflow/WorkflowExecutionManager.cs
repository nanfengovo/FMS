using System.Collections.Concurrent;
using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Vehicles;

namespace Fms.Domain.Workflow;

/// <summary>
/// 低代码流程实例执行管理器：解耦业务编排与纯物理搬运，驱动网关信号与工步流转
/// </summary>
public class WorkflowExecutionManager
{
    private readonly ConcurrentDictionary<string, WorkflowDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WorkflowInstance> _instances = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stepLock = new();

    public WorkflowExecutionManager()
    {
        SeedStandardDefinition();
    }

    public void RegisterDefinition(WorkflowDefinition definition)
    {
        _definitions[definition.Id] = definition;
    }

    public WorkflowDefinition? GetDefinition(string id) => _definitions.GetValueOrDefault(id);
    public IReadOnlyList<WorkflowDefinition> GetAllDefinitions() => _definitions.Values.ToList();

    public WorkflowInstance? GetInstance(string instanceId) => _instances.GetValueOrDefault(instanceId);
    public IReadOnlyList<WorkflowInstance> GetAllInstances() => _instances.Values.ToList();

    public WorkflowInstance StartInstance(
        string definitionId,
        string? bindVehicleId = null,
        Dictionary<string, string>? initialVars = null)
    {
        if (!_definitions.TryGetValue(definitionId, out var def))
            throw new InvalidOperationException($"流程定义 {definitionId} 不存在");

        var instance = new WorkflowInstance
        {
            DefinitionId = def.Id,
            VersionTag = def.VersionTag,
            BindVehicleId = bindVehicleId,
            CurrentNodeId = def.Nodes.FirstOrDefault()?.Id ?? "",
            Status = WorkflowInstanceStatus.Running
        };

        if (initialVars != null)
        {
            foreach (var kv in initialVars)
            {
                instance.Variables[kv.Key] = kv.Value;
            }
        }

        instance.AppendLog($"流程实例创建成功，采用定义 {def.Name} ({def.VersionTag})，首节点: {instance.CurrentNodeId}");
        _instances[instance.InstanceId] = instance;
        return instance;
    }

    /// <summary>
    /// 单拍轮询推进所有活动流程实例
    /// </summary>
    public void AdvanceInstances(
        Func<string, string> getIoSignalFunc,
        Func<string, VehicleAggregate?> getVehicleFunc,
        Func<string, TransportOrderAggregate?> getOrderFunc,
        Action<TransportOrderAggregate> submitTransportOrderAction)
    {
        lock (_stepLock)
        {
            foreach (var instance in _instances.Values)
            {
                for (int hop = 0; hop < 5; hop++)
                {
                    if (instance.Status != WorkflowInstanceStatus.Running &&
                        instance.Status != WorkflowInstanceStatus.WaitingIoSignal &&
                        instance.Status != WorkflowInstanceStatus.WaitingTransportOrder)
                    {
                        break;
                    }

                    if (!_definitions.TryGetValue(instance.DefinitionId, out var def)) break;
                    var currentNode = def.GetNode(instance.CurrentNodeId);
                    if (currentNode == null)
                    {
                        instance.Status = WorkflowInstanceStatus.Fault;
                        instance.AppendLog($"当前节点 {instance.CurrentNodeId} 在定义中未找到，流程异常终止");
                        break;
                    }

                    // 1. 如果正在等待运输单完成
                    if (instance.Status == WorkflowInstanceStatus.WaitingTransportOrder && !string.IsNullOrEmpty(instance.ActiveTransportOrderId))
                    {
                        var activeOrder = getOrderFunc(instance.ActiveTransportOrderId);
                        if (activeOrder != null)
                        {
                            if (activeOrder.Status == OrderStatus.Done)
                            {
                                instance.AppendLog($"关联运输单 {activeOrder.Id} 已完成，节点 {currentNode.Name} 顺利通过");
                                instance.ActiveTransportOrderId = null;
                                AdvanceToNext(instance, def, currentNode);
                                continue;
                            }
                            else if (activeOrder.Status == OrderStatus.Failed || activeOrder.Status == OrderStatus.Cancelled)
                            {
                                instance.Status = WorkflowInstanceStatus.Fault;
                                instance.AppendLog($"关联运输单 {activeOrder.Id} 失败或被取消 ({activeOrder.FailReason})");
                                break;
                            }
                            else
                            {
                                // 运输单仍在执行中，保持等待
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }

                    // 2. 处理不同节点动作
                    if (currentNode.ActionType.Equals("WAIT_IO_SIGNAL", StringComparison.OrdinalIgnoreCase))
                    {
                        instance.Status = WorkflowInstanceStatus.WaitingIoSignal;
                        string signalName = currentNode.IoSignalName ?? "DOCK_READY";
                        string expectedVal = currentNode.ExpectedIoValue ?? "1";
                        string actualVal = getIoSignalFunc(signalName);

                        if (actualVal.Equals(expectedVal, StringComparison.OrdinalIgnoreCase))
                        {
                            instance.AppendLog($"等待工业网关信号 [{signalName}] 满足期望值 {expectedVal}，放行流转");
                            instance.Status = WorkflowInstanceStatus.Running;
                            AdvanceToNext(instance, def, currentNode);
                            continue;
                        }
                        break;
                    }
                    else if (currentNode.ActionType.Equals("DECISION_BRANCH", StringComparison.OrdinalIgnoreCase))
                    {
                        instance.Status = WorkflowInstanceStatus.Running;
                        bool conditionMet = EvaluateCondition(instance, currentNode, getVehicleFunc, getIoSignalFunc);
                        string targetNodeId = conditionMet ? (currentNode.TrueTargetNodeId ?? "") : (currentNode.FalseTargetNodeId ?? "");

                        instance.AppendLog($"条件判定 [{currentNode.ConditionType}={currentNode.ConditionValue}] 结果为 {conditionMet}，跳转至目标节点: {targetNodeId}");

                        if (!string.IsNullOrEmpty(targetNodeId))
                        {
                            instance.CurrentNodeId = targetNodeId;
                            continue;
                        }
                        else
                        {
                            AdvanceToNext(instance, def, currentNode);
                            continue;
                        }
                    }
                    else if (currentNode.ActionType.Equals("TERMINAL", StringComparison.OrdinalIgnoreCase) ||
                             currentNode.ActionType.Equals("END", StringComparison.OrdinalIgnoreCase))
                    {
                        instance.Status = WorkflowInstanceStatus.Completed;
                        instance.CompletedAtUtc = DateTime.UtcNow;
                        instance.AppendLog("流程实例到达终止节点，正常结束");
                        break;
                    }
                    else
                    {
                        // 生成纯粹的搬运单下发给调度内核
                        string targetPoint = !string.IsNullOrEmpty(currentNode.TargetNodeId) ? currentNode.TargetNodeId : "STK-01";
                        string orderId = $"WF-ORD-{instance.InstanceId[..6]}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 100000}";

                        var action = currentNode.ActionType switch
                        {
                            "CHARGE" or "AUTO_CHARGE" => ActionType.Charge,
                            "LIFT_UP" or "FORK_PICK" => ActionType.LiftUp,
                            "PUT_DOWN" or "ROLLER_TRANSFER" => ActionType.PutDown,
                            "WAIT" or "PULL_OVER" => ActionType.Wait,
                            _ => ActionType.Move
                        };

                        var transportOrder = new TransportOrderAggregate(orderId, priority: 60, instance.BindVehicleId)
                        {
                            TaskType = currentNode.ActionType,
                            Target = targetPoint,
                            Storage = currentNode.TargetSlotId,
                            CargoSerial = currentNode.CargoId ?? ""
                        };

                        var step = new OrderStep
                        {
                            StepIndex = 0,
                            Name = currentNode.Name,
                            Action = action,
                            TargetNodeId = targetPoint,
                            TargetSlotId = currentNode.TargetSlotId,
                            CargoId = currentNode.CargoId ?? "",
                            DwellSec = currentNode.DwellSec
                        };
                        transportOrder.Steps.Add(step);

                        instance.ActiveTransportOrderId = orderId;
                        instance.Status = WorkflowInstanceStatus.WaitingTransportOrder;
                        instance.AppendLog($"向调度内核下发运输单 {orderId} (目标: {targetPoint}, 动作: {action}, 货位: {currentNode.TargetSlotId}, 载具: {currentNode.CargoId})");

                        submitTransportOrderAction(transportOrder);
                        break;
                    }
                }
            }
        }
    }

    private void AdvanceToNext(WorkflowInstance instance, WorkflowDefinition def, WorkflowNodeDefinition currentNode)
    {
        if (currentNode.IsTerminal || string.IsNullOrEmpty(currentNode.NextNodeId))
        {
            instance.Status = WorkflowInstanceStatus.Completed;
            instance.CompletedAtUtc = DateTime.UtcNow;
            instance.AppendLog("流程实例流转完成，全流程结束");
            return;
        }

        instance.CurrentNodeId = currentNode.NextNodeId;
        var nextNode = def.GetNode(instance.CurrentNodeId);
        instance.AppendLog($"流程推进至下一节点: {nextNode?.Name ?? instance.CurrentNodeId}");

        if (nextNode != null && nextNode.IsTerminal)
        {
            instance.Status = WorkflowInstanceStatus.Completed;
            instance.CompletedAtUtc = DateTime.UtcNow;
            instance.AppendLog("流程实例已到达结束端点");
        }
    }

    public static bool EvaluateCondition(
        VehicleAggregate? vehicle,
        string? conditionType,
        string? conditionValue,
        Func<string, string> getIoSignalFunc,
        string? ioSignalName = null,
        string? rfidValue = null)
    {
        string cType = (conditionType ?? "").ToUpperInvariant();
        string cVal = conditionValue ?? "";

        return cType switch
        {
            "SOC_LESS_THAN" or "BATTERY_LOW" => vehicle != null && double.TryParse(cVal, out double thresh) && vehicle.Bms.SocPercent < thresh,
            "CARGO_LOADED" => vehicle != null && vehicle.HoldingPayload,
            "RFID_MATCH" => !string.IsNullOrEmpty(rfidValue) && rfidValue.Equals(cVal, StringComparison.OrdinalIgnoreCase),
            "IO_SIGNAL" => getIoSignalFunc(ioSignalName ?? "DOCK_READY").Equals(cVal, StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private bool EvaluateCondition(
        WorkflowInstance instance,
        WorkflowNodeDefinition node,
        Func<string, VehicleAggregate?> getVehicleFunc,
        Func<string, string> getIoSignalFunc)
    {
        var vehicle = !string.IsNullOrEmpty(instance.BindVehicleId) ? getVehicleFunc(instance.BindVehicleId) : null;
        string? rfid = instance.Variables.TryGetValue("RFID", out var r) ? r : null;
        return EvaluateCondition(vehicle, node.ConditionType, node.ConditionValue, getIoSignalFunc, node.IoSignalName, rfid);
    }

    private void SeedStandardDefinition()
    {
        var def = new WorkflowDefinition
        {
            Id = "foup-semiconductor-flow",
            Name = "半导体 FOUP 晶圆盒智能转运标准工艺流",
            Description = "工业级标准全闭环：网关对接放行 -> 原料出库 -> 晶圆机台加工 -> 智能低电决策",
            Version = 1,
            VersionTag = "v1.0.0"
        };

        def.Nodes = new List<WorkflowNodeDefinition>
        {
            new() { Id = "N-01", Name = "等待产线装载就绪信号", ActionType = "WAIT_IO_SIGNAL", IoSignalName = "DOCK_READY", ExpectedIoValue = "1", NextNodeId = "N-02" },
            new() { Id = "N-02", Name = "驶往原料区货位取 FOUP 盒", ActionType = "MOVE_TO_POINT", TargetNodeId = "STK-01", TargetSlotId = "A-01", CargoId = "FOUP-8801", NextNodeId = "N-03" },
            new() { Id = "N-03", Name = "运输至主干道十字枢纽", ActionType = "MOVE_TO_POINT", TargetNodeId = "JUNCT-C", NextNodeId = "N-04" },
            new() { Id = "N-04", Name = "送料至晶圆加工机台 WB-01", ActionType = "MOVE_TO_POINT", TargetNodeId = "WB-01", TargetSlotId = "B-01", CargoId = "FOUP-8801", NextNodeId = "N-05" },
            new() { Id = "N-05", Name = "评估电量低电分流决策", ActionType = "DECISION_BRANCH", ConditionType = "SOC_LESS_THAN", ConditionValue = "25", TrueTargetNodeId = "N-06", FalseTargetNodeId = "N-07" },
            new() { Id = "N-06", Name = "电量低于阈值：自主前往大功率快充桩", ActionType = "CHARGE", TargetNodeId = "CH-01", IsTerminal = true },
            new() { Id = "N-07", Name = "电量充足：前往待命停靠区休眠就绪", ActionType = "MOVE_TO_POINT", TargetNodeId = "PARK-01", IsTerminal = true }
        };

        RegisterDefinition(def);
    }
}
