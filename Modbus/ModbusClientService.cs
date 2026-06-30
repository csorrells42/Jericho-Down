using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Net.Sockets;

namespace VoiceWorkbench.Modbus;

public sealed class ModbusClientService
{
    private ushort _transactionId;

    public async Task<ModbusRequestResult> SendTcpAsync(
        string host,
        int port,
        byte unitId,
        byte functionCode,
        ushort address,
        ushort count,
        IReadOnlyList<ushort> values,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        var pdu = BuildPdu(functionCode, address, count, values);
        var transactionId = unchecked(++_transactionId);
        var request = BuildTcpRequest(transactionId, unitId, pdu);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port).WaitAsync(timeout, timeoutSource.Token);
            await using var stream = client.GetStream();
            await stream.WriteAsync(request, timeoutSource.Token);
            await stream.FlushAsync(timeoutSource.Token);

            var header = await ReadExactAsync(stream, 7, timeoutSource.Token);
            var length = (header[4] << 8) | header[5];
            if (length < 2)
            {
                return Fail("Invalid Modbus TCP response length.", request, header);
            }

            var responsePdu = await ReadExactAsync(stream, length - 1, timeoutSource.Token);
            var response = header.Concat(responsePdu).ToArray();
            return ParseResponse(functionCode, address, count, request, response, responsePdu);
        }
        catch (Exception ex)
        {
            return Fail($"Modbus TCP request failed: {ex.Message}", request, []);
        }
    }

    public Task<ModbusRequestResult> SendRtuAsync(
        string portName,
        int baudRate,
        int dataBits,
        Parity parity,
        StopBits stopBits,
        byte unitId,
        byte functionCode,
        ushort address,
        ushort count,
        IReadOnlyList<ushort> values,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var pdu = BuildPdu(functionCode, address, count, values);
            var request = BuildRtuRequest(unitId, pdu);
            try
            {
                using var port = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
                {
                    ReadTimeout = (int)Math.Clamp(timeout.TotalMilliseconds, 250, 10000),
                    WriteTimeout = (int)Math.Clamp(timeout.TotalMilliseconds, 250, 10000)
                };
                port.Open();
                port.DiscardInBuffer();
                port.DiscardOutBuffer();
                port.Write(request, 0, request.Length);

                var response = ReadRtuResponse(port, functionCode, timeout, cancellationToken);
                if (!ValidateRtuCrc(response))
                {
                    return Fail("Invalid Modbus RTU CRC in response.", request, response);
                }

                var responsePdu = response.Skip(1).Take(response.Length - 3).ToArray();
                return ParseResponse(functionCode, address, count, request, response, responsePdu);
            }
            catch (Exception ex)
            {
                return Fail($"Modbus RTU request failed: {ex.Message}", request, []);
            }
        }, cancellationToken);
    }

    public static ushort CalculateCrc(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0xFFFF;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
        }

        return crc;
    }

    private static byte[] BuildPdu(byte functionCode, ushort address, ushort count, IReadOnlyList<ushort> values)
    {
        return functionCode switch
        {
            1 or 2 or 3 or 4 => [(byte)functionCode, High(address), Low(address), High(count), Low(count)],
            5 => BuildWriteSingleCoilPdu(address, values),
            6 => BuildWriteSingleRegisterPdu(address, values),
            15 => BuildWriteMultipleCoilsPdu(address, values),
            16 => BuildWriteMultipleRegistersPdu(address, values),
            _ => throw new InvalidOperationException($"Unsupported Modbus function {functionCode}.")
        };
    }

    private static byte[] BuildWriteSingleCoilPdu(ushort address, IReadOnlyList<ushort> values)
    {
        var on = values.Count > 0 && values[0] != 0;
        return [5, High(address), Low(address), on ? (byte)0xFF : (byte)0x00, 0x00];
    }

    private static byte[] BuildWriteSingleRegisterPdu(ushort address, IReadOnlyList<ushort> values)
    {
        var value = values.Count > 0 ? values[0] : (ushort)0;
        return [6, High(address), Low(address), High(value), Low(value)];
    }

    private static byte[] BuildWriteMultipleCoilsPdu(ushort address, IReadOnlyList<ushort> values)
    {
        var coilCount = (ushort)Math.Max(1, values.Count);
        var byteCount = (coilCount + 7) / 8;
        var pdu = new byte[6 + byteCount];
        pdu[0] = 15;
        pdu[1] = High(address);
        pdu[2] = Low(address);
        pdu[3] = High(coilCount);
        pdu[4] = Low(coilCount);
        pdu[5] = (byte)byteCount;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] != 0)
            {
                pdu[6 + i / 8] |= (byte)(1 << (i % 8));
            }
        }

        return pdu;
    }

    private static byte[] BuildWriteMultipleRegistersPdu(ushort address, IReadOnlyList<ushort> values)
    {
        var registerCount = (ushort)Math.Max(1, values.Count);
        var pdu = new byte[6 + registerCount * 2];
        pdu[0] = 16;
        pdu[1] = High(address);
        pdu[2] = Low(address);
        pdu[3] = High(registerCount);
        pdu[4] = Low(registerCount);
        pdu[5] = (byte)(registerCount * 2);
        for (var i = 0; i < registerCount; i++)
        {
            var value = i < values.Count ? values[i] : (ushort)0;
            pdu[6 + i * 2] = High(value);
            pdu[7 + i * 2] = Low(value);
        }

        return pdu;
    }

    private static byte[] BuildTcpRequest(ushort transactionId, byte unitId, byte[] pdu)
    {
        var request = new byte[7 + pdu.Length];
        request[0] = High(transactionId);
        request[1] = Low(transactionId);
        request[4] = High((ushort)(pdu.Length + 1));
        request[5] = Low((ushort)(pdu.Length + 1));
        request[6] = unitId;
        pdu.CopyTo(request, 7);
        return request;
    }

    private static byte[] BuildRtuRequest(byte unitId, byte[] pdu)
    {
        var request = new byte[1 + pdu.Length + 2];
        request[0] = unitId;
        pdu.CopyTo(request, 1);
        var crc = CalculateCrc(request.AsSpan(0, request.Length - 2));
        request[^2] = Low(crc);
        request[^1] = High(crc);
        return request;
    }

    private static byte[] ReadRtuResponse(SerialPort port, byte functionCode, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        var header = ReadSerialBytes(port, 2, deadline, cancellationToken);
        if ((header[1] & 0x80) != 0)
        {
            return header.Concat(ReadSerialBytes(port, 3, deadline, cancellationToken)).ToArray();
        }

        if (functionCode is 1 or 2 or 3 or 4)
        {
            var byteCount = ReadSerialBytes(port, 1, deadline, cancellationToken);
            return header.Concat(byteCount).Concat(ReadSerialBytes(port, byteCount[0] + 2, deadline, cancellationToken)).ToArray();
        }

        return header.Concat(ReadSerialBytes(port, 6, deadline, cancellationToken)).ToArray();
    }

    private static byte[] ReadSerialBytes(SerialPort port, int count, DateTime deadline, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Timed out waiting for Modbus RTU response.");
            }

            var available = port.BytesToRead;
            if (available <= 0)
            {
                Thread.Sleep(8);
                continue;
            }

            offset += port.Read(buffer, offset, Math.Min(available, count - offset));
        }

        return buffer;
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("The remote Modbus endpoint closed the connection.");
            }

            offset += read;
        }

        return buffer;
    }

    private static bool ValidateRtuCrc(byte[] response)
    {
        if (response.Length < 4)
        {
            return false;
        }

        var actual = (ushort)(response[^2] | (response[^1] << 8));
        var expected = CalculateCrc(response.AsSpan(0, response.Length - 2));
        return actual == expected;
    }

    private static ModbusRequestResult ParseResponse(byte requestFunction, ushort address, ushort count, byte[] request, byte[] response, byte[] responsePdu)
    {
        if (responsePdu.Length < 2)
        {
            return Fail("Modbus response was too short.", request, response);
        }

        var responseFunction = responsePdu[0];
        if ((responseFunction & 0x80) != 0)
        {
            return Fail($"Modbus exception {responsePdu[1]}: {DecodeException(responsePdu[1])}", request, response);
        }

        if (responseFunction != requestFunction)
        {
            return Fail($"Unexpected function code {responseFunction} in response.", request, response);
        }

        var rows = requestFunction switch
        {
            1 or 2 => ParseBitReadRows(requestFunction, address, count, responsePdu),
            3 or 4 => ParseRegisterReadRows(requestFunction, address, responsePdu),
            5 or 6 or 15 or 16 => ParseWriteEchoRows(requestFunction, responsePdu),
            _ => []
        };

        return new ModbusRequestResult
        {
            Success = true,
            Status = $"Modbus function {requestFunction:00} succeeded.",
            RequestBytes = request,
            ResponseBytes = response,
            Values = rows
        };
    }

    private static IReadOnlyList<ModbusValueRow> ParseBitReadRows(byte function, ushort address, ushort count, byte[] pdu)
    {
        if (pdu.Length < 2)
        {
            return [];
        }

        var rows = new List<ModbusValueRow>();
        var kind = function == 1 ? "Coil" : "Discrete Input";
        for (var i = 0; i < count; i++)
        {
            var value = (pdu[2 + i / 8] & (1 << (i % 8))) != 0;
            rows.Add(new ModbusValueRow
            {
                Address = address + i,
                Kind = kind,
                Value = value ? "ON / 1" : "OFF / 0",
                Hex = value ? "01" : "00"
            });
        }

        return rows;
    }

    private static IReadOnlyList<ModbusValueRow> ParseRegisterReadRows(byte function, ushort address, byte[] pdu)
    {
        if (pdu.Length < 2)
        {
            return [];
        }

        var rows = new List<ModbusValueRow>();
        var byteCount = pdu[1];
        var registerCount = byteCount / 2;
        var kind = function == 3 ? "Holding Register" : "Input Register";
        for (var i = 0; i < registerCount && 2 + i * 2 + 1 < pdu.Length; i++)
        {
            var value = (ushort)((pdu[2 + i * 2] << 8) | pdu[3 + i * 2]);
            rows.Add(new ModbusValueRow
            {
                Address = address + i,
                Kind = kind,
                Value = value.ToString(CultureInfo.InvariantCulture),
                Hex = value.ToString("X4", CultureInfo.InvariantCulture)
            });
        }

        return rows;
    }

    private static IReadOnlyList<ModbusValueRow> ParseWriteEchoRows(byte function, byte[] pdu)
    {
        if (pdu.Length < 5)
        {
            return [];
        }

        var address = (pdu[1] << 8) | pdu[2];
        var valueOrCount = (ushort)((pdu[3] << 8) | pdu[4]);
        var kind = function switch
        {
            5 => "Write Single Coil",
            6 => "Write Single Register",
            15 => "Write Multiple Coils",
            16 => "Write Multiple Registers",
            _ => "Write"
        };

        return
        [
            new ModbusValueRow
            {
                Address = address,
                Kind = kind,
                Value = valueOrCount.ToString(CultureInfo.InvariantCulture),
                Hex = valueOrCount.ToString("X4", CultureInfo.InvariantCulture)
            }
        ];
    }

    private static ModbusRequestResult Fail(string status, byte[] request, byte[] response)
    {
        return new ModbusRequestResult
        {
            Success = false,
            Status = status,
            RequestBytes = request,
            ResponseBytes = response
        };
    }

    private static string DecodeException(byte exception)
    {
        return exception switch
        {
            1 => "Illegal function",
            2 => "Illegal data address",
            3 => "Illegal data value",
            4 => "Server device failure",
            5 => "Acknowledge",
            6 => "Server device busy",
            8 => "Memory parity error",
            10 => "Gateway path unavailable",
            11 => "Gateway target failed to respond",
            _ => "Unknown exception"
        };
    }

    private static byte High(ushort value) => (byte)(value >> 8);

    private static byte Low(ushort value) => (byte)value;
}
