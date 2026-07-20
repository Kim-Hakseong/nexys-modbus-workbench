using Nmw.Core.Client;
using Nmw.Core.Framing;
using Nmw.Core.Protocol;
using Nmw.Core.Transport;

namespace Nmw.Core.Tests;

/// <summary>ModbusMaster 페이크 트랜스포트 기반 테스트 (M3 DoD).</summary>
public sealed class ModbusMasterTests
{
    private static ushort TxIdOf(byte[] adu) => (ushort)((adu[0] << 8) | adu[1]);

    private static byte[] MbapReply(byte[] requestAdu, byte unitId, string pduHex) =>
        MbapFraming.BuildAdu(TxIdOf(requestAdu), unitId, TestHex.Bytes(pduHex));

    private static byte[] RtuReply(string frameHexWithoutCrc)
    {
        var body = TestHex.Bytes(frameHexWithoutCrc);
        var (lo, hi) = Crc16.ComputeBytes(body);
        return body.Concat(new[] { lo, hi }).ToArray();
    }

    [Fact]
    public async Task ReadHoldingRegisters_Tcp_ReturnsValues()
    {
        var transport = new FakeTransport
        {
            OnRequest = req => [MbapReply(req, 0x11, "03 06 02 2B 00 00 00 64")],
        };
        await using var master = new ModbusMaster(transport);

        var result = await master.ReadHoldingRegistersAsync(0x11, 0x006B, 3);

        Assert.True(result.IsSuccess);
        Assert.Equal(new ushort[] { 0x022B, 0x0000, 0x0064 }, result.Value);
        Assert.True(result.Elapsed >= TimeSpan.Zero);

        var sent = Assert.Single(transport.SentFrames);
        Assert.Equal(TestHex.Bytes("00 00 00 00 00 06 11 03 00 6B 00 03"), sent);
    }

    [Fact]
    public async Task TransactionId_IncrementsPerRequest()
    {
        var transport = new FakeTransport
        {
            OnRequest = req => [MbapReply(req, 1, "03 02 00 2A")],
        };
        await using var master = new ModbusMaster(transport);

        Assert.True((await master.ReadHoldingRegistersAsync(1, 0, 1)).IsSuccess);
        Assert.True((await master.ReadHoldingRegistersAsync(1, 0, 1)).IsSuccess);

        Assert.Equal(2, transport.SentFrames.Count);
        Assert.Equal(0, TxIdOf(transport.SentFrames[0]));
        Assert.Equal(1, TxIdOf(transport.SentFrames[1]));
    }

    [Fact]
    public async Task Timeout_RetriesConfiguredTimesThenFails()
    {
        var transport = new FakeTransport { OnRequest = _ => [] };
        await using var master = new ModbusMaster(
            transport, new ModbusMasterOptions { TimeoutMs = 60, Retries = 2 });

        var result = await master.ReadHoldingRegistersAsync(1, 0, 1);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModbusErrorKind.Timeout, result.Error!.Kind);
        Assert.Equal(3, transport.SentFrames.Count);
    }

    [Fact]
    public async Task Timeout_ThenSuccess_OnRetry()
    {
        var calls = 0;
        var transport = new FakeTransport();
        transport.OnRequest = req => ++calls == 1
            ? []
            : new[] { MbapReply(req, 1, "03 02 00 2A") };
        await using var master = new ModbusMaster(
            transport, new ModbusMasterOptions { TimeoutMs = 60, Retries = 1 });

        var result = await master.ReadHoldingRegistersAsync(1, 0, 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(new ushort[] { 0x002A }, result.Value);
        Assert.Equal(2, transport.SentFrames.Count);
    }

    [Fact]
    public async Task StaleTransactionId_IsDiscardedAndCorrectResponseAccepted()
    {
        var transport = new FakeTransport();
        transport.OnRequest = req =>
        [
            MbapFraming.BuildAdu((ushort)(TxIdOf(req) + 100), 1, TestHex.Bytes("03 02 FF FF")),
            MbapReply(req, 1, "03 02 00 2A"),
        ];
        await using var master = new ModbusMaster(transport);

        var result = await master.ReadHoldingRegistersAsync(1, 0, 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(new ushort[] { 0x002A }, result.Value);
        Assert.Single(transport.SentFrames);
    }

    [Fact]
    public async Task ExceptionResponse_FailsWithoutRetry()
    {
        var transport = new FakeTransport
        {
            OnRequest = req => [MbapReply(req, 1, "83 02")],
        };
        await using var master = new ModbusMaster(
            transport, new ModbusMasterOptions { TimeoutMs = 200, Retries = 2 });

        var result = await master.ReadHoldingRegistersAsync(1, 0x1000, 1);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModbusErrorKind.Exception, result.Error!.Kind);
        Assert.Equal(ModbusExceptionCode.IllegalDataAddress, result.Error.ExceptionCode);
        Assert.Single(transport.SentFrames);
    }

    [Fact]
    public async Task UnitIdMismatch_FailsAsInvalidResponseWithoutRetry()
    {
        var transport = new FakeTransport
        {
            OnRequest = req => [MbapReply(req, 0x22, "03 02 00 2A")],
        };
        await using var master = new ModbusMaster(
            transport, new ModbusMasterOptions { TimeoutMs = 200, Retries = 2 });

        var result = await master.ReadHoldingRegistersAsync(0x11, 0, 1);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModbusErrorKind.InvalidResponse, result.Error!.Kind);
        Assert.Single(transport.SentFrames);
    }

    [Fact]
    public async Task NotConnected_FailsWithTransportClosed()
    {
        var transport = new FakeTransport { IsConnected = false };
        await using var master = new ModbusMaster(transport);

        var result = await master.ReadHoldingRegistersAsync(1, 0, 1);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModbusErrorKind.TransportClosed, result.Error!.Kind);
        Assert.Empty(transport.SentFrames);
    }

    [Fact]
    public async Task WriteSingleRegister_EchoValidated()
    {
        var transport = new FakeTransport
        {
            OnRequest = req => [MbapReply(req, 1, "06 00 01 00 03")],
        };
        await using var master = new ModbusMaster(transport);

        var result = await master.WriteSingleRegisterAsync(1, 0x0001, 0x0003);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task WriteMultipleRegisters_ResponseValidated()
    {
        var transport = new FakeTransport
        {
            OnRequest = req => [MbapReply(req, 0x11, "10 00 01 00 02")],
        };
        await using var master = new ModbusMaster(transport);

        var result = await master.WriteMultipleRegistersAsync(
            0x11, 0x0001, new ushort[] { 0x000A, 0x0102 });

        Assert.True(result.IsSuccess);
        // 요청 ADU 검증 (§7.2 골든 PDU + MBAP)
        var sent = Assert.Single(transport.SentFrames);
        Assert.Equal(TestHex.Bytes("00 00 00 00 00 0B 11 10 00 01 00 02 04 00 0A 01 02"), sent);
    }

    [Fact]
    public async Task Rtu_Read_WorksWithByteWiseDelivery()
    {
        var transport = new FakeTransport { FramingMode = ModbusFramingMode.Rtu };
        transport.OnRequest = _ =>
            RtuReply("11 03 06 02 2B 00 00 00 64").Select(b => new[] { b }).ToArray();
        await using var master = new ModbusMaster(transport);

        var result = await master.ReadHoldingRegistersAsync(0x11, 0x006B, 3);

        Assert.True(result.IsSuccess);
        Assert.Equal(new ushort[] { 0x022B, 0x0000, 0x0064 }, result.Value);
        // 요청 프레임은 §7.1 골든 CRC와 일치해야 한다
        Assert.Equal(TestHex.Bytes("11 03 00 6B 00 03 76 87"), Assert.Single(transport.SentFrames));
        Assert.True(transport.DiscardCount >= 1);
    }

    [Fact]
    public async Task Rtu_CrcMismatch_RetriesThenSucceeds()
    {
        var calls = 0;
        var transport = new FakeTransport { FramingMode = ModbusFramingMode.Rtu };
        transport.OnRequest = _ =>
        {
            var good = RtuReply("11 03 02 00 2A");
            if (++calls == 1)
            {
                good[^1] ^= 0xFF; // CRC 오염
            }

            return [good];
        };
        await using var master = new ModbusMaster(
            transport, new ModbusMasterOptions { TimeoutMs = 200, Retries = 1 });

        var result = await master.ReadHoldingRegistersAsync(0x11, 0, 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, transport.SentFrames.Count);
    }

    [Fact]
    public async Task Rtu_PersistentCrcMismatch_FailsAfterRetries()
    {
        var transport = new FakeTransport { FramingMode = ModbusFramingMode.Rtu };
        transport.OnRequest = _ =>
        {
            var bad = RtuReply("11 03 02 00 2A");
            bad[^1] ^= 0xFF;
            return [bad];
        };
        await using var master = new ModbusMaster(
            transport, new ModbusMasterOptions { TimeoutMs = 200, Retries = 2 });

        var result = await master.ReadHoldingRegistersAsync(0x11, 0, 1);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModbusErrorKind.CrcMismatch, result.Error!.Kind);
        Assert.Equal(3, transport.SentFrames.Count);
    }

    [Fact]
    public async Task Rtu_ExceptionResponse_Parsed()
    {
        var transport = new FakeTransport { FramingMode = ModbusFramingMode.Rtu };
        transport.OnRequest = _ => [TestHex.Bytes("01 83 02 C0 F1")]; // §7.1 골든 프레임
        await using var master = new ModbusMaster(transport);

        var result = await master.ReadHoldingRegistersAsync(1, 0x1000, 1);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModbusExceptionCode.IllegalDataAddress, result.Error!.ExceptionCode);
    }

    [Fact]
    public async Task ConcurrentRequests_AreSerializedInOrder()
    {
        var transport = new FakeTransport
        {
            OnRequest = req => [MbapReply(req, 1, "03 02 00 2A")],
        };
        await using var master = new ModbusMaster(transport);

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => master.ReadHoldingRegistersAsync(1, 0, 1))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Equal(5, transport.SentFrames.Count);
        for (var i = 1; i < 5; i++)
        {
            Assert.Equal(TxIdOf(transport.SentFrames[i - 1]) + 1, TxIdOf(transport.SentFrames[i]));
        }
    }

    [Fact]
    public async Task CallerCancellation_ReturnsTransportClosedResult()
    {
        var transport = new FakeTransport { OnRequest = _ => [] };
        await using var master = new ModbusMaster(
            transport, new ModbusMasterOptions { TimeoutMs = 10_000, Retries = 0 });
        using var cts = new CancellationTokenSource(50);

        var result = await master.ReadHoldingRegistersAsync(1, 0, 1, cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModbusErrorKind.TransportClosed, result.Error!.Kind);
    }

    [Fact]
    public async Task Traffic_RaisesTxAndRxEvents()
    {
        var transport = new FakeTransport
        {
            OnRequest = req => [MbapReply(req, 1, "03 02 00 2A")],
        };
        await using var master = new ModbusMaster(transport);
        var events = new List<TrafficEvent>();
        master.Traffic += (_, e) => events.Add(e);

        Assert.True((await master.ReadHoldingRegistersAsync(1, 0, 1)).IsSuccess);

        Assert.Equal(2, events.Count);
        Assert.Equal(TrafficDirection.Tx, events[0].Direction);
        Assert.Equal(transport.SentFrames[0], events[0].Data);
        Assert.Equal(TrafficDirection.Rx, events[1].Direction);
        Assert.Equal(TestHex.Bytes("00 00 00 00 00 05 01 03 02 00 2A"), events[1].Data);
    }

    [Fact]
    public async Task DisposeAsync_CompletesPendingRequestsWithTransportClosed()
    {
        var transport = new FakeTransport { OnRequest = _ => [] };
        var master = new ModbusMaster(
            transport, new ModbusMasterOptions { TimeoutMs = 10_000, Retries = 0 });

        var pending = master.ReadHoldingRegistersAsync(1, 0, 1);
        await Task.Delay(50);
        await master.DisposeAsync();

        var result = await pending;
        Assert.False(result.IsSuccess);
        Assert.Equal(ModbusErrorKind.TransportClosed, result.Error!.Kind);
    }
}
