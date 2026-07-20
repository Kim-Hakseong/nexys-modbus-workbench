using Nmw.Core.Protocol;

namespace Nmw.Core.Tests;

/// <summary>DESIGN.md §7.2 PDU 빌드 골든 벡터 테스트. 벡터 수정/삭제 금지.</summary>
public sealed class PduBuilderGoldenTests
{
    [Fact]
    public void BuildReadRequest_Fc03_MatchesGoldenVector()
    {
        var pdu = PduBuilder.BuildReadRequest(FunctionCode.ReadHoldingRegisters, 0x006B, 3);

        Assert.Equal(TestHex.Bytes("03 00 6B 00 03"), pdu);
    }

    [Fact]
    public void BuildWriteSingleCoil_On_MatchesGoldenVector()
    {
        var pdu = PduBuilder.BuildWriteSingleCoil(0x00AC, on: true);

        Assert.Equal(TestHex.Bytes("05 00 AC FF 00"), pdu);
    }

    [Fact]
    public void BuildWriteMultipleRegisters_MatchesGoldenVector()
    {
        var pdu = PduBuilder.BuildWriteMultipleRegisters(0x0001, new ushort[] { 0x000A, 0x0102 });

        Assert.Equal(TestHex.Bytes("10 00 01 00 02 04 00 0A 01 02"), pdu);
    }
}

/// <summary>PDU 빌더 경계/규격 검증 테스트.</summary>
public sealed class PduBuilderValidationTests
{
    [Fact]
    public void BuildWriteSingleCoil_Off_UsesZeroValue()
    {
        var pdu = PduBuilder.BuildWriteSingleCoil(0x0001, on: false);

        Assert.Equal(TestHex.Bytes("05 00 01 00 00"), pdu);
    }

    [Fact]
    public void BuildWriteSingleRegister_EncodesBigEndian()
    {
        var pdu = PduBuilder.BuildWriteSingleRegister(0x0001, 0x0003);

        Assert.Equal(TestHex.Bytes("06 00 01 00 03"), pdu);
    }

    [Fact]
    public void BuildWriteMultipleCoils_PacksBitsLsbFirstWithPadding()
    {
        // Modbus 스펙 예제: addr=0x0013, 10개, coil0..9 = [1,0,1,1,0,0,1,1, 1,0] → CD 01
        var values = new[] { true, false, true, true, false, false, true, true, true, false };

        var pdu = PduBuilder.BuildWriteMultipleCoils(0x0013, values);

        Assert.Equal(TestHex.Bytes("0F 00 13 00 0A 02 CD 01"), pdu);
    }

    [Theory]
    [InlineData(FunctionCode.ReadCoils, 0)]
    [InlineData(FunctionCode.ReadCoils, 2001)]
    [InlineData(FunctionCode.ReadDiscreteInputs, 2001)]
    [InlineData(FunctionCode.ReadHoldingRegisters, 0)]
    [InlineData(FunctionCode.ReadHoldingRegisters, 126)]
    [InlineData(FunctionCode.ReadInputRegisters, 126)]
    public void BuildReadRequest_RejectsOutOfRangeQuantity(FunctionCode function, int quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PduBuilder.BuildReadRequest(function, 0, (ushort)quantity));
    }

    [Theory]
    [InlineData(FunctionCode.ReadCoils, 2000)]
    [InlineData(FunctionCode.ReadHoldingRegisters, 125)]
    public void BuildReadRequest_AcceptsMaxQuantity(FunctionCode function, int quantity)
    {
        var pdu = PduBuilder.BuildReadRequest(function, 0, (ushort)quantity);

        Assert.Equal(5, pdu.Length);
    }

    [Fact]
    public void BuildReadRequest_RejectsWriteFunctionCode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PduBuilder.BuildReadRequest(FunctionCode.WriteSingleCoil, 0, 1));
    }

    [Fact]
    public void BuildWriteMultipleCoils_RejectsEmptyAndOversize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PduBuilder.BuildWriteMultipleCoils(0, Array.Empty<bool>()));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PduBuilder.BuildWriteMultipleCoils(0, new bool[1969]));
    }

    [Fact]
    public void BuildWriteMultipleRegisters_RejectsEmptyAndOversize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PduBuilder.BuildWriteMultipleRegisters(0, Array.Empty<ushort>()));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PduBuilder.BuildWriteMultipleRegisters(0, new ushort[124]));
    }
}
