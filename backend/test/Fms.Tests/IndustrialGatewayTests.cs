using Fms.Application.Gateway;
using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Vehicles;
using Fms.Engine;
using Xunit;

namespace Fms.Tests;

public class IndustrialGatewayTests
{
    [Fact]
    public async Task ModbusTcpMaster_ReadWriteCoils_AndHoldingRegisters()
    {
        // 1. 启动轻量内存级 Modbus-TCP 服务端
        using var server = new SimulatedModbusServer(11502);
        server.Start();
        Assert.True(server.IsRunning);

        // 设置线圈初始值
        server.SetCoil(100, false);
        server.SetCoil(101, true);
        server.SetHoldingRegister(200, 1234);

        // 2. 客户端发起连接与读写
        using var client = new ModbusTcpMaster("127.0.0.1", 11502);
        await client.ConnectAsync(2000);
        Assert.True(client.IsConnected);

        // 读线圈 100 和 101
        var coils = await client.ReadCoilsAsync(1, 100, 2);
        Assert.False(coils[0]);
        Assert.True(coils[1]);

        // 写线圈 100 为 true
        await client.WriteSingleCoilAsync(1, 100, true);
        Assert.True(server.GetCoil(100));

        // 读保持寄存器 200
        var regs = await client.ReadHoldingRegistersAsync(1, 200, 1);
        Assert.Equal(1234, regs[0]);

        // 写保持寄存器 200 为 5678
        await client.WriteSingleRegisterAsync(1, 200, 5678);
        Assert.Equal(5678, server.GetHoldingRegister(200));
    }

    [Fact]
    public async Task IndustrialGateway_PlcCoilChange_AutomaticallyTriggersDispatchEngineIoSignal()
    {
        var engine = new DispatchEngine();
        Assert.Equal("0", engine.GetIoSignal("DOCK_READY"));

        var gateway = new IndustrialGatewayService(engine, 12502);
        // 连接默认虚拟 Modbus 网关通道
        await gateway.ConnectChannelAsync("ch-modbus-default");

        var channel = gateway.GetChannel("ch-modbus-default");
        Assert.NotNull(channel);
        Assert.Equal(GatewayChannelStatus.Connected, channel.Status);

        // 注册车辆与等待 IO 信号工单
        var v = new VehicleAggregate("AGV-GW", "AGV-GW", new Pose2D(0, 0, 0)) { CurrentNodeId = "STK-01" };
        engine.RegisterVehicle(v);

        var order = new TransportOrderAggregate("ORD-GW-TEST", priority: 80, bindVehicleId: "AGV-GW");
        order.Steps.Add(new OrderStep
        {
            StepIndex = 0,
            StepId = "STEP-01",
            Name = "等待机台 DOCK_READY",
            Action = ActionType.WaitIoSignal,
            IoSignalName = "DOCK_READY",
            ExpectedIoValue = "1"
        });
        order.Steps.Add(new OrderStep
        {
            StepIndex = 1,
            StepId = "STEP-02",
            Name = "放行后工艺动作",
            Action = ActionType.Wait,
            DwellSec = 0.1
        });

        engine.SubmitOrder(order);
        engine.AdvanceTick(0.1);

        // 初始为 0，任务应处于等待状态
        Assert.Equal(0, order.CurrentStepIndex);
        Assert.Equal(OrderStatus.WaitingAction, order.Status);

        // 模拟外部真实 PLC 线圈 100 被外部传感器触发置 1
        await gateway.WriteTagAsync("ch-modbus-default", "Coil:100", "1");

        // 引擎内 IO 信号应当已被网关联动置 1
        Assert.Equal("1", engine.GetIoSignal("DOCK_READY"));

        // 调度循环推进，验证工步成功放行
        engine.AdvanceTick(0.6);
        Assert.Equal(1, order.CurrentStepIndex);
        Assert.Equal("STEP-02", order.CurrentStep?.StepId);

        gateway.Dispose();
    }
}
