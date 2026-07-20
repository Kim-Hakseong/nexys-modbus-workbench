using Nmw.Core.Framing;
using Nmw.Core.Protocol;

namespace Nmw.Core.Tests;

/// <summary>RTU 프레임 빌드 테스트 (CRC는 §7.1 골든 벡터 기반).</summary>
public sealed class RtuFramingTests
{
    [Fact]
    public void BuildFrame_AppendsGoldenCrcLowByteFirst()
    {
        // §7.1: "11 03 00 6B 00 03" → CRC 76 87
        var frame = RtuFraming.BuildFrame(0x11, TestHex.Bytes("03 00 6B 00 03"));

        Assert.Equal(TestHex.Bytes("11 03 00 6B 00 03 76 87"), frame);
    }

    [Fact]
    public void BuildFrame_Fc16_MatchesGoldenCrc()
    {
        // §7.1: "11 10 00 01 00 02 04 00 0A 01 02" → CRC C6 F0
        var frame = RtuFraming.BuildFrame(0x11, TestHex.Bytes("10 00 01 00 02 04 00 0A 01 02"));

        Assert.Equal(TestHex.Bytes("11 10 00 01 00 02 04 00 0A 01 02 C6 F0"), frame);
    }

    [Fact]
    public void BuildFrame_RejectsEmptyPdu()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RtuFraming.BuildFrame(0x01, ReadOnlySpan<byte>.Empty));
    }
}

/// <summary>RTU 응답 조립기(길이 예측) 테스트.</summary>
public sealed class RtuResponseAssemblerTests
{
    private static byte[] WithCrc(string frameHex)
    {
        var body = TestHex.Bytes(frameHex);
        var (lo, hi) = Crc16.ComputeBytes(body);
        return body.Concat(new[] { lo, hi }).ToArray();
    }

    [Fact]
    public void ReadResponse_CompletesAfterByteCountPredictedLength()
    {
        // FC03 응답: unit 11, pdu = 03 06 02 2B 00 00 00 64 (§7.3 골든 PDU)
        var frame = WithCrc("11 03 06 02 2B 00 00 00 64");
        var assembler = new RtuResponseAssembler(FunctionCode.ReadHoldingRegisters);

        for (var i = 0; i < frame.Length - 1; i++)
        {
            Assert.Equal(RtuAssemblyStatus.NeedMoreData, assembler.Feed(new[] { frame[i] }));
        }

        Assert.Equal(RtuAssemblyStatus.Complete, assembler.Feed(new[] { frame[^1] }));
        Assert.Equal(0x11, assembler.UnitId);
        Assert.Equal(TestHex.Bytes("03 06 02 2B 00 00 00 64"), assembler.GetPdu());
    }

    [Fact]
    public void EchoResponse_CompletesAtFixedLength8()
    {
        // §7.1 골든: "01 05 00 AC FF 00" → CRC 4C 1B
        var frame = TestHex.Bytes("01 05 00 AC FF 00 4C 1B");
        var assembler = new RtuResponseAssembler(FunctionCode.WriteSingleCoil);

        Assert.Equal(RtuAssemblyStatus.Complete, assembler.Feed(frame));
        Assert.Equal(TestHex.Bytes("05 00 AC FF 00"), assembler.GetPdu());
    }

    [Fact]
    public void ExceptionResponse_CompletesAtFixedLength5()
    {
        // §7.1 골든: "01 83 02" → CRC C0 F1
        var frame = TestHex.Bytes("01 83 02 C0 F1");
        var assembler = new RtuResponseAssembler(FunctionCode.ReadHoldingRegisters);

        Assert.Equal(RtuAssemblyStatus.Complete, assembler.Feed(frame));
        Assert.Equal(TestHex.Bytes("83 02"), assembler.GetPdu());
    }

    [Fact]
    public void CorruptedCrc_ReportsCrcMismatch()
    {
        var frame = TestHex.Bytes("01 83 02 C0 F2");
        var assembler = new RtuResponseAssembler(FunctionCode.ReadHoldingRegisters);

        Assert.Equal(RtuAssemblyStatus.CrcMismatch, assembler.Feed(frame));
    }

    [Fact]
    public void FunctionCodeMismatch_ReportsInvalid()
    {
        var assembler = new RtuResponseAssembler(FunctionCode.ReadHoldingRegisters);

        Assert.Equal(RtuAssemblyStatus.Invalid, assembler.Feed(TestHex.Bytes("01 04")));
    }

    [Fact]
    public void ExceptionFunctionCodeMismatch_ReportsInvalid()
    {
        var assembler = new RtuResponseAssembler(FunctionCode.ReadHoldingRegisters);

        Assert.Equal(RtuAssemblyStatus.Invalid, assembler.Feed(TestHex.Bytes("01 84")));
    }

    [Fact]
    public void ZeroByteCount_ReportsInvalid()
    {
        var assembler = new RtuResponseAssembler(FunctionCode.ReadHoldingRegisters);

        Assert.Equal(RtuAssemblyStatus.Invalid, assembler.Feed(TestHex.Bytes("01 03 00")));
    }

    [Fact]
    public void FeedAfterComplete_KeepsStateAndIgnoresRest()
    {
        var frame = TestHex.Bytes("01 83 02 C0 F1");
        var assembler = new RtuResponseAssembler(FunctionCode.ReadHoldingRegisters);
        assembler.Feed(frame);

        Assert.Equal(RtuAssemblyStatus.Complete, assembler.Feed(TestHex.Bytes("FF FF")));
        Assert.Equal(TestHex.Bytes("83 02"), assembler.GetPdu());
    }

    [Fact]
    public void GetPdu_BeforeComplete_Throws()
    {
        var assembler = new RtuResponseAssembler(FunctionCode.ReadHoldingRegisters);
        assembler.Feed(TestHex.Bytes("01 03"));

        Assert.Throws<InvalidOperationException>(() => assembler.GetPdu());
    }
}
