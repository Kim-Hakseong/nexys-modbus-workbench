using Nmw.Core.Framing;

namespace Nmw.Core.Tests;

/// <summary>DESIGN.md §7.4 MBAP 골든 벡터 테스트. 벡터 수정/삭제 금지.</summary>
public sealed class MbapGoldenTests
{
    [Fact]
    public void BuildAdu_MatchesGoldenVector()
    {
        var adu = MbapFraming.BuildAdu(0x0001, 0x11, TestHex.Bytes("03 00 6B 00 03"));

        Assert.Equal(TestHex.Bytes("00 01 00 00 00 06 11 03 00 6B 00 03"), adu);
    }
}

/// <summary>MBAP 조립 상태머신 테스트.</summary>
public sealed class MbapAssemblerTests
{
    private static readonly byte[] GoldenAdu = TestHex.Bytes("00 01 00 00 00 06 11 03 00 6B 00 03");

    [Fact]
    public void SingleFeed_ProducesFrame()
    {
        var assembler = new MbapAssembler();
        assembler.Feed(GoldenAdu);

        Assert.True(assembler.TryTakeFrame(out var frame));
        Assert.Equal(0x0001, frame!.TransactionId);
        Assert.Equal(0x11, frame.UnitId);
        Assert.Equal(TestHex.Bytes("03 00 6B 00 03"), frame.Pdu);
        Assert.False(assembler.TryTakeFrame(out _));
    }

    [Fact]
    public void ByteByByteFeed_ProducesFrameOnlyWhenComplete()
    {
        var assembler = new MbapAssembler();
        for (var i = 0; i < GoldenAdu.Length - 1; i++)
        {
            assembler.Feed(new[] { GoldenAdu[i] });
            Assert.False(assembler.TryTakeFrame(out _));
        }

        assembler.Feed(new[] { GoldenAdu[^1] });
        Assert.True(assembler.TryTakeFrame(out var frame));
        Assert.Equal(TestHex.Bytes("03 00 6B 00 03"), frame!.Pdu);
    }

    [Fact]
    public void TwoFramesInOneFeed_ProducesBothInOrder()
    {
        var second = MbapFraming.BuildAdu(0x0002, 0x11, TestHex.Bytes("03 02 00 2A"));
        var assembler = new MbapAssembler();
        assembler.Feed(GoldenAdu.Concat(second).ToArray());

        Assert.True(assembler.TryTakeFrame(out var first));
        Assert.Equal(0x0001, first!.TransactionId);
        Assert.True(assembler.TryTakeFrame(out var next));
        Assert.Equal(0x0002, next!.TransactionId);
        Assert.Equal(TestHex.Bytes("03 02 00 2A"), next.Pdu);
        Assert.False(assembler.TryTakeFrame(out _));
    }

    [Fact]
    public void NonZeroProtocolId_MarksCorrupted()
    {
        var assembler = new MbapAssembler();
        assembler.Feed(TestHex.Bytes("00 01 00 01 00 06 11 03 00 6B 00 03"));

        Assert.False(assembler.TryTakeFrame(out _));
        Assert.True(assembler.IsCorrupted);
    }

    [Theory]
    [InlineData("00 01 00 00 00 01 11")]       // Length=1 (< 2)
    [InlineData("00 01 00 00 01 00 11 03 00")] // Length=256 (> 254)
    public void OutOfRangeLength_MarksCorrupted(string aduHex)
    {
        var assembler = new MbapAssembler();
        assembler.Feed(TestHex.Bytes(aduHex));

        Assert.False(assembler.TryTakeFrame(out _));
        Assert.True(assembler.IsCorrupted);
        Assert.NotNull(assembler.CorruptReason);
    }

    [Fact]
    public void Reset_ClearsCorruptionAndBuffer()
    {
        var assembler = new MbapAssembler();
        assembler.Feed(TestHex.Bytes("00 01 00 09 00 06 11 03 00 6B 00 03"));
        Assert.False(assembler.TryTakeFrame(out _));
        Assert.True(assembler.IsCorrupted);

        assembler.Reset();
        Assert.False(assembler.IsCorrupted);
        assembler.Feed(GoldenAdu);
        Assert.True(assembler.TryTakeFrame(out var frame));
        Assert.Equal(0x0001, frame!.TransactionId);
    }
}
