using System.Diagnostics;
using System.Threading.Channels;
using Nmw.Core.Framing;
using Nmw.Core.Protocol;
using Nmw.Core.Transport;

namespace Nmw.Core.Client;

/// <summary>
/// Modbus 마스터 클라이언트. 요청을 Channel 큐로 직렬화해 워커 태스크 1개가 소비하므로
/// 시리얼/TCP 모두 채널당 동시에 1개 요청만 진행된다.
/// 통신 오류는 예외 대신 <see cref="ModbusResult{T}"/>로 반환하며,
/// Timeout/CrcMismatch에 한해 설정된 횟수만큼 재시도한다 (exception 응답은 재시도하지 않음).
/// </summary>
public sealed class ModbusMaster : IAsyncDisposable
{
    private readonly ITransport _transport;
    private readonly ModbusMasterOptions _options;
    private readonly TimeSpan _interFrameDelay;
    private readonly Channel<PendingRequest> _queue;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private readonly MbapAssembler _mbapAssembler = new();
    private readonly byte[] _receiveBuffer = new byte[512];
    private Task<int>? _pendingReceive;
    private ushort _nextTransactionId;

    /// <summary>raw TX/RX 프레임 이벤트. 트래픽 로그 뷰가 구독한다.</summary>
    public event EventHandler<TrafficEvent>? Traffic;

    /// <summary>트랜스포트와 옵션으로 마스터를 만들고 워커를 시작한다.</summary>
    /// <param name="transport">사용할 트랜스포트 (연결은 호출측이 관리).</param>
    /// <param name="options">동작 옵션. null이면 기본값.</param>
    public ModbusMaster(ITransport transport, ModbusMasterOptions? options = null)
    {
        _transport = transport;
        _options = options ?? new ModbusMasterOptions();
        _interFrameDelay = _options.InterFrameDelayMs is { } ms
            ? TimeSpan.FromMilliseconds(ms)
            : transport.InterFrameDelayHint;
        _queue = Channel.CreateUnbounded<PendingRequest>(
            new UnboundedChannelOptions { SingleReader = true });
        _worker = Task.Run(RunWorkerAsync);
    }

    /// <summary>FC01 — 코일을 읽는다.</summary>
    /// <param name="unitId">슬레이브 Unit ID.</param>
    /// <param name="startAddress">시작 주소(0-base).</param>
    /// <param name="quantity">개수 (1..2000).</param>
    /// <param name="ct">취소 토큰.</param>
    public Task<ModbusResult<bool[]>> ReadCoilsAsync(
        byte unitId, ushort startAddress, ushort quantity, CancellationToken ct = default) =>
        ExecuteAsync(
            unitId,
            PduBuilder.BuildReadRequest(FunctionCode.ReadCoils, startAddress, quantity),
            pdu => PduParser.ParseReadBitsResponse(pdu, FunctionCode.ReadCoils, quantity),
            ct);

    /// <summary>FC02 — 접점(Discrete Input)을 읽는다.</summary>
    /// <param name="unitId">슬레이브 Unit ID.</param>
    /// <param name="startAddress">시작 주소(0-base).</param>
    /// <param name="quantity">개수 (1..2000).</param>
    /// <param name="ct">취소 토큰.</param>
    public Task<ModbusResult<bool[]>> ReadDiscreteInputsAsync(
        byte unitId, ushort startAddress, ushort quantity, CancellationToken ct = default) =>
        ExecuteAsync(
            unitId,
            PduBuilder.BuildReadRequest(FunctionCode.ReadDiscreteInputs, startAddress, quantity),
            pdu => PduParser.ParseReadBitsResponse(pdu, FunctionCode.ReadDiscreteInputs, quantity),
            ct);

    /// <summary>FC03 — 홀딩 레지스터를 읽는다.</summary>
    /// <param name="unitId">슬레이브 Unit ID.</param>
    /// <param name="startAddress">시작 주소(0-base).</param>
    /// <param name="quantity">개수 (1..125).</param>
    /// <param name="ct">취소 토큰.</param>
    public Task<ModbusResult<ushort[]>> ReadHoldingRegistersAsync(
        byte unitId, ushort startAddress, ushort quantity, CancellationToken ct = default) =>
        ExecuteAsync(
            unitId,
            PduBuilder.BuildReadRequest(FunctionCode.ReadHoldingRegisters, startAddress, quantity),
            pdu => PduParser.ParseReadRegistersResponse(pdu, FunctionCode.ReadHoldingRegisters, quantity),
            ct);

    /// <summary>FC04 — 입력 레지스터를 읽는다.</summary>
    /// <param name="unitId">슬레이브 Unit ID.</param>
    /// <param name="startAddress">시작 주소(0-base).</param>
    /// <param name="quantity">개수 (1..125).</param>
    /// <param name="ct">취소 토큰.</param>
    public Task<ModbusResult<ushort[]>> ReadInputRegistersAsync(
        byte unitId, ushort startAddress, ushort quantity, CancellationToken ct = default) =>
        ExecuteAsync(
            unitId,
            PduBuilder.BuildReadRequest(FunctionCode.ReadInputRegisters, startAddress, quantity),
            pdu => PduParser.ParseReadRegistersResponse(pdu, FunctionCode.ReadInputRegisters, quantity),
            ct);

    /// <summary>FC05 — 단일 코일을 쓴다.</summary>
    /// <param name="unitId">슬레이브 Unit ID.</param>
    /// <param name="address">코일 주소(0-base).</param>
    /// <param name="on">ON이면 true.</param>
    /// <param name="ct">취소 토큰.</param>
    public Task<ModbusResult<Unit>> WriteSingleCoilAsync(
        byte unitId, ushort address, bool on, CancellationToken ct = default)
    {
        var request = PduBuilder.BuildWriteSingleCoil(address, on);
        return ExecuteAsync(unitId, request, pdu => PduParser.ParseWriteSingleResponse(pdu, request), ct);
    }

    /// <summary>FC06 — 단일 레지스터를 쓴다.</summary>
    /// <param name="unitId">슬레이브 Unit ID.</param>
    /// <param name="address">레지스터 주소(0-base).</param>
    /// <param name="value">쓸 값.</param>
    /// <param name="ct">취소 토큰.</param>
    public Task<ModbusResult<Unit>> WriteSingleRegisterAsync(
        byte unitId, ushort address, ushort value, CancellationToken ct = default)
    {
        var request = PduBuilder.BuildWriteSingleRegister(address, value);
        return ExecuteAsync(unitId, request, pdu => PduParser.ParseWriteSingleResponse(pdu, request), ct);
    }

    /// <summary>FC15 — 다중 코일을 쓴다.</summary>
    /// <param name="unitId">슬레이브 Unit ID.</param>
    /// <param name="startAddress">시작 주소(0-base).</param>
    /// <param name="values">쓸 코일 값들 (1..1968개).</param>
    /// <param name="ct">취소 토큰.</param>
    public Task<ModbusResult<Unit>> WriteMultipleCoilsAsync(
        byte unitId, ushort startAddress, IReadOnlyList<bool> values, CancellationToken ct = default)
    {
        var request = PduBuilder.BuildWriteMultipleCoils(startAddress, values);
        return ExecuteAsync(
            unitId,
            request,
            pdu => PduParser.ParseWriteMultipleResponse(
                pdu, FunctionCode.WriteMultipleCoils, startAddress, (ushort)values.Count),
            ct);
    }

    /// <summary>FC16 — 다중 레지스터를 쓴다.</summary>
    /// <param name="unitId">슬레이브 Unit ID.</param>
    /// <param name="startAddress">시작 주소(0-base).</param>
    /// <param name="values">쓸 레지스터 값들 (1..123개).</param>
    /// <param name="ct">취소 토큰.</param>
    public Task<ModbusResult<Unit>> WriteMultipleRegistersAsync(
        byte unitId, ushort startAddress, IReadOnlyList<ushort> values, CancellationToken ct = default)
    {
        var request = PduBuilder.BuildWriteMultipleRegisters(startAddress, values);
        return ExecuteAsync(
            unitId,
            request,
            pdu => PduParser.ParseWriteMultipleResponse(
                pdu, FunctionCode.WriteMultipleRegisters, startAddress, (ushort)values.Count),
            ct);
    }

    /// <summary>워커를 정지하고 트랜스포트를 정리한다.</summary>
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _queue.Writer.TryComplete();
        await _worker.ConfigureAwait(false);
        _cts.Dispose();
        if (_pendingReceive is { } abandoned)
        {
            // 트랜스포트 dispose로 실패할 수 있는 잔여 수신 태스크의 예외를 관찰 처리한다.
            _ = abandoned.ContinueWith(
                static t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        await _transport.DisposeAsync().ConfigureAwait(false);
    }

    private sealed record PendingRequest(
        byte UnitId,
        byte[] Pdu,
        FunctionCode Function,
        TaskCompletionSource<ModbusResult<byte[]>> Tcs,
        CancellationToken Ct);

    private async Task<ModbusResult<T>> ExecuteAsync<T>(
        byte unitId, byte[] requestPdu, Func<byte[], PduParseResult<T>> parse, CancellationToken ct)
    {
        var raw = await ExecuteRawAsync(unitId, requestPdu, ct).ConfigureAwait(false);
        if (!raw.IsSuccess)
        {
            return ModbusResult<T>.Fail(raw.Error!);
        }

        var parsed = parse(raw.Value);
        return parsed.IsSuccess
            ? ModbusResult<T>.Ok(parsed.Value, raw.Elapsed)
            : ModbusResult<T>.Fail(parsed.Error!);
    }

    private async Task<ModbusResult<byte[]>> ExecuteRawAsync(
        byte unitId, byte[] pdu, CancellationToken ct)
    {
        var pending = new PendingRequest(
            unitId,
            pdu,
            (FunctionCode)pdu[0],
            new TaskCompletionSource<ModbusResult<byte[]>>(TaskCreationOptions.RunContinuationsAsynchronously),
            ct);
        if (!_queue.Writer.TryWrite(pending))
        {
            return ModbusResult<byte[]>.Fail(ModbusError.TransportClosed("마스터가 종료되었습니다"));
        }

        return await pending.Tcs.Task.ConfigureAwait(false);
    }

    private async Task RunWorkerAsync()
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                while (_queue.Reader.TryRead(out var request))
                {
                    ModbusResult<byte[]> result;
                    try
                    {
                        result = await ProcessRequestAsync(request).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        result = ModbusResult<byte[]>.Fail(ModbusError.TransportClosed(ex.Message));
                    }

                    request.Tcs.TrySetResult(result);

                    if (_transport.FramingMode == ModbusFramingMode.Rtu && _interFrameDelay > TimeSpan.Zero)
                    {
                        try
                        {
                            await Task.Delay(_interFrameDelay, _cts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 종료 시퀀스: 아래 finally에서 대기 중 요청을 정리한다.
        }
        finally
        {
            _queue.Writer.TryComplete();
            while (_queue.Reader.TryRead(out var leftover))
            {
                leftover.Tcs.TrySetResult(
                    ModbusResult<byte[]>.Fail(ModbusError.TransportClosed("마스터가 종료되었습니다")));
            }
        }
    }

    private async Task<ModbusResult<byte[]>> ProcessRequestAsync(PendingRequest request)
    {
        var lastError = ModbusError.Timeout();
        var attempts = Math.Max(1, _options.Retries + 1);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (_cts.IsCancellationRequested || request.Ct.IsCancellationRequested)
            {
                return ModbusResult<byte[]>.Fail(ModbusError.TransportClosed("요청이 취소되었습니다"));
            }

            if (!_transport.IsConnected)
            {
                return ModbusResult<byte[]>.Fail(ModbusError.TransportClosed());
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, request.Ct);
            timeoutCts.CancelAfter(_options.TimeoutMs);
            var stopwatch = Stopwatch.StartNew();
            AttemptResult attemptResult;
            try
            {
                attemptResult = _transport.FramingMode == ModbusFramingMode.Mbap
                    ? await AttemptMbapAsync(request, timeoutCts.Token).ConfigureAwait(false)
                    : await AttemptRtuAsync(request, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (_cts.IsCancellationRequested || request.Ct.IsCancellationRequested)
                {
                    return ModbusResult<byte[]>.Fail(ModbusError.TransportClosed("요청이 취소되었습니다"));
                }

                lastError = ModbusError.Timeout();
                continue;
            }
            catch (Exception ex)
            {
                return ModbusResult<byte[]>.Fail(ModbusError.TransportClosed(ex.Message));
            }

            if (attemptResult.Pdu is { } responsePdu)
            {
                return ModbusResult<byte[]>.Ok(responsePdu, stopwatch.Elapsed);
            }

            lastError = attemptResult.Error!;
            if (lastError.Kind is not (ModbusErrorKind.Timeout or ModbusErrorKind.CrcMismatch))
            {
                return ModbusResult<byte[]>.Fail(lastError);
            }
        }

        return ModbusResult<byte[]>.Fail(lastError);
    }

    private readonly record struct AttemptResult(byte[]? Pdu, ModbusError? Error);

    /// <summary>
    /// 수신을 대기하되 소켓 read 자체는 취소하지 않는다 (.NET에서 소켓 수신 취소는 연결을 abort하므로).
    /// 타임아웃/취소 시 진행 중인 read 태스크를 보존해 다음 시도에서 이어받는다.
    /// </summary>
    private async Task<int> AwaitReceiveAsync(CancellationToken waitCt)
    {
        var receive = _pendingReceive ??=
            _transport.ReceiveAsync(_receiveBuffer, CancellationToken.None).AsTask();
        var completed = await Task.WhenAny(
            receive, Task.Delay(Timeout.Infinite, waitCt)).ConfigureAwait(false);
        if (completed == receive)
        {
            _pendingReceive = null;
            return await receive.ConfigureAwait(false);
        }

        // waitCt 발화 (타임아웃 또는 종료) → TaskCanceledException 전파
        await completed.ConfigureAwait(false);
        throw new OperationCanceledException(waitCt);
    }

    private async Task<AttemptResult> AttemptMbapAsync(PendingRequest request, CancellationToken ct)
    {
        var transactionId = unchecked(_nextTransactionId++);
        var adu = MbapFraming.BuildAdu(transactionId, request.UnitId, request.Pdu);
        await _transport.SendAsync(adu, ct).ConfigureAwait(false);
        RaiseTraffic(TrafficDirection.Tx, adu);

        while (true)
        {
            while (_mbapAssembler.TryTakeFrame(out var frame))
            {
                RaiseTraffic(
                    TrafficDirection.Rx,
                    MbapFraming.BuildAdu(frame.TransactionId, frame.UnitId, frame.Pdu));

                if (frame.TransactionId != transactionId)
                {
                    // TxId 불일치 응답은 폐기하고 타임아웃까지 계속 대기한다 (DESIGN §2.4).
                    continue;
                }

                if (frame.UnitId != request.UnitId)
                {
                    return new AttemptResult(null, ModbusError.InvalidResponse(
                        $"UnitId 불일치 (기대 {request.UnitId}, 수신 {frame.UnitId})"));
                }

                return new AttemptResult(frame.Pdu, null);
            }

            if (_mbapAssembler.IsCorrupted)
            {
                var reason = _mbapAssembler.CorruptReason;
                _mbapAssembler.Reset();
                return new AttemptResult(null, ModbusError.InvalidResponse($"MBAP 스트림 손상: {reason}"));
            }

            var read = await AwaitReceiveAsync(ct).ConfigureAwait(false);
            if (read == 0)
            {
                return new AttemptResult(null, ModbusError.TransportClosed("원격이 연결을 종료했습니다"));
            }

            _mbapAssembler.Feed(_receiveBuffer.AsSpan(0, read));
        }
    }

    private async Task<AttemptResult> AttemptRtuAsync(PendingRequest request, CancellationToken ct)
    {
        _transport.DiscardReceiveBuffer();
        var frame = RtuFraming.BuildFrame(request.UnitId, request.Pdu);
        var assembler = new RtuResponseAssembler(request.Function);
        await _transport.SendAsync(frame, ct).ConfigureAwait(false);
        RaiseTraffic(TrafficDirection.Tx, frame);

        var received = new List<byte>();
        while (true)
        {
            var read = await AwaitReceiveAsync(ct).ConfigureAwait(false);
            if (read == 0)
            {
                return new AttemptResult(null, ModbusError.TransportClosed("원격이 연결을 종료했습니다"));
            }

            for (var i = 0; i < read; i++)
            {
                received.Add(_receiveBuffer[i]);
            }

            var status = assembler.Feed(_receiveBuffer.AsSpan(0, read));
            if (status == RtuAssemblyStatus.NeedMoreData)
            {
                continue;
            }

            RaiseTraffic(TrafficDirection.Rx, received.ToArray());
            return status switch
            {
                RtuAssemblyStatus.Complete when assembler.UnitId != request.UnitId =>
                    new AttemptResult(null, ModbusError.InvalidResponse(
                        $"UnitId 불일치 (기대 {request.UnitId}, 수신 {assembler.UnitId})")),
                RtuAssemblyStatus.Complete => new AttemptResult(assembler.GetPdu(), null),
                RtuAssemblyStatus.CrcMismatch => new AttemptResult(null, ModbusError.CrcMismatch()),
                _ => new AttemptResult(null, ModbusError.InvalidResponse("길이 예측이 불가능한 RTU 응답")),
            };
        }
    }

    private void RaiseTraffic(TrafficDirection direction, byte[] data) =>
        Traffic?.Invoke(this, new TrafficEvent(direction, data, DateTimeOffset.Now));
}
