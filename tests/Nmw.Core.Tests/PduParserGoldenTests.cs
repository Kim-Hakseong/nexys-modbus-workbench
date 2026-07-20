using Nmw.Core.Protocol;

namespace Nmw.Core.Tests;

/// <summary>DESIGN.md §7.3 응답 파싱 골든 벡터 테스트. 벡터 수정/삭제 금지.</summary>
public sealed class PduParserGoldenTests
{
    [Fact]
    public void ParseReadRegistersResponse_Fc03_MatchesGoldenVector()
    {
        var result = PduParser.ParseReadRegistersResponse(
            TestHex.Bytes("03 06 02 2B 00 00 00 64"), FunctionCode.ReadHoldingRegisters, 3);

        Assert.True(result.IsSuccess);
        Assert.Equal(new ushort[] { 0x022B, 0x0000, 0x0064 }, result.Value);
    }

    [Fact]
    public void ParseReadBitsResponse_Fc01_MatchesGoldenVector()
    {
        var result = PduParser.ParseReadBitsResponse(
            TestHex.Bytes("01 03 CD 6B 05"), FunctionCode.ReadCoils, 19);

        Assert.True(result.IsSuccess);
        var expected = new[]
        {
            // CD = coil0..7
            true, false, true, true, false, false, true, true,
            // 6B = coil8..15
            true, true, false, true, false, true, true, false,
            // 05 = coil16..18
            true, false, true,
        };
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void Parse_ExceptionPdu_ReturnsIllegalDataAddress()
    {
        var result = PduParser.ParseReadRegistersResponse(
            TestHex.Bytes("83 02"), FunctionCode.ReadHoldingRegisters, 3);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ModbusErrorKind.Exception, result.Error.Kind);
        Assert.Equal(ModbusExceptionCode.IllegalDataAddress, result.Error.ExceptionCode);
        Assert.Equal("Illegal Data Address", result.Error.Text);
    }
}

/// <summary>파서 규격 검증(비골든) 테스트.</summary>
public sealed class PduParserValidationTests
{
    [Fact]
    public void ParseReadRegistersResponse_RejectsFunctionCodeMismatch()
    {
        var result = PduParser.ParseReadRegistersResponse(
            TestHex.Bytes("04 02 00 01"), FunctionCode.ReadHoldingRegisters, 1);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModbusErrorKind.InvalidResponse, result.Error!.Kind);
    }

    [Fact]
    public void ParseReadRegistersResponse_RejectsByteCountMismatch()
    {
        var result = PduParser.ParseReadRegistersResponse(
            TestHex.Bytes("03 04 00 01 00 02"), FunctionCode.ReadHoldingRegisters, 3);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModbusErrorKind.InvalidResponse, result.Error!.Kind);
    }

    [Fact]
    public void ParseReadBitsResponse_RejectsTruncatedPdu()
    {
        var result = PduParser.ParseReadBitsResponse(
            TestHex.Bytes("01 03 CD 6B"), FunctionCode.ReadCoils, 19);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModbusErrorKind.InvalidResponse, result.Error!.Kind);
    }

    [Fact]
    public void ParseReadBitsResponse_RejectsEmptyOrTooShortPdu()
    {
        var result = PduParser.ParseReadBitsResponse(
            ReadOnlySpan<byte>.Empty, FunctionCode.ReadCoils, 1);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModbusErrorKind.InvalidResponse, result.Error!.Kind);
    }

    [Fact]
    public void ParseWriteSingleResponse_AcceptsExactEcho()
    {
        var request = TestHex.Bytes("05 00 AC FF 00");

        var result = PduParser.ParseWriteSingleResponse(request, request);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ParseWriteSingleResponse_RejectsMismatchedEcho()
    {
        var request = TestHex.Bytes("05 00 AC FF 00");
        var response = TestHex.Bytes("05 00 AC 00 00");

        var result = PduParser.ParseWriteSingleResponse(response, request);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModbusErrorKind.InvalidResponse, result.Error!.Kind);
    }

    [Fact]
    public void ParseWriteSingleResponse_PropagatesException()
    {
        var request = TestHex.Bytes("06 00 01 00 03");

        var result = PduParser.ParseWriteSingleResponse(TestHex.Bytes("86 03"), request);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModbusErrorKind.Exception, result.Error!.Kind);
        Assert.Equal(ModbusExceptionCode.IllegalDataValue, result.Error.ExceptionCode);
    }

    [Fact]
    public void ParseWriteMultipleResponse_AcceptsMatchingAddressAndQuantity()
    {
        var result = PduParser.ParseWriteMultipleResponse(
            TestHex.Bytes("10 00 01 00 02"), FunctionCode.WriteMultipleRegisters, 0x0001, 2);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ParseWriteMultipleResponse_RejectsMismatchedQuantity()
    {
        var result = PduParser.ParseWriteMultipleResponse(
            TestHex.Bytes("10 00 01 00 03"), FunctionCode.WriteMultipleRegisters, 0x0001, 2);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModbusErrorKind.InvalidResponse, result.Error!.Kind);
    }

    [Fact]
    public void ExceptionText_UnknownCode_FormatsAsHex()
    {
        var result = PduParser.ParseReadRegistersResponse(
            TestHex.Bytes("83 7F"), FunctionCode.ReadHoldingRegisters, 1);

        Assert.False(result.IsSuccess);
        Assert.Equal("Unknown Exception (0x7F)", result.Error!.Text);
    }

    [Fact]
    public void FailedResult_ValueAccess_Throws()
    {
        var result = PduParser.ParseReadRegistersResponse(
            TestHex.Bytes("83 02"), FunctionCode.ReadHoldingRegisters, 1);

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }
}
