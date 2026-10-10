using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Fms.Application.Gateway;

public class SimulatedModbusServer : IDisposable
{
    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private readonly ConcurrentDictionary<int, TcpClient> _clients = new();

    public readonly bool[] Coils = new bool[65536];
    public readonly bool[] DiscreteInputs = new bool[65536];
    public readonly ushort[] HoldingRegisters = new ushort[65536];

    public bool IsRunning { get; private set; } = false;
    public int Port => _port;

    public SimulatedModbusServer(int port = 1502)
    {
        _port = port;
    }

    public void Start()
    {
        if (IsRunning) return;
        try
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            IsRunning = true;
            _listenerTask = Task.Run(() => AcceptClientsAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SimulatedModbusServer] Start error on port {_port}: {ex.Message}");
        }
    }

    public void SetCoil(ushort address, bool value)
    {
        Coils[address] = value;
    }

    public bool GetCoil(ushort address)
    {
        return Coils[address];
    }

    public void SetHoldingRegister(ushort address, ushort value)
    {
        HoldingRegisters[address] = value;
    }

    public ushort GetHoldingRegister(ushort address)
    {
        return HoldingRegisters[address];
    }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        int clientId = 0;
        while (!ct.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                int id = Interlocked.Increment(ref clientId);
                _clients[id] = client;
                _ = Task.Run(() => HandleClientAsync(id, client, ct), ct);
            }
            catch when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    Console.WriteLine($"[SimulatedModbusServer] Accept error: {ex.Message}");
                }
            }
        }
    }

    private async Task HandleClientAsync(int id, TcpClient client, CancellationToken ct)
    {
        using var stream = client.GetStream();
        byte[] headerBuffer = new byte[7]; // MBAP header

        try
        {
            while (!ct.IsCancellationRequested && client.Connected)
            {
                // Read 7 bytes MBAP
                int read = await stream.ReadAsync(headerBuffer.AsMemory(0, 7), ct);
                if (read == 0) break;
                if (read < 7)
                {
                    int remain = 7 - read;
                    while (remain > 0)
                    {
                        int r = await stream.ReadAsync(headerBuffer.AsMemory(7 - remain, remain), ct);
                        if (r == 0) break;
                        remain -= r;
                    }
                }

                ushort txId = BinaryPrimitives.ReadUInt16BigEndian(headerBuffer.AsSpan(0, 2));
                ushort protoId = BinaryPrimitives.ReadUInt16BigEndian(headerBuffer.AsSpan(2, 2));
                ushort length = BinaryPrimitives.ReadUInt16BigEndian(headerBuffer.AsSpan(4, 2));
                byte unitId = headerBuffer[6];

                int pduLen = length - 1;
                byte[] pduBuffer = new byte[pduLen];
                int pduRead = 0;
                while (pduRead < pduLen)
                {
                    int r = await stream.ReadAsync(pduBuffer.AsMemory(pduRead, pduLen - pduRead), ct);
                    if (r == 0) break;
                    pduRead += r;
                }

                byte fc = pduBuffer[0];
                ushort param1 = BinaryPrimitives.ReadUInt16BigEndian(pduBuffer.AsSpan(1, 2));
                ushort param2 = BinaryPrimitives.ReadUInt16BigEndian(pduBuffer.AsSpan(3, 2));

                byte[]? responsePdu = null;

                switch (fc)
                {
                    case 0x01: // Read Coils
                    {
                        ushort start = param1;
                        ushort count = param2;
                        int byteCount = (count + 7) / 8;
                        responsePdu = new byte[2 + byteCount];
                        responsePdu[0] = fc;
                        responsePdu[1] = (byte)byteCount;
                        for (int i = 0; i < count; i++)
                        {
                            if (Coils[start + i])
                            {
                                responsePdu[2 + (i / 8)] |= (byte)(1 << (i % 8));
                            }
                        }
                        break;
                    }
                    case 0x02: // Read Discrete Inputs
                    {
                        ushort start = param1;
                        ushort count = param2;
                        int byteCount = (count + 7) / 8;
                        responsePdu = new byte[2 + byteCount];
                        responsePdu[0] = fc;
                        responsePdu[1] = (byte)byteCount;
                        for (int i = 0; i < count; i++)
                        {
                            if (DiscreteInputs[start + i])
                            {
                                responsePdu[2 + (i / 8)] |= (byte)(1 << (i % 8));
                            }
                        }
                        break;
                    }
                    case 0x03: // Read Holding Registers
                    case 0x04: // Read Input Registers
                    {
                        ushort start = param1;
                        ushort count = param2;
                        responsePdu = new byte[2 + count * 2];
                        responsePdu[0] = fc;
                        responsePdu[1] = (byte)(count * 2);
                        for (int i = 0; i < count; i++)
                        {
                            ushort val = HoldingRegisters[start + i];
                            BinaryPrimitives.WriteUInt16BigEndian(responsePdu.AsSpan(2 + i * 2, 2), val);
                        }
                        break;
                    }
                    case 0x05: // Write Single Coil
                    {
                        ushort addr = param1;
                        bool val = param2 == 0xFF00;
                        Coils[addr] = val;
                        responsePdu = pduBuffer; // Echo
                        break;
                    }
                    case 0x06: // Write Single Register
                    {
                        ushort addr = param1;
                        HoldingRegisters[addr] = param2;
                        responsePdu = pduBuffer; // Echo
                        break;
                    }
                    default:
                    {
                        // Exception response (Illegal Function)
                        responsePdu = new byte[] { (byte)(fc | 0x80), 0x01 };
                        break;
                    }
                }

                if (responsePdu != null)
                {
                    byte[] fullResponse = new byte[7 + responsePdu.Length];
                    BinaryPrimitives.WriteUInt16BigEndian(fullResponse.AsSpan(0, 2), txId);
                    BinaryPrimitives.WriteUInt16BigEndian(fullResponse.AsSpan(2, 2), protoId);
                    BinaryPrimitives.WriteUInt16BigEndian(fullResponse.AsSpan(4, 2), (ushort)(responsePdu.Length + 1));
                    fullResponse[6] = unitId;
                    Array.Copy(responsePdu, 0, fullResponse, 7, responsePdu.Length);

                    await stream.WriteAsync(fullResponse, ct);
                }
            }
        }
        catch { }
        finally
        {
            _clients.TryRemove(id, out _);
            try { client.Close(); client.Dispose(); } catch { }
        }
    }

    public void Stop()
    {
        IsRunning = false;
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        foreach (var c in _clients.Values)
        {
            try { c.Close(); c.Dispose(); } catch { }
        }
        _clients.Clear();
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
