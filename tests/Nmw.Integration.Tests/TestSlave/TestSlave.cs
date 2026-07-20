using System.Net;
using System.Net.Sockets;
using Nmw.Core.Framing;

namespace Nmw.Integration.Tests.TestSlave;

/// <summary>
/// 테스트 전용 인메모리 Modbus TCP 슬레이브. FC01~06, 15, 16을 지원하고
/// 시나리오 주입(응답 지연, 잘못된 TransactionId 선행 응답)을 제공한다.
/// 레지스터/코일 값은 테스트 코드가 명시적으로 세팅한다.
/// </summary>
public sealed class TestSlave : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _clientTasks = [];
    private Task? _acceptLoop;
    private int _requestCount;

    /// <summary>홀딩 레지스터 (주소 0..99).</summary>
    public ushort[] HoldingRegisters { get; } = new ushort[100];

    /// <summary>입력 레지스터 (주소 0..99).</summary>
    public ushort[] InputRegisters { get; } = new ushort[100];

    /// <summary>코일 (주소 0..99).</summary>
    public bool[] Coils { get; } = new bool[100];

    /// <summary>접점 (주소 0..99).</summary>
    public bool[] DiscreteInputs { get; } = new bool[100];

    /// <summary>모든 응답 전에 적용할 지연 (타임아웃 시나리오용).</summary>
    public TimeSpan ResponseDelay { get; set; }

    /// <summary>true면 다음 응답 직전에 TxId+1로 오염된 프레임을 먼저 1회 보낸다.</summary>
    public bool InjectWrongTransactionIdOnce { get; set; }

    /// <summary>수신·처리한 요청 프레임 수 (재시도 검증용).</summary>
    public int RequestCount => Volatile.Read(ref _requestCount);

    /// <summary>리슨 포트. Start 후 유효.</summary>
    public int Port { get; private set; }

    /// <summary>슬레이브를 시작한다 (루프백, 임시 포트).</summary>
    public void Start()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>슬레이브를 정지하고 모든 연결을 정리한다.</summary>
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        if (_acceptLoop is not null)
        {
            await _acceptLoop;
        }

        await Task.WhenAll(_clientTasks);
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }

            _clientTasks.Add(Task.Run(() => HandleClientAsync(client, _cts.Token)));
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        var stream = client.GetStream();
        var assembler = new MbapAssembler();
        var buffer = new byte[512];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, ct);
                if (read == 0)
                {
                    break;
                }

                assembler.Feed(buffer.AsSpan(0, read));
                while (assembler.TryTakeFrame(out var frame))
                {
                    Interlocked.Increment(ref _requestCount);
                    var responsePdu = ProcessPdu(frame.Pdu);

                    if (ResponseDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(ResponseDelay, ct);
                    }

                    if (InjectWrongTransactionIdOnce)
                    {
                        InjectWrongTransactionIdOnce = false;
                        var bogus = MbapFraming.BuildAdu(
                            (ushort)(frame.TransactionId + 1), frame.UnitId, responsePdu);
                        await stream.WriteAsync(bogus, ct);
                    }

                    var adu = MbapFraming.BuildAdu(frame.TransactionId, frame.UnitId, responsePdu);
                    await stream.WriteAsync(adu, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 슬레이브 종료
        }
        catch (IOException)
        {
            // 클라이언트가 연결을 끊음 — 테스트 슬레이브에서는 정상 종료로 취급
        }
    }

    private static byte[] ExceptionPdu(byte functionCode, byte exceptionCode) =>
        [(byte)(functionCode | 0x80), exceptionCode];

    private byte[] ProcessPdu(byte[] pdu)
    {
        if (pdu.Length < 1)
        {
            return ExceptionPdu(0, 0x01);
        }

        var fc = pdu[0];
        return fc switch
        {
            0x01 => ReadBits(pdu, Coils),
            0x02 => ReadBits(pdu, DiscreteInputs),
            0x03 => ReadRegisters(pdu, HoldingRegisters),
            0x04 => ReadRegisters(pdu, InputRegisters),
            0x05 => WriteSingleCoil(pdu),
            0x06 => WriteSingleRegister(pdu),
            0x0F => WriteMultipleCoils(pdu),
            0x10 => WriteMultipleRegisters(pdu),
            _ => ExceptionPdu(fc, 0x01),
        };
    }

    private static ushort ReadU16(byte[] pdu, int offset) =>
        (ushort)((pdu[offset] << 8) | pdu[offset + 1]);

    private static byte[] ReadBits(byte[] pdu, bool[] map)
    {
        var fc = pdu[0];
        if (pdu.Length != 5)
        {
            return ExceptionPdu(fc, 0x03);
        }

        var address = ReadU16(pdu, 1);
        var quantity = ReadU16(pdu, 3);
        if (quantity < 1 || quantity > 2000)
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
        if (quantity < 1 || quantity > 125)
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

    private byte[] WriteSingleCoil(byte[] pdu)
    {
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

        if (address >= Coils.Length)
        {
            return ExceptionPdu(0x05, 0x02);
        }

        Coils[address] = value == 0xFF00;
        return pdu; // 요청 에코
    }

    private byte[] WriteSingleRegister(byte[] pdu)
    {
        if (pdu.Length != 5)
        {
            return ExceptionPdu(0x06, 0x03);
        }

        var address = ReadU16(pdu, 1);
        if (address >= HoldingRegisters.Length)
        {
            return ExceptionPdu(0x06, 0x02);
        }

        HoldingRegisters[address] = ReadU16(pdu, 3);
        return pdu; // 요청 에코
    }

    private byte[] WriteMultipleCoils(byte[] pdu)
    {
        if (pdu.Length < 6)
        {
            return ExceptionPdu(0x0F, 0x03);
        }

        var address = ReadU16(pdu, 1);
        var quantity = ReadU16(pdu, 3);
        var byteCount = pdu[5];
        if (quantity < 1 || quantity > 1968 || byteCount != (quantity + 7) / 8 ||
            pdu.Length != 6 + byteCount)
        {
            return ExceptionPdu(0x0F, 0x03);
        }

        if (address + quantity > Coils.Length)
        {
            return ExceptionPdu(0x0F, 0x02);
        }

        for (var i = 0; i < quantity; i++)
        {
            Coils[address + i] = (pdu[6 + (i / 8)] & (1 << (i % 8))) != 0;
        }

        return [0x0F, pdu[1], pdu[2], pdu[3], pdu[4]];
    }

    private byte[] WriteMultipleRegisters(byte[] pdu)
    {
        if (pdu.Length < 6)
        {
            return ExceptionPdu(0x10, 0x03);
        }

        var address = ReadU16(pdu, 1);
        var quantity = ReadU16(pdu, 3);
        var byteCount = pdu[5];
        if (quantity < 1 || quantity > 123 || byteCount != quantity * 2 ||
            pdu.Length != 6 + byteCount)
        {
            return ExceptionPdu(0x10, 0x03);
        }

        if (address + quantity > HoldingRegisters.Length)
        {
            return ExceptionPdu(0x10, 0x02);
        }

        for (var i = 0; i < quantity; i++)
        {
            HoldingRegisters[address + i] = ReadU16(pdu, 6 + (i * 2));
        }

        return [0x10, pdu[1], pdu[2], pdu[3], pdu[4]];
    }
}
