using Nmw.Core.Data;
using Nmw.Core.Protocol;

namespace Nmw.Core.Tests;

/// <summary>주소 표기 변환 테스트 (DESIGN §5).</summary>
public sealed class AddressNotationTests
{
    [Theory]
    [InlineData(FunctionCode.ReadCoils, AddressArea.Coil)]
    [InlineData(FunctionCode.WriteSingleCoil, AddressArea.Coil)]
    [InlineData(FunctionCode.WriteMultipleCoils, AddressArea.Coil)]
    [InlineData(FunctionCode.ReadDiscreteInputs, AddressArea.DiscreteInput)]
    [InlineData(FunctionCode.ReadInputRegisters, AddressArea.InputRegister)]
    [InlineData(FunctionCode.ReadHoldingRegisters, AddressArea.HoldingRegister)]
    [InlineData(FunctionCode.WriteSingleRegister, AddressArea.HoldingRegister)]
    [InlineData(FunctionCode.WriteMultipleRegisters, AddressArea.HoldingRegister)]
    public void AreaOf_MapsFunctionToArea(FunctionCode function, AddressArea expected)
    {
        Assert.Equal(expected, AddressNotation.AreaOf(function));
    }

    [Theory]
    [InlineData(0, AddressArea.HoldingRegister, "40001")] // DESIGN §5 예시
    [InlineData(0, AddressArea.Coil, "00001")]
    [InlineData(0, AddressArea.DiscreteInput, "10001")]
    [InlineData(0, AddressArea.InputRegister, "30001")]
    [InlineData(9998, AddressArea.HoldingRegister, "49999")]
    [InlineData(9999, AddressArea.HoldingRegister, "410000")] // 6자리 확장
    [InlineData(65535, AddressArea.HoldingRegister, "465536")]
    public void Format_OneBase_UsesAreaPrefix(int address, AddressArea area, string expected)
    {
        Assert.Equal(expected, AddressNotation.Format((ushort)address, area, AddressBase.OneBase));
    }

    [Fact]
    public void Format_ZeroBase_IsPlainDecimal()
    {
        Assert.Equal("123", AddressNotation.Format(123, AddressArea.HoldingRegister, AddressBase.ZeroBase));
    }

    [Theory]
    [InlineData("40001", 0)]
    [InlineData("400001", 0)] // 6자리 스타일
    [InlineData("49999", 9998)]
    [InlineData("465536", 65535)]
    [InlineData("7", 6)] // 프리픽스 없는 1-base
    public void TryParse_OneBase_Holding(string text, int expected)
    {
        Assert.True(AddressNotation.TryParse(
            text, AddressArea.HoldingRegister, AddressBase.OneBase, out var address));
        Assert.Equal((ushort)expected, address);
    }

    [Fact]
    public void TryParse_OneBase_WrongAreaPrefix_Fails()
    {
        Assert.False(AddressNotation.TryParse(
            "30001", AddressArea.HoldingRegister, AddressBase.OneBase, out _));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("40000")] // OneBase에서 offset 0은 없음... 프리픽스 4 + 0000 → offset 0 → 실패
    [InlineData("")]
    [InlineData("abc")]
    public void TryParse_OneBase_Invalid_Fails(string text)
    {
        Assert.False(AddressNotation.TryParse(
            text, AddressArea.HoldingRegister, AddressBase.OneBase, out _));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("65535", 65535)]
    public void TryParse_ZeroBase(string text, int expected)
    {
        Assert.True(AddressNotation.TryParse(
            text, AddressArea.HoldingRegister, AddressBase.ZeroBase, out var address));
        Assert.Equal((ushort)expected, address);
    }

    [Fact]
    public void RoundTrip_FormatThenParse_AllAreas()
    {
        foreach (var area in Enum.GetValues<AddressArea>())
        {
            foreach (ushort address in new ushort[] { 0, 1, 9998, 9999, 30000, 65535 })
            {
                var text = AddressNotation.Format(address, area, AddressBase.OneBase);
                Assert.True(AddressNotation.TryParse(text, area, AddressBase.OneBase, out var parsed));
                Assert.Equal(address, parsed);
            }
        }
    }
}
