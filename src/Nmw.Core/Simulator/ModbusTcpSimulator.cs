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
/// </summary>
public sealed class ModbusTcpSimulator : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly ushort[] _holding;
    private readonly ushort[] _input;
    private readonly bool[] _coils;
    private readonly bool[] _discrete;
    private readonly List<Task> _clientTasks = [];

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private long _requestCount;
    private int _clientCount;

    /// <summary>옵션으로 시뮬레이터를 만든다 (Start 전에는 리슨하지 않음).</summary>
    /// <param name="options">옵션. null이면 기본값.</param>
    public ModbusTcpSimulator(SimulatorOptions? options = null)
    {
        Options = options ?? new SimulatorOptions();
        if (Options.AreaSize is < 1 or > 65536)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), Options.AreaSize, "AreaSize는 1..65536 범위여야 합니다.");
        }

        _holding = new ushort[Options.AreaSize];
        _input = new ushort[Options.AreaSize];
        _coils = new bool[Options.AreaSize];
        _discrete = new bool[Options.AreaSize];
    }

    /// <summary>적용된 옵션.</summary>
    public SimulatorOptions Options { get; }

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
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    /// <summary>홀딩 레지스터 값을 읽는다.</summary>
    /// <param name="address">주소 (0-base).</param>
    public ushort GetHoldingRegister(int address)
    {
        lock (_gate)
        {
            return _holding[CheckAddress(address)];
        }
    }

    /// <summary>홀딩 레지스터 값을 쓴다.</summary>
    /// <param name="address">주소 (0-base).</param>
    /// <param name="value">값.</param>
    public void SetHoldingRegister(int address, ushort value)
    {
        lock (_gate)
        {
            _holding[CheckAddress(address)] = value;
        }
    }

    /// <summary>입력 레지스터 값을 읽는다.</summary>
    /// <param name="address">주소 (0-base).</param>
    public ushort GetInputRegister(int address)
    {
        lock (_gate)
        {
            return _input[CheckAddress(address)];
        }
    }

    /// <summary>입력 레지스터 값을 쓴다 (시뮬레이터 UI/코드 전용 — 마스터는 읽기만 가능).</summary>
    /// <param name="address">주소 (0-base).</param>
    /// <param name="value">값.</param>
    public void SetInputRegister(int address, ushort value)
    {
        lock (_gate)
        {
            _input[CheckAddress(address)] = value;
        }
    }

    /// <summary>코일 값을 읽는다.</summary>
    /// <param name="address">주소 (0-base).</param>
    public bool GetCoil(int address)
    {
        lock (_gate)
        {
            return _coils[CheckAddress(address)];
        }
    }

    /// <summary>코일 값을 쓴다.</summary>
    /// <param name="address">주소 (0-base).</param>
    /// <param name="value">값.</param>
    public void SetCoil(int address, bool value)
    {
        lock (_gate)
        {
            _coils[CheckAddress(address)] = value;
        }
    }

    /// <summary>접점 값을 읽는다.</summary>
    /// <param name="address">주소 (0-base).</param>
    public bool GetDiscreteInput(int address)
    {
        lock (_gate)
        {
            return _discrete[CheckAddress(address)];
        }
    }

    /// <summary>접점 값을 쓴다 (시뮬레이터 UI/코드 전용 — 마스터는 읽기만 가능).</summary>
    /// <param name="address">주소 (0-base).</param>
    /// <param name="value">값.</param>
    public void SetDiscreteInput(int address, bool value)
    {
        lock (_gate)
        {
            _discrete[CheckAddress(address)] = value;
        }
    }

    /// <summary>
    /// 홀딩·입력 레지스터 주소 0..count-1 값을 1씩 증가시킨다 (값 자동 변화 데모용, 래핑).
    /// </summary>
    /// <param name="count">증가시킬 주소 개수.</param>
    public void IncrementRegisters(int count)
    {
        lock (_gate)
        {
            var n = Math.Clamp(count, 0, Options.AreaSize);
            for (var i = 0; i < n; i++)
            {
                _holding[i] = unchecked((ushort)(_holding[i] + 1));
                _input[i] = unchecked((ushort)(_input[i] + 1));
            }
        }
    }

    private int CheckAddress(int address) =>
        address >= 0 && address < Options.AreaSize
            ? address
            : throw new ArgumentOutOfRangeException(
                nameof(address), address, $"주소는 0..{Options.AreaSize - 1} 범위여야 합니다.");

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
                    var responsePdu = ProcessPdu(frame.Pdu, out var dataChanged);
                    if (dataChanged)
                    {
                        DataChangedByMaster?.Invoke(this, EventArgs.Empty);
                    }

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

    private static byte[] ExceptionPdu(byte functionCode, byte exceptionCode) =>
        [(byte)(functionCode | 0x80), exceptionCode];

    private static ushort ReadU16(byte[] pdu, int offset) =>
        (ushort)((pdu[offset] << 8) | pdu[offset + 1]);

    private byte[] ProcessPdu(byte[] pdu, out bool dataChanged)
    {
        dataChanged = false;
        if (pdu.Length < 1)
        {
            return ExceptionPdu(0, 0x01);
        }

        lock (_gate)
        {
            var fc = pdu[0];
            switch (fc)
            {
                case 0x01:
                    return ReadBits(pdu, _coils);
                case 0x02:
                    return ReadBits(pdu, _discrete);
                case 0x03:
                    return ReadRegisters(pdu, _holding);
                case 0x04:
                    return ReadRegisters(pdu, _input);
                case 0x05:
                    return WriteSingleCoil(pdu, out dataChanged);
                case 0x06:
                    return WriteSingleRegister(pdu, out dataChanged);
                case 0x0F:
                    return WriteMultipleCoils(pdu, out dataChanged);
                case 0x10:
                    return WriteMultipleRegisters(pdu, out dataChanged);
                default:
                    return ExceptionPdu(fc, 0x01);
            }
        }
    }

    private static byte[] ReadBits(byte[] pdu, bool[] map)
    {
        var fc = pdu[0];
        if (pdu.Length != 5)
        {
            return ExceptionPdu(fc, 0x03);
        }

        var address = ReadU16(pdu, 1);
        var quantity = ReadU16(pdu, 3);
        if (quantity is < 1 or > 2000)
        {
            return ExceptionPdu(fc, 0x03);
        }

        if (address + quantity > map.Length)
        {
            return ExceptionPdu(fc, 0x02);
        }

        var byteCount = (byte)((quantity + 7) / 8);
        var response = new byte[2 + byteCount];
        response[0] = fc;
        response[1] = byteCount;
        for (var i = 0; i < quantity; i++)
        {
            if (map[address + i])
            {
                response[2 + (i / 8)] |= (byte)(1 << (i % 8));
            }
        }

        return response;
    }

    private static byte[] ReadRegisters(byte[] pdu, ushort[] map)
    {
        var fc = pdu[0];
        if (pdu.Length != 5)
        {
            return ExceptionPdu(fc, 0x03);
        }

        var address = ReadU16(pdu, 1);
        var quantity = ReadU16(pdu, 3);
        if (quantity is < 1 or > 125)
        {
            return ExceptionPdu(fc, 0x03);
        }

        if (address + quantity > map.Length)
        {
            return ExceptionPdu(fc, 0x02);
        }

        var response = new byte[2 + (quantity * 2)];
        response[0] = fc;
        response[1] = (byte)(quantity * 2);
        for (var i = 0; i < quantity; i++)
        {
            response[2 + (i * 2)] = (byte)(map[address + i] >> 8);
            response[3 + (i * 2)] = (byte)map[address + i];
        }

        return response;
    }

    private byte[] WriteSingleCoil(byte[] pdu, out bool dataChanged)
    {
        dataChanged = false;
        if (pdu.Length != 5)
        {
            return ExceptionPdu(0x05, 0x03);
        }

        var address = ReadU16(pdu, 1);
        var value = ReadU16(pdu, 3);
        if (value is not (0xFF00 or 0x0000))
        {
            return ExceptionPdu(0x05, 0x03);
        }

        if (address >= _coils.Length)
        {
            return ExceptionPdu(0x05, 0x02);
        }

        _coils[address] = value == 0xFF00;
        dataChanged = true;
        return pdu; // 요청 에코
    }

    private byte[] WriteSingleRegister(byte[] pdu, out bool dataChanged)
    {
        dataChanged = false;
        if (pdu.Length != 5)
        {
            return ExceptionPdu(0x06, 0x03);
        }

        var address = ReadU16(pdu, 1);
        if (address >= _holding.Length)
        {
            return ExceptionPdu(0x06, 0x02);
        }

        _holding[address] = ReadU16(pdu, 3);
        dataChanged = true;
        return pdu; // 요청 에코
    }

    private byte[] WriteMultipleCoils(byte[] pdu, out bool dataChanged)
    {
        dataChanged = false;
        if (pdu.Length < 6)
        {
            return ExceptionPdu(0x0F, 0x03);
        }

        var address = ReadU16(pdu, 1);
        var quantity = ReadU16(pdu, 3);
        var byteCount = pdu[5];
        if (quantity is < 1 or > 1968 || byteCount != (quantity + 7) / 8 ||
            pdu.Length != 6 + byteCount)
        {
            return ExceptionPdu(0x0F, 0x03);
        }

        if (address + quantity > _coils.Length)
        {
            return ExceptionPdu(0x0F, 0x02);
        }

        for (var i = 0; i < quantity; i++)
        {
            _coils[address + i] = (pdu[6 + (i / 8)] & (1 << (i % 8))) != 0;
        }

        dataChanged = true;
        return [0x0F, pdu[1], pdu[2], pdu[3], pdu[4]];
    }

    private byte[] WriteMultipleRegisters(byte[] pdu, out bool dataChanged)
    {
        dataChanged = false;
        if (pdu.Length < 6)
        {
            return ExceptionPdu(0x10, 0x03);
        }

        var address = ReadU16(pdu, 1);
        var quantity = ReadU16(pdu, 3);
        var byteCount = pdu[5];
        if (quantity is < 1 or > 123 || byteCount != quantity * 2 ||
            pdu.Length != 6 + byteCount)
        {
            return ExceptionPdu(0x10, 0x03);
        }

        if (address + quantity > _holding.Length)
        {
            return ExceptionPdu(0x10, 0x02);
        }

        for (var i = 0; i < quantity; i++)
        {
            _holding[address + i] = ReadU16(pdu, 6 + (i * 2));
        }

        dataChanged = true;
        return [0x10, pdu[1], pdu[2], pdu[3], pdu[4]];
    }
}
