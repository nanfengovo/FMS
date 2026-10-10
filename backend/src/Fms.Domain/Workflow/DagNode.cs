using System;
using System.Collections.Generic;

namespace Fms.Domain.Workflow;

/// <summary>
/// 表示低代码业务流中的一个执行节点（支持仙工 RDS 风格的任务拖拽编排）
/// </summary>
public abstract class DagNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<Guid> NextNodeIds { get; set; } = new();
    
    // 返回 true 表示节点执行完成，调度器可以流转到下一个节点
    public abstract bool Execute(WorkflowContext context);
}

public class MoveToNode : DagNode
{
    public string TargetMapNodeId { get; set; } = string.Empty;
    
    public override bool Execute(WorkflowContext context)
    {
        // 核心逻辑：通知底层引擎将车辆导航到目标节点
        // 若到达则返回 true
        return false; 
    }
}

public class CheckRfidNode : DagNode
{
    public string ExpectedRfid { get; set; } = string.Empty;
    
    public override bool Execute(WorkflowContext context)
    {
        // 核心逻辑：发起北向 HTTP 请求（如 /api/v1/xinsong/check_cargo_rfid）校验物料
        return true;
    }
}

public class TriggerEquipmentNode : DagNode
{
    public string EquipmentActionCode { get; set; } = string.Empty; // e.g., "0,0"
    
    public override bool Execute(WorkflowContext context)
    {
        // 核心逻辑：请求设备放行，如 /api/v1/xinsong/robot_permiss_start_action
        return true;
    }
}

public class WorkflowContext
{
    public string VehicleId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    // ... 其他上下文状态
}
