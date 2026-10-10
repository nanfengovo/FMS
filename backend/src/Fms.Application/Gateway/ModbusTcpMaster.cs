using System.Buffers.Binary;
using System.Net.Sockets;

namespace Fms.Application.Gateway;

public class ModbusTcpMaster : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private ushort _transactionId = 0;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsConnected => _tcpClient != null && _tcpClient.Connected;

    public ModbusTcpMaster(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public async Task ConnectAsync(int timeoutMs = 3000, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (IsConnected) return;
            DisposeClient();

            _tcpClient = new TcpClient();
            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            await _tcpClient.ConnectAsync(_host, _port, linkedCts.Token);
            _stream = _tcpClient.GetStream();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool[]> ReadCoilsAsync(byte unitId, ushort startAddress, ushort count, CancellationToken ct = default)
    {
        var response = await ExecuteTransactionAsync(unitId, 0x01, startAddress, count, ct);
        if (response.Length < 1) throw new InvalidOperationException("Invalid Modbus response length for ReadCoils");
        byte byteCount = response[0];
        var result = new bool[count];
        for (int i = 0; i < count; i++)
        {
            int byteIndex = 1 + (i / 8);
            int bitIndex = i % 8;
            if (byteIndex < response.Length)
            {
                result[i] = (response[byteIndex] & (1 << bitIndex)) != 0;
            }
        }
        return result;
    }

    public async Task<bool[]> ReadDiscreteInputsAsync(byte unitId, ushort startAddress, ushort count, CancellationToken ct = default)
    {
        var response = await ExecuteTransactionAsync(unitId, 0x02, startAddress, count, ct);
        if (response.Length < 1) throw new InvalidOperationException("Invalid Modbus response length for ReadDiscreteInputs");
        var result = new bool[count];
        for (int i = 0; i < count; i++)
        {
            int byteIndex = 1 + (i / 8);
            int bitIndex = i % 8;
            if (byteIndex < response.Length)
            {
                result[i] = (response[byteIndex] & (1 << bitIndex)) != 0;
            }
        }
        return result;
    }

    public async Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, ushort startAddress, ushort count, CancellationToken ct = default)
    {
        var response = await ExecuteTransactionAsync(unitId, 0x03, startAddress, count, ct);
        if (response.Length < 1 + count * 2) throw new InvalidOperationException("Invalid Modbus response length for ReadHoldingRegisters");
        var result = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(1 + i * 2, 2));
        }
        return result;
    }

    public async Task WriteSingleCoilAsync(byte unitId, ushort address, bool value, CancellationToken ct = default)
    {
        ushort rawValue = value ? (ushort)0xFF00 : (ushort)0x0000;
        await ExecuteTransactionAsync(unitId, 0x05, address, rawValue, ct);
    }

    public async Task WriteSingleRegisterAsync(byte unitId, ushort address, ushort value, CancellationToken ct = default)
    {
        await ExecuteTransactionAsync(unitId, 0x06, address, value, ct);
    }

    private async Task<byte[]> ExecuteTransactionAsync(byte unitId, byte functionCode, ushort param1, ushort param2, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!IsConnected || _stream == null)
            {
                await ConnectAsync(3000, ct);
            }

            ushort txId = unchecked(++_transactionId);
            byte[] request = new byte[12];
            // MBAP Header (7 bytes)
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), txId); // Transaction ID
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), 0);    // Protocol ID (0 = Modbus TCP)
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), 6);    // Length (UnitID + PDU = 1 + 5 = 6)
            request[6] = unitId;                                               // Unit ID
            // PDU (5 bytes)
            request[7] = functionCode;
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8, 2), param1);
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(10, 2), param2);

            await _stream!.WriteAsync(request, ct);

            // Read MBAP response header (7 bytes)
            byte[] headerBuffer = new byte[7];
            await ReadExactAsync(_stream, headerBuffer, 0, 7, ct);

            ushort respLength = BinaryPrimitives.ReadUInt16BigEndian(headerBuffer.AsSpan(4, 2));
            if (respLength < 2) throw new InvalidOperationException($"Invalid Modbus MBAP response length: {respLength}");

            int pduLength = respLength - 1; // subtract UnitId
            byte[] pduBuffer = new byte[pduLength];
            await ReadExactAsync(_stream, pduBuffer, 0, pduLength, ct);

            byte respFunctionCode = pduBuffer[0];
            if ((respFunctionCode & 0x80) != 0)
            {
                byte errorCode = pduBuffer.Length > 1 ? pduBuffer[1] : (byte)0;
                throw new InvalidOperationException($"Modbus Exception Response: Function 0x{functionCode:X2}, ErrorCode: 0x{errorCode:X2}");
            }

            // Return payload data without FunctionCode
            byte[] dataPayload = new byte[pduLength - 1];
            Array.Copy(pduBuffer, 1, dataPayload, 0, dataPayload.Length);
            return dataPayload;
        }
        catch
        {
            DisposeClient();
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), ct);
            if (read == 0) throw new EndOfStreamException("Modbus connection closed unexpectedly");
            totalRead += read;
        }
    }

    private void DisposeClient()
    {
        try { _stream?.Dispose(); } catch { }
        try { _tcpClient?.Close(); _tcpClient?.Dispose(); } catch { }
        _stream = null;
        _tcpClient = null;
    }

    public void Dispose()
    {
        DisposeClient();
        _lock.Dispose();
    }
}
