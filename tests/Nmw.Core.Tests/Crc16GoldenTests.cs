using Nmw.Core.Framing;

namespace Nmw.Core.Tests;

/// <summary>DESIGN.md §7.1 CRC16 골든 벡터 테스트. 벡터 수정/삭제 금지.</summary>
public sealed class Crc16GoldenTests
{
    public static TheoryData<string, byte, byte> GoldenVectors => new()
    {
        { "01 03 00 00 00 0A", 0xC5, 0xCD },
        { "11 03 00 6B 00 03", 0x76, 0x87 },
        { "01 06 00 01 00 03", 0x98, 0x0B },
        { "01 83 02", 0xC0, 0xF1 },
        { "01 05 00 AC FF 00", 0x4C, 0x1B },
        { "11 10 00 01 00 02 04 00 0A 01 02", 0xC6, 0xF0 },
    };

    [Theory]
    [MemberData(nameof(GoldenVectors))]
    public void ComputeBytes_MatchesGoldenVector(string frameHex, byte expectedLo, byte expectedHi)
    {
        var (lo, hi) = Crc16.ComputeBytes(TestHex.Bytes(frameHex));

        Assert.Equal(expectedLo, lo);
        Assert.Equal(expectedHi, hi);
    }

    [Theory]
    [MemberData(nameof(GoldenVectors))]
    public void Validate_AcceptsFrameWithGoldenCrc(string frameHex, byte expectedLo, byte expectedHi)
    {
        var frame = TestHex.Bytes(frameHex);
        var withCrc = frame.Concat(new[] { expectedLo, expectedHi }).ToArray();

        Assert.True(Crc16.Validate(withCrc));
    }

    [Fact]
    public void Validate_RejectsCorruptedFrame()
    {
        var frame = TestHex.Bytes("01 03 00 00 00 0A C5 CD");
        frame[2] ^= 0x01;

        Assert.False(Crc16.Validate(frame));
    }

    [Fact]
    public void Validate_RejectsTooShortFrame()
    {
        Assert.False(Crc16.Validate(TestHex.Bytes("C5 CD")));
    }
}
