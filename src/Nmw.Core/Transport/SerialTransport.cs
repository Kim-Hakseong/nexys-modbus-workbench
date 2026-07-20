using System.IO.Ports;

namespace Nmw.Core.Transport;

/// <summary>시리얼(RTU) 트랜스포트 접속 설정.</summary>
/// <param name="PortName">COM 포트 이름 (예: COM3, /dev/tty.usbserial).</param>
/// <param name="BaudRate">보레이트 (기본 9600).</param>
/// <param name="Parity">패리티 (기본 None).</param>
/// <param name="DataBits">데이터 비트 (기본 8).</param>
/// <param name="StopBits">정지 비트 (기본 One).</param>
public sealed record SerialTransportSettings(
    string PortName,
    int BaudRate = 9600,
    Parity Parity = Parity.None,
    int DataBits = 8,
    StopBits StopBits = StopBits.One);

/// <summary>USB-RS485 등 시리얼 포트 기반 Modbus RTU 트랜스포트.</summary>
public sealed class SerialTransport : ITransport
{
    private readonly SerialTransportSettings _settings;
    private SerialPort? _port;

    /// <summary>접속 설정으로 트랜스포트를 만든다.</summary>
    /// <param name="settings">시리얼 접속 설정.</param>
    public SerialTransport(SerialTransportSettings settings)
    {
        _settings = settings;
    }

    /// <inheritdoc />
    public ModbusFramingMode FramingMode => ModbusFramingMode.Rtu;

    /// <inheritdoc />
    public bool IsConnected => _port?.IsOpen ?? false;

    /// <inheritdoc />
    public TimeSpan InterFrameDelayHint => ComputeInterFrameDelay(_settings);

    /// <summary>
    /// 3.5 char time 기반 권장 프레임간 지연을 계산한다.
    /// Modbus 스펙에 따라 19200 baud 초과에서는 고정 1.75ms.
    /// </summary>
    /// <param name="settings">시리얼 설정.</param>
    /// <returns>권장 프레임간 지연.</returns>
    public static TimeSpan ComputeInterFrameDelay(SerialTransportSettings settings)
    {
        if (settings.BaudRate > 19200)
        {
            return TimeSpan.FromMilliseconds(1.75);
        }

        var stopBits = settings.StopBits switch
        {
            StopBits.Two or StopBits.OnePointFive => 2,
            _ => 1,
        };
        var bitsPerChar = 1 + settings.DataBits + (settings.Parity == Parity.None ? 0 : 1) + stopBits;
        return TimeSpan.FromMilliseconds(3.5 * 1000.0 * bitsPerChar / settings.BaudRate);
    }

    private SerialPort Port =>
        _port ?? throw new InvalidOperationException("시리얼 포트가 열려 있지 않습니다.");

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct)
    {
        await CloseAsync(ct).ConfigureAwait(false);

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
    }

    /// <inheritdoc />
    public Task CloseAsync(CancellationToken ct)
    {
        _port?.Dispose();
        _port = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct) =>
        Port.BaseStream.WriteAsync(data, ct).AsTask();

    /// <inheritdoc />
    public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct) =>
        Port.BaseStream.ReadAsync(buffer, ct);

    /// <inheritdoc />
    public void DiscardReceiveBuffer() => _port?.DiscardInBuffer();

    /// <inheritdoc />
    public async ValueTask DisposeAsync() =>
        await CloseAsync(CancellationToken.None).ConfigureAwait(false);
}
