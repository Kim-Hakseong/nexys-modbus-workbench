using Nmw.Core.Data;

namespace Nmw.Core.Tests;

/// <summary>DESIGN.md §7.5 포맷 변환 골든 벡터 테스트. 벡터 수정/삭제 금지.</summary>
public sealed class RegisterFormatterGoldenTests
{
    [Theory]
    [InlineData(new ushort[] { 0x4049, 0x0FDB }, WordOrder.ABCD)]
    [InlineData(new ushort[] { 0x0FDB, 0x4049 }, WordOrder.CDAB)]
    [InlineData(new ushort[] { 0x4940, 0xDB0F }, WordOrder.BADC)]
    [InlineData(new ushort[] { 0xDB0F, 0x4940 }, WordOrder.DCBA)]
    public void Float32_AllWordOrders_MatchGoldenPi(ushort[] registers, WordOrder order)
    {
        Assert.Equal(3.1415927f, RegisterFormatter.ToFloat32(registers, order));
    }

    [Fact]
    public void S32_Abcd_MatchesGoldenVector()
    {
        Assert.Equal(-123456, RegisterFormatter.ToS32(new ushort[] { 0xFFFE, 0x1DC0 }, WordOrder.ABCD));
    }

    [Fact]
    public void S16_MatchesGoldenVector()
    {
        Assert.Equal((short)-10, RegisterFormatter.ToS16(0xFFF6));
    }

    [Fact]
    public void Double64_Abcd_MatchesGoldenVector()
    {
        Assert.Equal(
            123.456,
            RegisterFormatter.ToDouble64(
                new ushort[] { 0x405E, 0xDD2F, 0x1A9F, 0xBE77 }, WordOrder.ABCD));
    }
}

/// <summary>포맷 문자열/보조 변환 테스트.</summary>
public sealed class RegisterFormatterTests
{
    [Theory]
    [InlineData(RegisterFormat.U16, 1)]
    [InlineData(RegisterFormat.S16, 1)]
    [InlineData(RegisterFormat.Hex16, 1)]
    [InlineData(RegisterFormat.Bin16, 1)]
    [InlineData(RegisterFormat.U32, 2)]
    [InlineData(RegisterFormat.S32, 2)]
    [InlineData(RegisterFormat.Float32, 2)]
    [InlineData(RegisterFormat.S64, 4)]
    [InlineData(RegisterFormat.Double64, 4)]
    [InlineData(RegisterFormat.Ascii, 1)]
    public void RegistersPerValue_ReturnsConsumedCount(RegisterFormat format, int expected)
    {
        Assert.Equal(expected, RegisterFormatter.RegistersPerValue(format));
    }

    [Theory]
    [InlineData(RegisterFormat.U16, "65526")]
    [InlineData(RegisterFormat.S16, "-10")]
    [InlineData(RegisterFormat.Hex16, "0xFFF6")]
    [InlineData(RegisterFormat.Bin16, "1111111111110110")]
    public void Format_16BitFormats(RegisterFormat format, string expected)
    {
        Assert.Equal(expected, RegisterFormatter.Format(new ushort[] { 0xFFF6 }, format, WordOrder.ABCD));
    }

    [Fact]
    public void Format_U32_UsesWordOrder()
    {
        // 0x0001_0000 = 65536, CDAB이면 워드 스왑
        Assert.Equal(
            "65536",
            RegisterFormatter.Format(new ushort[] { 0x0001, 0x0000 }, RegisterFormat.U32, WordOrder.ABCD));
        Assert.Equal(
            "65536",
            RegisterFormatter.Format(new ushort[] { 0x0000, 0x0001 }, RegisterFormat.U32, WordOrder.CDAB));
    }

    [Fact]
    public void Format_Ascii_HighByteFirst_NonPrintableAsDot()
    {
        // 'A'=0x41 'B'=0x42 'C'=0x43, 0x01은 비인쇄 문자
        Assert.Equal(
            "ABC.",
            RegisterFormatter.Format(
                new ushort[] { 0x4142, 0x4301 }, RegisterFormat.Ascii, WordOrder.ABCD));
    }

    [Fact]
    public void Format_S64_FourWordExtension()
    {
        // -1 = FFFF FFFF FFFF FFFF (모든 오더에서 동일)
        var regs = new ushort[] { 0xFFFF, 0xFFFF, 0xFFFF, 0xFFFF };
        Assert.Equal("-1", RegisterFormatter.Format(regs, RegisterFormat.S64, WordOrder.CDAB));
    }

    [Fact]
    public void Double64_CdabExtension_ReversesAllFourWords()
    {
        // ABCD 골든 [405E DD2F 1A9F BE77]의 워드 역순 → CDAB로 동일 값 복원
        Assert.Equal(
            123.456,
            RegisterFormatter.ToDouble64(
                new ushort[] { 0xBE77, 0x1A9F, 0xDD2F, 0x405E }, WordOrder.CDAB));
    }

    [Fact]
    public void Format_InsufficientRegisters_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => RegisterFormatter.Format(new ushort[] { 1 }, RegisterFormat.Float32, WordOrder.ABCD));
    }
}
