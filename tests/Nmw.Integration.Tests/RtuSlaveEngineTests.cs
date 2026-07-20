using System.Threading.Channels;
using Nmw.Core.Framing;
using Nmw.Core.Simulator;

namespace Nmw.Integration.Tests;

/// <summary>
/// 테스트용 인메모리 duplex 스트림 쌍. 한쪽의 Write가 반대쪽의 Read로 전달된다
/// (가상 COM 포트 쌍을 흉내낸다).
/// </summary>
internal sealed class DuplexPipe
{
    private readonly Channel<byte[]> _aToB = Channel.CreateUnbounded<byte[]>();
    private readonly Channel<byte[]> _bToA = Channel.CreateUnbounded<byte[]>();

    public Stream EndA => new ChannelStream(_bToA.Reader, _aToB.Writer);

    public Stream EndB => new ChannelStream(_aToB.Reader, _bToA.Writer);

    private sealed class ChannelStream : Stream
    {
        private readonly ChannelReader<byte[]> _reader;
        private readonly ChannelWriter<byte[]> _writer;
        private byte[]? _pending;
        private int _offset;

        public ChannelStream(ChannelReader<byte[]> reader, ChannelWriter<byte[]> writer)
        {
            _reader = reader;
            _writer = writer;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_pending is null)
            {
                try
                {
                    _pending = await _reader.ReadAsync(cancellationToken);
                }
                catch (ChannelClosedException)
                {
                    return 0;
                }

                _offset = 0;
            }

            var count = Math.Min(buffer.Length, _pending.Length - _offset);
            _pending.AsSpan(_offset, count).CopyTo(buffer.Span);
            _offset += count;
            if (_offset >= _pending.Length)
            {
                _pending = null;
            }

            return count;
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _writer.TryWrite(buffer.ToArray());
            return ValueTask.CompletedTask;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Write(byte[] buffer, int offset, int count) =>
            _writer.TryWrite(buffer.AsSpan(offset, count).ToArray());

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _writer.TryComplete();
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>RTU 슬레이브 엔진 테스트 — 인메모리 duplex 스트림으로 마스터 프레임 왕복.</summary>
public sealed class RtuSlaveEngineTests : IAsyncLifetime
{
    private readonly SimulatorDataStore _store = new(200);
    private readonly DuplexPipe _pipe = new();
    private readonly CancellationTokenSource _cts = new();
    private RtuSlaveEngine _engine = null!;
    private Task _engineTask = Task.CompletedTask;
    private Stream _masterSide = null!;

    public Task InitializeAsync()
    {
        _engine = new RtuSlaveEngine(_store);
        _engineTask = _engine.RunAsync(_pipe.EndB, _cts.Token);
        _masterSide = _pipe.EndA;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _cts.CancelAsync();
        _masterSide.Dispose();
        await _engineTask;
        _cts.Dispose();
    }

    private static byte[] WithCrc(string hexWithoutCrc)
    {
        var body = TestHexBytes(hexWithoutCrc);
        var (lo, hi) = Crc16.ComputeBytes(body);
        return [.. body, lo, hi];
    }

    private static byte[] TestHexBytes(string spacedHex) =>
        Convert.FromHexString(spacedHex.Replace(" ", "", StringComparison.Ordinal));

    private async Task<byte[]> ReadResponseAsync(int expectedLength)
    {
        var response = new byte[expectedLength];
        var offset = 0;
        using var timeout = new CancellationTokenSource(3000);
        while (offset < expectedLength)
        {
            var read = await _masterSide.ReadAsync(response.AsMemory(offset), timeout.Token);
            Assert.True(read > 0, "스트림이 조기 종료되었습니다.");
            offset += read;
        }

        return response;
    }

    [Fact]
    public async Task Fc03Request_GoldenFrame_ReturnsHoldingValues()
    {
        _store.SetHoldingRegister(0x6B, 0x022B);
        _store.SetHoldingRegister(0x6C, 0x0000);
        _store.SetHoldingRegister(0x6D, 0x0064);

        // §7.1 골든 요청 프레임
        await _masterSide.WriteAsync(TestHexBytes("11 03 00 6B 00 03 76 87"));

        // 응답: unit + fc + byteCount + 6바이트 + crc2 = 11바이트, §7.3 골든 PDU
        var response = await ReadResponseAsync(11);
        Assert.True(Crc16.Validate(response));
        Assert.Equal(TestHexBytes("11 03 06 02 2B 00 00 00 64"), response[..9]);
        Assert.Equal(1, _engine.RequestCount);
    }

    [Fact]
    public async Task Fc06Write_EchoAndStoreUpdated()
    {
        await _masterSide.WriteAsync(WithCrc("01 06 00 05 BE EF"));

        var response = await ReadResponseAsync(8);
        Assert.Equal(WithCrc("01 06 00 05 BE EF"), response);
        Assert.Equal(0xBEEF, _store.GetHoldingRegister(5));
    }

    [Fact]
    public async Task Fc16Write_VariableLengthRequest_Processed()
    {
        // §7.1 골든 FC16 요청 프레임
        await _masterSide.WriteAsync(TestHexBytes("11 10 00 01 00 02 04 00 0A 01 02 C6 F0"));

        var response = await ReadResponseAsync(8);
        Assert.True(Crc16.Validate(response));
        Assert.Equal(TestHexBytes("11 10 00 01 00 02"), response[..6]);
        Assert.Equal(0x000A, _store.GetHoldingRegister(1));
        Assert.Equal(0x0102, _store.GetHoldingRegister(2));
    }

    [Fact]
    public async Task CorruptedCrcRequest_SilentlyDropped_NextRequestStillWorks()
    {
        var corrupted = TestHexBytes("11 03 00 6B 00 03 76 88"); // CRC 오류
        await _masterSide.WriteAsync(corrupted);
        await Task.Delay(150);
        Assert.Equal(0, _engine.RequestCount); // 무응답으로 버림

        _store.SetHoldingRegister(0, 42);
        await _masterSide.WriteAsync(WithCrc("01 03 00 00 00 01"));
        var response = await ReadResponseAsync(7);
        Assert.True(Crc16.Validate(response));
        Assert.Equal(TestHexBytes("01 03 02 00 2A"), response[..5]);
    }

    [Fact]
    public async Task TwoRequestsInOneWrite_BothProcessed()
    {
        _store.SetHoldingRegister(0, 1);
        var first = WithCrc("01 03 00 00 00 01");
        var second = WithCrc("01 06 00 00 00 07");
        await _masterSide.WriteAsync(first.Concat(second).ToArray());

        var firstResponse = await ReadResponseAsync(7);
        Assert.Equal(0x03, firstResponse[1]);
        var secondResponse = await ReadResponseAsync(8);
        Assert.Equal(second, secondResponse);
        Assert.Equal(7, _store.GetHoldingRegister(0));
        Assert.Equal(2, _engine.RequestCount);
    }

    [Fact]
    public async Task OutOfRangeRead_ReturnsExceptionFrame()
    {
        // 저장소 크기 200 → 주소 200부터 10개 읽기 (범위 밖)
        await _masterSide.WriteAsync(WithCrc("01 03 00 C8 00 0A"));

        var response = await ReadResponseAsync(5); // unit + fc|0x80 + code + crc2
        Assert.True(Crc16.Validate(response));
        Assert.Equal(0x83, response[1]);
        Assert.Equal(0x02, response[2]); // Illegal Data Address
    }
}
