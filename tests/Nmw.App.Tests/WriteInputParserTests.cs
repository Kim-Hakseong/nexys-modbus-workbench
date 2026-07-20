using Nmw.App.Models;
using Xunit;

namespace Nmw.App.Tests;

/// <summary>쓰기 다이얼로그 입력 파서 테스트.</summary>
public sealed class WriteInputParserTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("65535", 65535)]
    [InlineData("0xBEEF", 0xBEEF)]
    [InlineData("0x0001", 1)]
    [InlineData(" 42 ", 42)]
    public void TryParseRegister_Valid(string text, int expected)
    {
        Assert.True(WriteInputParser.TryParseRegister(text, out var value));
        Assert.Equal((ushort)expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("0xGGGG")]
    [InlineData("abc")]
    public void TryParseRegister_Invalid(string text)
    {
        Assert.False(WriteInputParser.TryParseRegister(text, out _));
    }

    [Fact]
    public void TryParseRegisterList_MixedRadix()
    {
        Assert.True(WriteInputParser.TryParseRegisterList("10, 0x0102; 3", out var values));
        Assert.Equal(new ushort[] { 10, 0x0102, 3 }, values);
    }

    [Fact]
    public void TryParseRegisterList_Empty_Fails()
    {
        Assert.False(WriteInputParser.TryParseRegisterList("  ", out _));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("ON", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData("off", false)]
    public void TryParseBit_Valid(string text, bool expected)
    {
        Assert.True(WriteInputParser.TryParseBit(text, out var on));
        Assert.Equal(expected, on);
    }

    [Fact]
    public void TryParseBitList_Valid()
    {
        Assert.True(WriteInputParser.TryParseBitList("1,0,1 1", out var values));
        Assert.Equal(new[] { true, false, true, true }, values);
    }

    [Fact]
    public void TryParseBitList_InvalidToken_Fails()
    {
        Assert.False(WriteInputParser.TryParseBitList("1,2", out _));
    }
}
