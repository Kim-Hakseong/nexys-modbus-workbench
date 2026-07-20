using System.Net;
using System.Net.Sockets;
using Nmw.Core.Framing;

namespace Nmw.Core.Simulator;

/// <summary>시뮬레이터 옵션.</summary>
public sealed record SimulatorOptions
{
    /// <summary>리슨 포트 (0이면 임시 포트). 기본 1502 (502는 Windows에서 관리자 권한 필요).</summary>
    public int Port { get; init; } = 1502;

    /// <summary>각 데이터 영역(홀딩/입력 레지스터, 코일, 접점)의 주소 개수 (1..65536).</summary>
    public int AreaSize { get; init; } = 1000;
}

/// <summary>
/// 내장 Modbus TCP 슬레이브 시뮬레이터. 물리 장비 없이 마스터 기능을 테스트하기 위한
/// 인메모리 슬레이브로, FC01~06, 15, 16을 완전 지원하고 범위 밖 주소에는
/// Illegal Data Address(0x02)를 반환한다. 루프백(127.0.0.1)에만 바인딩하며
/// 다중 클라이언트 동시 접속을 지원한다. 모든 Unit ID에 응답(에코)한다.
/// 데이터는 <see cref="SimulatorDataStore"/>에 저장되며 RTU 슬레이브와 공유할 수 있다.
/// </summary>
public sealed class ModbusTcpSimulator : IAsyncDisposable
{
    private readonly SimulatorDataStore _store;
    private readonly List<Task> _clientTasks = [];

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private long _requestCount;
    private int _clientCount;

    /// <summary>옵션과 (선택) 공유 데이터 저장소로 시뮬레이터를 만든다.</summary>
    /// <param name="options">옵션. null이면 기본값.</param>
    /// <param name="store">공유할 데이터 저장소. null이면 옵션의 AreaSize로 새로 만든다.</param>
    public ModbusTcpSimulator(SimulatorOptions? options = null, SimulatorDataStore? store = null)
    {
        Options = options ?? new SimulatorOptions();
        _store = store ?? new SimulatorDataStore(Options.AreaSize);
        _store.DataChangedByMaster += OnStoreDataChanged;
    }

    /// <summary>적용된 옵션. 공유 저장소를 받았다면 영역 크기는 저장소 기준이다.</summary>
    public SimulatorOptions Options { get; }

    /// <summary>데이터 저장소 (RTU 슬레이브와 공유 가능).</summary>
    public SimulatorDataStore Store => _store;

    /// <summary>리슨 중 여부.</summary>
    public bool IsRunning => _listener is not null;

    /// <summary>실제 바인딩된 포트 (Start 후 유효).</summary>
    public int Port { get; private set; }

    /// <summary>처리한 요청 프레임 수.</summary>
    public long RequestCount => Interlocked.Read(ref _requestCount);

    /// <summary>현재 접속 중인 클라이언트 수.</summary>
    public int ClientCount => Volatile.Read(ref _clientCount);

    /// <summary>마스터의 쓰기 요청(FC05/06/15/16)으로 데이터가 바뀔 때 발생 (워커 스레드).</summary>
    public event EventHandler? DataChangedByMaster;

    /// <summary>리슨을 시작한다.</summary>
    /// <exception cref="InvalidOperationException">이미 실행 중.</exception>
    /// <exception cref="SocketException">포트 사용 중 등 바인딩 실패.</exception>
    public void Start()
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("시뮬레이터가 이미 실행 중입니다.");
        }

        var listener = new TcpListener(IPAddress.Loopback, Options.Port);
        listener.Start();
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
    }

    /// <summary>리슨을 정지하고 모든 연결을 정리한다. 정지 후 다시 Start 가능.</summary>
    public async Task StopAsync()
    {
        if (_listener is not { } listener || _cts is not { } cts)
        {
            return;
        }

        _listener = null;
        await cts.CancelAsync().ConfigureAwait(false);
        listener.Stop();
        if (_acceptLoop is { } acceptLoop)
        {
            await acceptLoop.ConfigureAwait(false);
        }

        Task[] clients;
        lock (_clientTasks)
        {
            clients = [.. _clientTasks];
            _clientTasks.Clear();
        }

        await Task.WhenAll(clients).ConfigureAwait(false);
        cts.Dispose();
        _cts = null;
        _acceptLoop = null;
    }

    /// <summary>정지 후 자원을 정리한다.</summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _store.DataChangedByMaster -= OnStoreDataChanged;
    }

    /// <summary>홀딩 레지스터 값을 읽는다.</summary>
    /// <param name="address">주소 (0-base).</param>
    public ushort GetHoldingRegister(int address) => _store.GetHoldingRegister(address);

    /// <summary>홀딩 레지스터 값을 쓴다.</summary>
    /// <param name="address">주소 (0-base).</param>
    /// <param name="value">값.</param>
    public void SetHoldingRegister(int address, ushort value) => _store.SetHoldingRegister(address, value);

    /// <summary>입력 레지스터 값을 읽는다.</summary>
    /// <param name="address">주소 (0-base).</param>
    public ushort GetInputRegister(int address) => _store.GetInputRegister(address);

    /// <summary>입력 레지스터 값을 쓴다 (시뮬레이터 전용 — 마스터는 읽기만 가능).</summary>
    /// <param name="address">주소 (0-base).</param>
    /// <param name="value">값.</param>
    public void SetInputRegister(int address, ushort value) => _store.SetInputRegister(address, value);

    /// <summary>코일 값을 읽는다.</summary>
    /// <param name="address">주소 (0-base).</param>
    public bool GetCoil(int address) => _store.GetCoil(address);

    /// <summary>코일 값을 쓴다.</summary>
    /// <param name="address">주소 (0-base).</param>
    /// <param name="value">값.</param>
    public void SetCoil(int address, bool value) => _store.SetCoil(address, value);

    /// <summary>접점 값을 읽는다.</summary>
    /// <param name="address">주소 (0-base).</param>
    public bool GetDiscreteInput(int address) => _store.GetDiscreteInput(address);

    /// <summary>접점 값을 쓴다 (시뮬레이터 전용 — 마스터는 읽기만 가능).</summary>
    /// <param name="address">주소 (0-base).</param>
    /// <param name="value">값.</param>
    public void SetDiscreteInput(int address, bool value) => _store.SetDiscreteInput(address, value);

    /// <summary>홀딩·입력 레지스터 주소 0..count-1 값을 1씩 증가시킨다 (값 자동 변화 데모용).</summary>
    /// <param name="count">증가시킬 주소 개수.</param>
    public void IncrementRegisters(int count) => _store.IncrementRegisters(count);

    private void OnStoreDataChanged(object? sender, EventArgs e) =>
        DataChangedByMaster?.Invoke(this, EventArgs.Empty);

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            lock (_clientTasks)
            {
                _clientTasks.Add(Task.Run(() => HandleClientAsync(client, ct), CancellationToken.None));
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        Interlocked.Increment(ref _clientCount);
        try
        {
            using var _ = client;
            var stream = client.GetStream();
            var assembler = new MbapAssembler();
            var buffer = new byte[512];
            while (!ct.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }

                if (read == 0)
                {
                    break;
                }

                assembler.Feed(buffer.AsSpan(0, read));
                while (assembler.TryTakeFrame(out var frame))
                {
                    Interlocked.Increment(ref _requestCount);
                    var responsePdu = _store.ProcessPdu(frame.Pdu);
                    var adu = MbapFraming.BuildAdu(frame.TransactionId, frame.UnitId, responsePdu);
                    try
                    {
                        await stream.WriteAsync(adu, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (IOException)
                    {
                        return;
                    }
                }

                if (assembler.IsCorrupted)
                {
                    break; // 규격 위반 스트림 — 연결 종료
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _clientCount);
        }
    }
}
