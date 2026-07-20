using System.Net.Sockets;

namespace Nmw.Core.Transport;

/// <summary>TCP 트랜스포트 접속 설정.</summary>
/// <param name="Host">호스트명 또는 IP.</param>
/// <param name="Port">포트 (기본 502).</param>
public sealed record TcpTransportSettings(string Host, int Port = 502);

/// <summary>TCP 기반 트랜스포트 공통 구현 (Modbus TCP / RTU over TCP).</summary>
public abstract class TcpTransportBase : ITransport
{
    private readonly TcpTransportSettings _settings;
    private TcpClient? _client;
    private NetworkStream? _stream;

    private protected TcpTransportBase(TcpTransportSettings settings, ModbusFramingMode framingMode)
    {
        _settings = settings;
        FramingMode = framingMode;
    }

    /// <inheritdoc />
    public ModbusFramingMode FramingMode { get; }

    /// <inheritdoc />
    public bool IsConnected => _client?.Connected ?? false;

    /// <inheritdoc />
    public TimeSpan InterFrameDelayHint => TimeSpan.Zero;

    private NetworkStream Stream =>
        _stream ?? throw new InvalidOperationException("트랜스포트가 연결되지 않았습니다.");

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct)
    {
        await CloseAsync(ct).ConfigureAwait(false);

        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(_settings.Host, _settings.Port, ct).ConfigureAwait(false);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;
        _stream = client.GetStream();
    }

    /// <inheritdoc />
    public Task CloseAsync(CancellationToken ct)
    {
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct) =>
        Stream.WriteAsync(data, ct).AsTask();

    /// <inheritdoc />
    public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct) =>
        Stream.ReadAsync(buffer, ct);

    /// <inheritdoc />
    public void DiscardReceiveBuffer()
    {
        if (_client is null || _stream is null)
        {
            return;
        }

        Span<byte> sink = stackalloc byte[256];
        while (_client.Available > 0)
        {
            _ = _stream.Read(sink);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CloseAsync(CancellationToken.None).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}

/// <summary>Modbus TCP 트랜스포트 (MBAP 프레이밍).</summary>
public sealed class TcpTransport : TcpTransportBase
{
    /// <summary>접속 설정으로 트랜스포트를 만든다.</summary>
    /// <param name="settings">TCP 접속 설정.</param>
    public TcpTransport(TcpTransportSettings settings)
        : base(settings, ModbusFramingMode.Mbap)
    {
    }
}

/// <summary>RTU over TCP 게이트웨이 트랜스포트 (RTU 프레임을 TCP로 전송).</summary>
public sealed class RtuOverTcpTransport : TcpTransportBase
{
    /// <summary>접속 설정으로 트랜스포트를 만든다.</summary>
    /// <param name="settings">TCP 접속 설정.</param>
    public RtuOverTcpTransport(TcpTransportSettings settings)
        : base(settings, ModbusFramingMode.Rtu)
    {
    }
}
