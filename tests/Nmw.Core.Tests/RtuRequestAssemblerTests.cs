using Nmw.Core.Framing;

namespace Nmw.Core.Tests;

/// <summary>슬레이브 측 RTU 요청 조립기 테스트 (§7.1 골든 CRC 프레임 사용).</summary>
public sealed class RtuRequestAssemblerTests
{
    private static RtuRequestStatus FeedAll(RtuRequestAssembler assembler, byte[] frame)
    {
        var status = RtuRequestStatus.NeedMoreData;
        foreach (var b in frame)
        {
            status = assembler.FeedByte(b);
        }

        return status;
    }

    [Fact]
    public void Fc03Request_FixedLength8_Complete()
    {
        // §7.1 골든: "11 03 00 6B 00 03" + CRC 76 87
        var assembler = new RtuRequestAssembler();
        var status = FeedAll(assembler, TestHex.Bytes("11 03 00 6B 00 03 76 87"));

        Assert.Equal(RtuRequestStatus.Complete, status);
        Assert.Equal(0x11, assembler.UnitId);
        Assert.Equal(TestHex.Bytes("03 00 6B 00 03"), assembler.GetPdu());
    }

    [Fact]
    public void Fc16Request_VariableLength_Complete()
    {
        // §7.1 골든: "11 10 00 01 00 02 04 00 0A 01 02" + CRC C6 F0
        var assembler = new RtuRequestAssembler();
        var frame = TestHex.Bytes("11 10 00 01 00 02 04 00 0A 01 02 C6 F0");
        for (var i = 0; i < frame.Length - 1; i++)
        {
            Assert.Equal(RtuRequestStatus.NeedMoreData, assembler.FeedByte(frame[i]));
        }

        Assert.Equal(RtuRequestStatus.Complete, assembler.FeedByte(frame[^1]));
        Assert.Equal(TestHex.Bytes("10 00 01 00 02 04 00 0A 01 02"), assembler.GetPdu());
    }

    [Fact]
    public void Fc05Request_Complete()
    {
        // §7.1 골든: "01 05 00 AC FF 00" + CRC 4C 1B
        var assembler = new RtuRequestAssembler();
        var status = FeedAll(assembler, TestHex.Bytes("01 05 00 AC FF 00 4C 1B"));

        Assert.Equal(RtuRequestStatus.Complete, status);
        Assert.Equal(0x01, assembler.UnitId);
    }

    [Fact]
    public void CorruptedCrc_ReportsCrcMismatch()
    {
        var assembler = new RtuRequestAssembler();
        var status = FeedAll(assembler, TestHex.Bytes("11 03 00 6B 00 03 76 88"));

        Assert.Equal(RtuRequestStatus.CrcMismatch, status);
    }

    [Fact]
    public void UnsupportedFunction_ReportsInvalid()
    {
        var assembler = new RtuRequestAssembler();

        Assert.Equal(RtuRequestStatus.NeedMoreData, assembler.FeedByte(0x01));
        Assert.Equal(RtuRequestStatus.Invalid, assembler.FeedByte(0x2B));
    }

    [Fact]
    public void ZeroByteCount_ReportsInvalid()
    {
        var assembler = new RtuRequestAssembler();
        var status = FeedAll(assembler, TestHex.Bytes("11 10 00 01 00 02 00"));

        Assert.Equal(RtuRequestStatus.Invalid, status);
    }

    [Fact]
    public void Reset_AllowsReuseAfterTerminalState()
    {
        var assembler = new RtuRequestAssembler();
        FeedAll(assembler, TestHex.Bytes("11 03 00 6B 00 03 76 88")); // CRC 오류
        Assert.Equal(RtuRequestStatus.CrcMismatch, assembler.Status);

        assembler.Reset();
        var status = FeedAll(assembler, TestHex.Bytes("11 03 00 6B 00 03 76 87"));
        Assert.Equal(RtuRequestStatus.Complete, status);
    }

    [Fact]
    public void FeedAfterTerminalState_Throws()
    {
        var assembler = new RtuRequestAssembler();
        FeedAll(assembler, TestHex.Bytes("11 03 00 6B 00 03 76 87"));

        Assert.Throws<InvalidOperationException>(() => assembler.FeedByte(0x00));
    }
}
