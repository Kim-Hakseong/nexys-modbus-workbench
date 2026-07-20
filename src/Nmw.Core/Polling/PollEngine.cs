using Nmw.Core.Client;
using Nmw.Core.Protocol;

namespace Nmw.Core.Polling;

/// <summary>
/// 주기 폴링 엔진. 폴마다 독립 async 루프(<see cref="PeriodicTimer"/>)를 돌리며,
/// 실제 요청은 채널의 <see cref="ModbusMaster"/> 큐로 직렬화되므로 시리얼에서도 안전하다.
/// 결과는 불변 <see cref="PollSnapshot"/>으로 발행한다 (UI는 Dispatcher로 마샬링).
/// 이전 요청이 끝나지 않은 틱은 자연히 skip된다 (루프 내 await + PeriodicTimer 백프레셔).
/// </summary>
public sealed class PollEngine : IAsyncDisposable
{
    /// <summary>Scan rate 하한(ms).</summary>
    public const int MinScanRateMs = 10;

    private readonly ModbusMaster _master;
    private readonly object _gate = new();
    private readonly Dictionary<string, (CancellationTokenSource Cts, Task Task)> _polls = [];

    /// <summary>폴 수행마다 발행되는 스냅샷 이벤트 (폴 태스크 스레드에서 호출됨).</summary>
    public event EventHandler<PollSnapshot>? SnapshotPublished;

    /// <summary>마스터를 공유하는 폴링 엔진을 만든다.</summary>
    /// <param name="master">요청을 보낼 마스터.</param>
    public PollEngine(ModbusMaster master)
    {
        _master = master;
    }

    /// <summary>현재 실행 중인 폴 ID 목록.</summary>
    public IReadOnlyList<string> ActivePollIds
    {
        get
        {
            lock (_gate)
            {
                return [.. _polls.Keys];
            }
        }
    }

    /// <summary>해당 폴이 실행 중인지 확인한다.</summary>
    /// <param name="pollId">폴 ID.</param>
    public bool IsRunning(string pollId)
    {
        lock (_gate)
        {
            return _polls.ContainsKey(pollId);
        }
    }

    /// <summary>폴을 시작한다. 읽기 function(FC01~04)만 허용된다.</summary>
    /// <param name="definition">폴 정의.</param>
    public void StartPoll(PollDefinition definition)
    {
        if (definition.Function is not (FunctionCode.ReadCoils or FunctionCode.ReadDiscreteInputs
            or FunctionCode.ReadHoldingRegisters or FunctionCode.ReadInputRegisters))
        {
            throw new ArgumentException(
                $"폴은 읽기 function(FC01~04)만 가능합니다: {definition.Function}", nameof(definition));
        }

        lock (_gate)
        {
            if (_polls.ContainsKey(definition.Id))
            {
                throw new InvalidOperationException($"이미 실행 중인 폴입니다: {definition.Id}");
            }

            var cts = new CancellationTokenSource();
            var task = Task.Run(() => RunPollAsync(definition, cts.Token));
            _polls[definition.Id] = (cts, task);
        }
    }

    /// <summary>폴을 정지하고 루프 종료를 대기한다. 없으면 무시.</summary>
    /// <param name="pollId">폴 ID.</param>
    public async Task StopPollAsync(string pollId)
    {
        (CancellationTokenSource Cts, Task Task) entry;
        lock (_gate)
        {
            if (!_polls.Remove(pollId, out entry))
            {
                return;
            }
        }

        await entry.Cts.CancelAsync().ConfigureAwait(false);
        await entry.Task.ConfigureAwait(false);
        entry.Cts.Dispose();
    }

    /// <summary>모든 폴을 정지한다.</summary>
    public async Task StopAllAsync()
    {
        List<string> ids;
        lock (_gate)
        {
            ids = [.. _polls.Keys];
        }

        foreach (var id in ids)
        {
            await StopPollAsync(id).ConfigureAwait(false);
        }
    }

    /// <summary>모든 폴을 정지하고 엔진을 정리한다 (마스터는 소유하지 않음).</summary>
    public async ValueTask DisposeAsync() => await StopAllAsync().ConfigureAwait(false);

    private async Task RunPollAsync(PollDefinition definition, CancellationToken ct)
    {
        long txCount = 0, validRx = 0, errorCount = 0;
        double lastResponseMs = 0;
        string? lastErrorText = null;

        var scanRate = Math.Max(MinScanRateMs, definition.ScanRateMs);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(scanRate));
        try
        {
            do
            {
                txCount++;
                var (registers, bits, error, elapsed) = await ReadOnceAsync(definition, ct)
                    .ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                if (error is null)
                {
                    validRx++;
                    lastResponseMs = elapsed.TotalMilliseconds;
                }
                else
                {
                    errorCount++;
                    lastErrorText = error.Text;
                }

                var snapshot = new PollSnapshot(
                    definition.Id,
                    registers,
                    bits,
                    new PollStats(txCount, validRx, errorCount, lastResponseMs, lastErrorText),
                    DateTimeOffset.Now,
                    error);
                SnapshotPublished?.Invoke(this, snapshot);
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // 정지 요청 — 정상 종료
        }
    }

    private async Task<(ushort[]? Registers, bool[]? Bits, ModbusError? Error, TimeSpan Elapsed)>
        ReadOnceAsync(PollDefinition definition, CancellationToken ct)
    {
        switch (definition.Function)
        {
            case FunctionCode.ReadCoils:
            {
                var result = await _master.ReadCoilsAsync(
                    definition.UnitId, definition.StartAddress, definition.Quantity, ct)
                    .ConfigureAwait(false);
                return result.IsSuccess
                    ? (null, result.Value, null, result.Elapsed)
                    : (null, null, result.Error, TimeSpan.Zero);
            }

            case FunctionCode.ReadDiscreteInputs:
            {
                var result = await _master.ReadDiscreteInputsAsync(
                    definition.UnitId, definition.StartAddress, definition.Quantity, ct)
                    .ConfigureAwait(false);
                return result.IsSuccess
                    ? (null, result.Value, null, result.Elapsed)
                    : (null, null, result.Error, TimeSpan.Zero);
            }

            case FunctionCode.ReadInputRegisters:
            {
                var result = await _master.ReadInputRegistersAsync(
                    definition.UnitId, definition.StartAddress, definition.Quantity, ct)
                    .ConfigureAwait(false);
                return result.IsSuccess
                    ? (result.Value, null, null, result.Elapsed)
                    : (null, null, result.Error, TimeSpan.Zero);
            }

            default:
            {
                var result = await _master.ReadHoldingRegistersAsync(
                    definition.UnitId, definition.StartAddress, definition.Quantity, ct)
                    .ConfigureAwait(false);
                return result.IsSuccess
                    ? (result.Value, null, null, result.Elapsed)
                    : (null, null, result.Error, TimeSpan.Zero);
            }
        }
    }
}
