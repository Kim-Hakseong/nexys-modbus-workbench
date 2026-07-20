using System.Threading.Channels;
using Nmw.Core.Transport;

namespace Nmw.Core.Tests;

/// <summary>
/// ModbusMaster 테스트용 인메모리 트랜스포트.
/// SendAsync 시 <see cref="OnRequest"/>가 반환한 청크들을 수신 큐에 넣는다
/// (청크 단위로 ReceiveAsync에 전달되어 부분 수신을 재현한다).
/// </summary>
internal sealed class FakeTransport : ITransport
{
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
    private byte[]? _pendingChunk;
    private int _pendingOffset;

    public List<byte[]> SentFrames { get; } = [];

    public Func<byte[], IEnumerable<byte[]>>? OnRequest { get; set; }

    public int DiscardCount { get; private set; }

    public ModbusFramingMode FramingMode { get; set; } = ModbusFramingMode.Mbap;

    public bool IsConnected { get; set; } = true;

    public TimeSpan InterFrameDelayHint => TimeSpan.Zero;

    public Task ConnectAsync(CancellationToken ct)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken ct)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var frame = data.ToArray();
        SentFrames.Add(frame);
        if (OnRequest is { } handler)
        {
            foreach (var chunk in handler(frame))
            {
                _incoming.Writer.TryWrite(chunk);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>각 수신 청크 전달 전 지연 (느린 슬레이브 재현용).</summary>
    public TimeSpan ReceiveDelay { get; set; }

    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct)
    {
        if (ReceiveDelay > TimeSpan.Zero)
        {
            await Task.Delay(ReceiveDelay, ct);
        }

        if (_pendingChunk is null)
        {
            _pendingChunk = await _incoming.Reader.ReadAsync(ct);
            _pendingOffset = 0;
        }

        var count = Math.Min(buffer.Length, _pendingChunk.Length - _pendingOffset);
        _pendingChunk.AsSpan(_pendingOffset, count).CopyTo(buffer.Span);
        _pendingOffset += count;
        if (_pendingOffset >= _pendingChunk.Length)
        {
            _pendingChunk = null;
        }

        return count;
    }

    public void DiscardReceiveBuffer()
    {
        DiscardCount++;
        while (_incoming.Reader.TryRead(out _))
        {
        }

        _pendingChunk = null;
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}
