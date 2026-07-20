using System.IO.Ports;
using Nmw.Core.Framing;
using Nmw.Core.Transport;

namespace Nmw.Core.Simulator;

/// <summary>
/// RTU 슬레이브 처리 엔진. 임의의 duplex 스트림 위에서 RTU 요청을 조립·처리·응답한다.
/// CRC 불일치 요청에는 실제 슬레이브처럼 응답하지 않고 버린다(재동기화).
/// 시리얼 포트 없이 스트림만으로 테스트할 수 있도록 분리되어 있다.
/// </summary>
public sealed class RtuSlaveEngine
{
    private readonly SimulatorDataStore _store;
    private long _requestCount;

    /// <summary>데이터 저장소로 엔진을 만든다.</summary>
    /// <param name="store">공유 데이터 저장소.</param>
    public RtuSlaveEngine(SimulatorDataStore store)
    {
        _store = store;
    }

    /// <summary>처리한 정상 요청 프레임 수.</summary>
    public long RequestCount => Interlocked.Read(ref _requestCount);

    /// <summary>
    /// 스트림에서 RTU 요청을 읽어 처리하는 루프. 스트림 종료/취소/IO 오류 시 반환한다.
    /// </summary>
    /// <param name="duplex">읽기/쓰기 가능한 duplex 스트림 (시리얼 BaseStream 등).</param>
    /// <param name="ct">취소 토큰.</param>
    public async Task RunAsync(Stream duplex, CancellationToken ct)
    {
        var buffer = new byte[512];
        var assembler = new RtuRequestAssembler();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = await duplex.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                for (var i = 0; i < read; i++)
                {
                    var status = assembler.FeedByte(buffer[i]);
                    switch (status)
                    {
                        case RtuRequestStatus.Complete:
                        {
                            Interlocked.Increment(ref _requestCount);
                            var responsePdu = _store.ProcessPdu(assembler.GetPdu());
                            var frame = RtuFraming.BuildFrame(assembler.UnitId, responsePdu);
                            await duplex.WriteAsync(frame, ct).ConfigureAwait(false);
                            assembler.Reset();
                            break;
                        }

                        case RtuRequestStatus.CrcMismatch:
                        case RtuRequestStatus.Invalid:
                            // 실제 슬레이브처럼 무응답으로 버리고 재동기화한다.
                            assembler.Reset();
                            break;

                        default:
                            break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 정지 요청
        }
        catch (IOException)
        {
            // 포트 닫힘
        }
        catch (ObjectDisposedException)
        {
            // 포트 dispose로 인한 종료
        }
    }
}

/// <summary>
/// 내장 Modbus RTU(시리얼) 슬레이브 시뮬레이터. 지정한 COM 포트를 열고 슬레이브로 응답한다.
/// 같은 PC에서 마스터와 함께 테스트하려면 가상 COM 포트 쌍(com0com 등)이나
/// 서로 연결된 USB-RS485 컨버터 2개가 필요하다. 모든 Unit ID에 응답(에코)한다.
/// 데이터는 <see cref="SimulatorDataStore"/>에 저장되며 TCP 시뮬레이터와 공유할 수 있다.
/// </summary>
public sealed class ModbusRtuSlaveSimulator : IAsyncDisposable
{
    private readonly SerialTransportSettings _settings;
    private readonly RtuSlaveEngine _engine;
    private SerialPort? _port;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>시리얼 설정과 (선택) 공유 데이터 저장소로 시뮬레이터를 만든다.</summary>
    /// <param name="settings">시리얼 포트 설정.</param>
    /// <param name="store">공유할 데이터 저장소. null이면 새로 만든다.</param>
    public ModbusRtuSlaveSimulator(SerialTransportSettings settings, SimulatorDataStore? store = null)
    {
        _settings = settings;
        Store = store ?? new SimulatorDataStore();
        _engine = new RtuSlaveEngine(Store);
    }

    /// <summary>데이터 저장소 (TCP 시뮬레이터와 공유 가능).</summary>
    public SimulatorDataStore Store { get; }

    /// <summary>포트 이름.</summary>
    public string PortName => _settings.PortName;

    /// <summary>보레이트.</summary>
    public int BaudRate => _settings.BaudRate;

    /// <summary>실행 중 여부.</summary>
    public bool IsRunning => _port?.IsOpen ?? false;

    /// <summary>처리한 정상 요청 프레임 수.</summary>
    public long RequestCount => _engine.RequestCount;

    /// <summary>시리얼 포트를 열고 슬레이브 루프를 시작한다.</summary>
    /// <exception cref="InvalidOperationException">이미 실행 중.</exception>
    /// <exception cref="IOException">포트 열기 실패.</exception>
    public void Start()
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("시뮬레이터가 이미 실행 중입니다.");
        }

        var port = new SerialPort(
            _settings.PortName, _settings.BaudRate, _settings.Parity, _settings.DataBits, _settings.StopBits)
        {
            ReadTimeout = SerialPort.InfiniteTimeout,
            WriteTimeout = SerialPort.InfiniteTimeout,
        };
        try
        {
            port.Open();
        }
        catch
        {
            port.Dispose();
            throw;
        }

        _port = port;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => _engine.RunAsync(port.BaseStream, _cts.Token));
    }

    /// <summary>슬레이브 루프를 정지하고 포트를 닫는다. 정지 후 다시 Start 가능.</summary>
    public async Task StopAsync()
    {
        if (_port is not { } port || _cts is not { } cts)
        {
            return;
        }

        _port = null;
        await cts.CancelAsync().ConfigureAwait(false);
        port.Dispose(); // BaseStream 읽기를 깨워 루프를 종료시킨다
        if (_loop is { } loop)
        {
            await loop.ConfigureAwait(false);
        }

        cts.Dispose();
        _cts = null;
        _loop = null;
    }

    /// <summary>정지 후 자원을 정리한다.</summary>
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
