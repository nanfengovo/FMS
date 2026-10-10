using System.Collections.Concurrent;
using System.Diagnostics;
using Fms.Engine;

namespace Fms.Application.Gateway;

public interface IIndustrialGatewayService : IDisposable
{
    IReadOnlyCollection<GatewayChannelConfig> GetChannels();
    GatewayChannelConfig? GetChannel(string id);
    Task<GatewayChannelConfig> SaveChannelAsync(CreateChannelDto dto);
    bool DeleteChannel(string id);
    Task ConnectChannelAsync(string id);
    void DisconnectChannel(string id);
    Task<bool> WriteTagAsync(string channelId, string tagAddress, string value);
    string ReadSignal(string signalName);
    void UpdateSignal(string signalName, string value);
    SimulatedModbusServer SimulatedServer { get; }
}

public class IndustrialGatewayService : IIndustrialGatewayService
{
    private readonly DispatchEngine _engine;
    private readonly ConcurrentDictionary<string, GatewayChannelConfig> _channels = new();
    private readonly ConcurrentDictionary<string, ModbusTcpMaster> _modbusClients = new();
    private readonly SimulatedModbusServer _simulatedModbusServer;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pollLoopTask;

    public SimulatedModbusServer SimulatedServer => _simulatedModbusServer;

    public IndustrialGatewayService(DispatchEngine engine, int simulatedPort = 1502)
    {
        _engine = engine;
        _simulatedModbusServer = new SimulatedModbusServer(simulatedPort);
        _engine.SetGatewaySignalReader(ReadSignal);
        InitializeDefaultChannels(simulatedPort);
        _pollLoopTask = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    private void InitializeDefaultChannels(int simulatedPort)
    {
        // 自动启动本地虚拟 Modbus-TCP 服务端，提供开箱即用的工业测试闭环
        _simulatedModbusServer.Start();

        // 默认将模拟服务端线圈 100 置为 0 (对应 DOCK_READY), 101 置为 0 (对应 LINE_CLEAR), 102 置为 0 (对应 MATERIAL_ARRIVED)
        _simulatedModbusServer.SetCoil(100, false);
        _simulatedModbusServer.SetCoil(101, false);
        _simulatedModbusServer.SetCoil(102, false);

        var defaultModbus = new GatewayChannelConfig
        {
            Id = "ch-modbus-default",
            Name = "半导体洁净室机台 PLC 网关 (Modbus-TCP)",
            Protocol = GatewayProtocol.ModbusTcp,
            Host = "127.0.0.1",
            Port = simulatedPort,
            UnitId = 1,
            PollIntervalMs = 200,
            IsEnabled = true,
            Status = GatewayChannelStatus.Disconnected,
            TagMappings = new List<GatewayTagMapping>
            {
                new()
                {
                    TagAddress = "Coil:100",
                    SignalName = "DOCK_READY",
                    Direction = TagDirection.Bidirectional,
                    LastValue = "0",
                    Description = "Stocker 货架装载对接就绪信号 (Coil 100)",
                    UpdatedAt = DateTime.UtcNow
                },
                new()
                {
                    TagAddress = "Coil:101",
                    SignalName = "LINE_CLEAR",
                    Direction = TagDirection.Bidirectional,
                    LastValue = "0",
                    Description = "核心走廊安全光栅出清信号 (Coil 101)",
                    UpdatedAt = DateTime.UtcNow
                },
                new()
                {
                    TagAddress = "Coil:102",
                    SignalName = "MATERIAL_ARRIVED",
                    Direction = TagDirection.Bidirectional,
                    LastValue = "0",
                    Description = "线边料架物料已就绪信号 (Coil 102)",
                    UpdatedAt = DateTime.UtcNow
                }
            }
        };

        var defaultMqtt = new GatewayChannelConfig
        {
            Id = "ch-mqtt-default",
            Name = "MES / SCADA 工业物联网 MQTT 网关",
            Protocol = GatewayProtocol.Mqtt,
            Host = "127.0.0.1",
            Port = 1883,
            PollIntervalMs = 500,
            IsEnabled = false,
            Status = GatewayChannelStatus.Disconnected,
            TagMappings = new List<GatewayTagMapping>
            {
                new()
                {
                    TagAddress = "Topic:factory/line1/dock_ready",
                    SignalName = "DOCK_READY",
                    Direction = TagDirection.ReadFromPlc,
                    LastValue = "0",
                    Description = "产线 A MQTT 消息就绪订阅",
                    UpdatedAt = DateTime.UtcNow
                }
            }
        };

        _channels[defaultModbus.Id] = defaultModbus;
        _channels[defaultMqtt.Id] = defaultMqtt;
    }

    public IReadOnlyCollection<GatewayChannelConfig> GetChannels() => _channels.Values.ToList();

    public GatewayChannelConfig? GetChannel(string id) => _channels.TryGetValue(id, out var c) ? c : null;

    public Task<GatewayChannelConfig> SaveChannelAsync(CreateChannelDto dto)
    {
        string id = string.IsNullOrWhiteSpace(dto.Id) ? $"ch-{Guid.NewGuid():N}"[..12] : dto.Id;
        var channel = new GatewayChannelConfig
        {
            Id = id,
            Name = dto.Name,
            Protocol = dto.Protocol,
            Host = dto.Host,
            Port = dto.Port,
            UnitId = dto.UnitId,
            PollIntervalMs = Math.Max(50, dto.PollIntervalMs),
            IsEnabled = dto.IsEnabled,
            Status = GatewayChannelStatus.Disconnected,
            TagMappings = dto.TagMappings ?? new List<GatewayTagMapping>()
        };

        _channels[id] = channel;
        return Task.FromResult(channel);
    }

    public bool DeleteChannel(string id)
    {
        DisconnectChannel(id);
        return _channels.TryRemove(id, out _);
    }

    public async Task ConnectChannelAsync(string id)
    {
        if (!_channels.TryGetValue(id, out var channel)) return;
        channel.Status = GatewayChannelStatus.Connecting;

        if (channel.Protocol == GatewayProtocol.ModbusTcp)
        {
            try
            {
                var client = new ModbusTcpMaster(channel.Host, channel.Port);
                var sw = Stopwatch.StartNew();
                await client.ConnectAsync(3000);
                sw.Stop();

                _modbusClients[id] = client;
                channel.Status = GatewayChannelStatus.Connected;
                channel.LatencyMs = sw.ElapsedMilliseconds;
                channel.LastError = null;
                channel.LastSyncUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                channel.Status = GatewayChannelStatus.Error;
                channel.LastError = $"连接失败: {ex.Message}";
            }
        }
        else
        {
            // MQTT / 模拟协议
            channel.Status = GatewayChannelStatus.Connected;
            channel.LastError = null;
            channel.LastSyncUtc = DateTime.UtcNow;
        }
    }

    public void DisconnectChannel(string id)
    {
        if (_modbusClients.TryRemove(id, out var client))
        {
            client.Dispose();
        }
        if (_channels.TryGetValue(id, out var ch))
        {
            ch.Status = GatewayChannelStatus.Disconnected;
        }
    }

    public async Task<bool> WriteTagAsync(string channelId, string tagAddress, string value)
    {
        if (!_channels.TryGetValue(channelId, out var channel)) return false;

        var mapping = channel.TagMappings.FirstOrDefault(m => 
            m.TagAddress.Equals(tagAddress, StringComparison.OrdinalIgnoreCase) ||
            m.TagAddress.Equals($"Coil:{tagAddress}", StringComparison.OrdinalIgnoreCase) ||
            m.TagAddress.Equals($"HoldingRegister:{tagAddress}", StringComparison.OrdinalIgnoreCase));
        if (mapping == null) return false;

        mapping.LastValue = value;
        mapping.UpdatedAt = DateTime.UtcNow;

        if (channel.Protocol == GatewayProtocol.ModbusTcp)
        {
            if (!_modbusClients.TryGetValue(channelId, out var client) || !client.IsConnected)
            {
                await ConnectChannelAsync(channelId);
                _modbusClients.TryGetValue(channelId, out client);
            }

            string targetAddrStr = mapping.TagAddress;
            if (targetAddrStr.StartsWith("Coil:", StringComparison.OrdinalIgnoreCase))
            {
                if (ushort.TryParse(targetAddrStr[5..], out ushort addr))
                {
                    bool boolVal = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (channel.Host == "127.0.0.1" && channel.Port == _simulatedModbusServer.Port)
                    {
                        _simulatedModbusServer.SetCoil(addr, boolVal);
                    }
                    if (client != null && client.IsConnected)
                    {
                        try { await client.WriteSingleCoilAsync(channel.UnitId, addr, boolVal); } catch { }
                    }
                }
            }
            else if (targetAddrStr.StartsWith("HoldingRegister:", StringComparison.OrdinalIgnoreCase))
            {
                if (ushort.TryParse(targetAddrStr[16..], out ushort addr) && ushort.TryParse(value, out ushort regVal))
                {
                    if (channel.Host == "127.0.0.1" && channel.Port == _simulatedModbusServer.Port)
                    {
                        _simulatedModbusServer.SetHoldingRegister(addr, regVal);
                    }
                    if (client != null && client.IsConnected)
                    {
                        try { await client.WriteSingleRegisterAsync(channel.UnitId, addr, regVal); } catch { }
                    }
                }
            }
        }

        // 同步通知调度引擎 IO 信号状态
        _engine.SetIoSignal(mapping.SignalName, value);
        return true;
    }

    /// <summary>
    /// 从工业网关活跃通道采集标签中实时读取信号，未命中或通道未连通时回退至调度引擎内部信号
    /// </summary>
    public string ReadSignal(string signalName)
    {
        foreach (var ch in _channels.Values)
        {
            if (!ch.IsEnabled) continue;
            var mapping = ch.TagMappings.FirstOrDefault(m => string.Equals(m.SignalName, signalName, StringComparison.OrdinalIgnoreCase));
            if (mapping != null && ch.Status == GatewayChannelStatus.Connected && !string.IsNullOrEmpty(mapping.LastValue))
            {
                return mapping.LastValue;
            }
        }
        return _engine.GetIoSignal(signalName);
    }

    /// <summary>
    /// 双向同步信号至网关映射标签及底层模拟 PLC 线圈
    /// </summary>
    public void UpdateSignal(string signalName, string value)
    {
        foreach (var ch in _channels.Values)
        {
            var mapping = ch.TagMappings.FirstOrDefault(m => string.Equals(m.SignalName, signalName, StringComparison.OrdinalIgnoreCase));
            if (mapping != null)
            {
                mapping.LastValue = value;
                mapping.UpdatedAt = DateTime.UtcNow;
                if (ch.Protocol == GatewayProtocol.ModbusTcp && mapping.TagAddress.StartsWith("Coil:", StringComparison.OrdinalIgnoreCase))
                {
                    if (ushort.TryParse(mapping.TagAddress[5..], out ushort addr))
                    {
                        _simulatedModbusServer.SetCoil(addr, value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
        }
    }

    private async Task PollLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var channel in _channels.Values)
            {
                if (!channel.IsEnabled) continue;

                try
                {
                    if (channel.Protocol == GatewayProtocol.ModbusTcp)
                    {
                        await PollModbusChannelAsync(channel, stoppingToken);
                    }
                    else if (channel.Protocol == GatewayProtocol.Mqtt)
                    {
                        // 心跳同步
                        if (channel.Status == GatewayChannelStatus.Connected)
                        {
                            channel.LastSyncUtc = DateTime.UtcNow;
                        }
                    }
                }
                catch (Exception ex)
                {
                    channel.Status = GatewayChannelStatus.Error;
                    channel.LastError = ex.Message;
                }
            }

            await Task.Delay(200, stoppingToken);
        }
    }

    private async Task PollModbusChannelAsync(GatewayChannelConfig channel, CancellationToken ct)
    {
        if (!_modbusClients.TryGetValue(channel.Id, out var client) || !client.IsConnected)
        {
            try
            {
                var newClient = new ModbusTcpMaster(channel.Host, channel.Port);
                var sw = Stopwatch.StartNew();
                await newClient.ConnectAsync(2000, ct);
                sw.Stop();
                _modbusClients[channel.Id] = newClient;
                client = newClient;
                channel.Status = GatewayChannelStatus.Connected;
                channel.LatencyMs = sw.ElapsedMilliseconds;
                channel.LastError = null;
            }
            catch (Exception ex)
            {
                channel.Status = GatewayChannelStatus.Error;
                channel.LastError = $"连接失败: {ex.Message}";
                return;
            }
        }

        var pollSw = Stopwatch.StartNew();
        foreach (var mapping in channel.TagMappings)
        {
            if (mapping.Direction == TagDirection.WriteToPlc) continue;

            string addr = mapping.TagAddress;
            if (addr.StartsWith("Coil:", StringComparison.OrdinalIgnoreCase))
            {
                if (ushort.TryParse(addr[5..], out ushort coilAddr))
                {
                    var coils = await client.ReadCoilsAsync(channel.UnitId, coilAddr, 1, ct);
                    if (coils.Length > 0)
                    {
                        string valStr = coils[0] ? "1" : "0";
                        if (mapping.LastValue != valStr)
                        {
                            mapping.LastValue = valStr;
                            mapping.UpdatedAt = DateTime.UtcNow;
                            // 核心联动：PLC 线圈电平跳变直接驱动 Fms 调度引擎 IO 信号！
                            _engine.SetIoSignal(mapping.SignalName, valStr);
                        }
                    }
                }
            }
            else if (addr.StartsWith("DiscreteInput:", StringComparison.OrdinalIgnoreCase))
            {
                if (ushort.TryParse(addr[14..], out ushort diAddr))
                {
                    var dis = await client.ReadDiscreteInputsAsync(channel.UnitId, diAddr, 1, ct);
                    if (dis.Length > 0)
                    {
                        string valStr = dis[0] ? "1" : "0";
                        if (mapping.LastValue != valStr)
                        {
                            mapping.LastValue = valStr;
                            mapping.UpdatedAt = DateTime.UtcNow;
                            _engine.SetIoSignal(mapping.SignalName, valStr);
                        }
                    }
                }
            }
            else if (addr.StartsWith("HoldingRegister:", StringComparison.OrdinalIgnoreCase))
            {
                if (ushort.TryParse(addr[16..], out ushort regAddr))
                {
                    var regs = await client.ReadHoldingRegistersAsync(channel.UnitId, regAddr, 1, ct);
                    if (regs.Length > 0)
                    {
                        string valStr = regs[0].ToString();
                        if (mapping.LastValue != valStr)
                        {
                            mapping.LastValue = valStr;
                            mapping.UpdatedAt = DateTime.UtcNow;
                            _engine.SetIoSignal(mapping.SignalName, valStr);
                        }
                    }
                }
            }
        }
        pollSw.Stop();
        channel.LatencyMs = pollSw.ElapsedMilliseconds;
        channel.LastSyncUtc = DateTime.UtcNow;
    }

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
        }
        catch { }

        _simulatedModbusServer.Dispose();
        foreach (var client in _modbusClients.Values)
        {
            client.Dispose();
        }
        _modbusClients.Clear();
        _cts.Dispose();
    }
}
