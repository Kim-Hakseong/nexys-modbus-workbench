using Nmw.Core.Client;
using Nmw.Core.Framing;
using Nmw.Core.Polling;
using Nmw.Core.Protocol;

namespace Nmw.Core.Tests;

/// <summary>PollEngine 스케줄/통계 테스트 (M5 DoD).</summary>
public sealed class PollEngineTests
{
    private static FakeTransport HoldingSlave(params ushort[] values)
    {
        var pdu = new byte[2 + (values.Length * 2)];
        pdu[0] = 0x03;
        pdu[1] = (byte)(values.Length * 2);
        for (var i = 0; i < values.Length; i++)
        {
            pdu[2 + (i * 2)] = (byte)(values[i] >> 8);
            pdu[3 + (i * 2)] = (byte)values[i];
        }

        return new FakeTransport
        {
            OnRequest = req => [MbapFraming.BuildAdu((ushort)((req[0] << 8) | req[1]), req[6], pdu)],
        };
    }

    private static PollDefinition Definition(int scanRateMs, ushort quantity = 2) => new()
    {
        Id = "p1",
        Name = "테스트 폴",
        UnitId = 1,
        Function = FunctionCode.ReadHoldingRegisters,
        StartAddress = 0,
        Quantity = quantity,
        ScanRateMs = scanRateMs,
    };

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "대기 조건이 시간 내에 충족되지 않았습니다.");
    }

    [Fact]
    public async Task Poll_PublishesSnapshotsWithValuesAndStats()
    {
        var transport = HoldingSlave(0x0102, 0x0304);
        await using var master = new ModbusMaster(transport);
        await using var engine = new PollEngine(master);
        var snapshots = new List<PollSnapshot>();
        engine.SnapshotPublished += (_, s) =>
        {
            lock (snapshots)
            {
                snapshots.Add(s);
            }
        };

        engine.StartPoll(Definition(scanRateMs: 20));
        await WaitUntilAsync(() =>
        {
            lock (snapshots)
            {
                return snapshots.Count >= 5;
            }
        });
        await engine.StopPollAsync("p1");

        PollSnapshot last;
        lock (snapshots)
        {
            last = snapshots[^1];
        }

        Assert.Equal("p1", last.PollId);
        Assert.Equal(new ushort[] { 0x0102, 0x0304 }, last.Registers);
        Assert.Null(last.Bits);
        Assert.Null(last.LastError);
        Assert.Equal(last.Stats.TxCount, last.Stats.ValidRx);
        Assert.Equal(0, last.Stats.ErrorCount);
        Assert.True(last.Stats.LastResponseMs >= 0);
    }

    [Fact]
    public async Task Poll_AccumulatesErrorsOnException()
    {
        var transport = new FakeTransport
        {
            OnRequest = req =>
                [MbapFraming.BuildAdu((ushort)((req[0] << 8) | req[1]), req[6], TestHex.Bytes("83 02"))],
        };
        await using var master = new ModbusMaster(transport);
        await using var engine = new PollEngine(master);
        var snapshots = new List<PollSnapshot>();
        engine.SnapshotPublished += (_, s) =>
        {
            lock (snapshots)
            {
                snapshots.Add(s);
            }
        };

        engine.StartPoll(Definition(scanRateMs: 20));
        await WaitUntilAsync(() =>
        {
            lock (snapshots)
            {
                return snapshots.Count >= 3;
            }
        });
        await engine.StopPollAsync("p1");

        PollSnapshot last;
        lock (snapshots)
        {
            last = snapshots[^1];
        }

        Assert.Null(last.Registers);
        Assert.NotNull(last.LastError);
        Assert.Equal(ModbusErrorKind.Exception, last.LastError.Kind);
        Assert.Equal(last.Stats.TxCount, last.Stats.ErrorCount);
        Assert.Equal("Illegal Data Address", last.Stats.LastErrorText);
    }

    [Fact]
    public async Task Stop_NoMoreSnapshotsAfterStop()
    {
        var transport = HoldingSlave(1, 2);
        await using var master = new ModbusMaster(transport);
        await using var engine = new PollEngine(master);
        var count = 0;
        engine.SnapshotPublished += (_, _) => Interlocked.Increment(ref count);

        engine.StartPoll(Definition(scanRateMs: 20));
        await WaitUntilAsync(() => Volatile.Read(ref count) >= 3);
        await engine.StopPollAsync("p1");
        Assert.False(engine.IsRunning("p1"));

        var after = Volatile.Read(ref count);
        await Task.Delay(150);
        Assert.Equal(after, Volatile.Read(ref count));
    }

    [Fact]
    public async Task SlowResponse_SkipsTicksInsteadOfQueueing()
    {
        var transport = HoldingSlave(1, 2);
        transport.ReceiveDelay = TimeSpan.FromMilliseconds(80);
        await using var master = new ModbusMaster(transport);
        await using var engine = new PollEngine(master);
        var count = 0;
        engine.SnapshotPublished += (_, _) => Interlocked.Increment(ref count);

        // 10ms 주기지만 응답이 80ms 걸림 → 400ms 동안 틱이 누적되지 않고 skip되어야 한다
        engine.StartPoll(Definition(scanRateMs: 10));
        await Task.Delay(400);
        await engine.StopPollAsync("p1");

        var total = Volatile.Read(ref count);
        Assert.InRange(total, 1, 10);
    }

    [Fact]
    public async Task MultiplePolls_RunIndependently()
    {
        var transport = HoldingSlave(7, 8);
        await using var master = new ModbusMaster(transport);
        await using var engine = new PollEngine(master);
        var perPoll = new Dictionary<string, int>();
        engine.SnapshotPublished += (_, s) =>
        {
            lock (perPoll)
            {
                perPoll[s.PollId] = perPoll.GetValueOrDefault(s.PollId) + 1;
            }
        };

        engine.StartPoll(Definition(scanRateMs: 20) with { Id = "a" });
        engine.StartPoll(Definition(scanRateMs: 20) with { Id = "b" });
        Assert.Equal(2, engine.ActivePollIds.Count);
        await WaitUntilAsync(() =>
        {
            lock (perPoll)
            {
                return perPoll.GetValueOrDefault("a") >= 3 && perPoll.GetValueOrDefault("b") >= 3;
            }
        });
        await engine.StopAllAsync();

        Assert.Empty(engine.ActivePollIds);
    }

    [Fact]
    public async Task StartPoll_RejectsWriteFunctionAndDuplicateId()
    {
        var transport = HoldingSlave(1);
        await using var master = new ModbusMaster(transport);
        await using var engine = new PollEngine(master);

        Assert.Throws<ArgumentException>(() => engine.StartPoll(
            Definition(100) with { Function = FunctionCode.WriteSingleCoil }));

        engine.StartPoll(Definition(scanRateMs: 1000));
        Assert.Throws<InvalidOperationException>(() => engine.StartPoll(Definition(scanRateMs: 1000)));
    }

    [Fact]
    public async Task Poll_ReadCoils_PublishesBits()
    {
        // FC01 응답: 3비트 [T,F,T] → 0x05
        var transport = new FakeTransport
        {
            OnRequest = req =>
                [MbapFraming.BuildAdu((ushort)((req[0] << 8) | req[1]), req[6], TestHex.Bytes("01 01 05"))],
        };
        await using var master = new ModbusMaster(transport);
        await using var engine = new PollEngine(master);
        PollSnapshot? last = null;
        engine.SnapshotPublished += (_, s) => Volatile.Write(ref last, s);

        engine.StartPoll(Definition(scanRateMs: 20, quantity: 3) with
        {
            Function = FunctionCode.ReadCoils,
        });
        await WaitUntilAsync(() => Volatile.Read(ref last) is not null);
        await engine.StopAllAsync();

        var snapshot = Volatile.Read(ref last)!;
        Assert.Null(snapshot.Registers);
        Assert.Equal(new[] { true, false, true }, snapshot.Bits);
    }
}
