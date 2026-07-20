using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Nmw.Core.Data;

/// <summary>표시 데이터 포맷.</summary>
public enum RegisterFormat
{
    /// <summary>Unsigned 16bit.</summary>
    U16,

    /// <summary>Signed 16bit (2의 보수).</summary>
    S16,

    /// <summary>16bit hex (0xXXXX).</summary>
    Hex16,

    /// <summary>16bit 2진수 (16자리).</summary>
    Bin16,

    /// <summary>Unsigned 32bit (레지스터 2개).</summary>
    U32,

    /// <summary>Signed 32bit (레지스터 2개).</summary>
    S32,

    /// <summary>IEEE754 float (레지스터 2개).</summary>
    Float32,

    /// <summary>Signed 64bit (레지스터 4개).</summary>
    S64,

    /// <summary>IEEE754 double (레지스터 4개).</summary>
    Double64,

    /// <summary>ASCII (레지스터당 2문자, 상위 바이트 먼저).</summary>
    Ascii,
}

/// <summary>32bit 이상 값의 워드/바이트 배열 순서 (r0=AB, r1=CD 수신 기준).</summary>
public enum WordOrder
{
    /// <summary>A B C D — Big-endian.</summary>
    ABCD,

    /// <summary>C D A B — Word swap (Modicon 전통).</summary>
    CDAB,

    /// <summary>B A D C — Byte swap.</summary>
    BADC,

    /// <summary>D C B A — Little-endian.</summary>
    DCBA,
}

/// <summary>
/// 수신 레지스터(ushort[])를 표시 포맷으로 변환한다.
/// 64bit는 2워드 스왑 규칙을 4워드로 확장해 적용한다.
/// </summary>
public static class RegisterFormatter
{
    /// <summary>해당 포맷이 소비하는 레지스터 개수. ASCII는 가변(1 반환).</summary>
    /// <param name="format">포맷.</param>
    /// <returns>레지스터 개수.</returns>
    public static int RegistersPerValue(RegisterFormat format) => format switch
    {
        RegisterFormat.U32 or RegisterFormat.S32 or RegisterFormat.Float32 => 2,
        RegisterFormat.S64 or RegisterFormat.Double64 => 4,
        _ => 1,
    };

    /// <summary>Float32로 변환한다 (레지스터 2개).</summary>
    /// <param name="registers">수신 레지스터 2개.</param>
    /// <param name="order">워드오더.</param>
    public static float ToFloat32(ReadOnlySpan<ushort> registers, WordOrder order)
    {
        Span<byte> bytes = stackalloc byte[4];
        WriteCanonicalBytes(registers, order, bytes, 2);
        return BinaryPrimitives.ReadSingleBigEndian(bytes);
    }

    /// <summary>U32로 변환한다 (레지스터 2개).</summary>
    /// <param name="registers">수신 레지스터 2개.</param>
    /// <param name="order">워드오더.</param>
    public static uint ToU32(ReadOnlySpan<ushort> registers, WordOrder order)
    {
        Span<byte> bytes = stackalloc byte[4];
        WriteCanonicalBytes(registers, order, bytes, 2);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    /// <summary>S32로 변환한다 (레지스터 2개).</summary>
    /// <param name="registers">수신 레지스터 2개.</param>
    /// <param name="order">워드오더.</param>
    public static int ToS32(ReadOnlySpan<ushort> registers, WordOrder order) =>
        unchecked((int)ToU32(registers, order));

    /// <summary>S64로 변환한다 (레지스터 4개).</summary>
    /// <param name="registers">수신 레지스터 4개.</param>
    /// <param name="order">워드오더.</param>
    public static long ToS64(ReadOnlySpan<ushort> registers, WordOrder order)
    {
        Span<byte> bytes = stackalloc byte[8];
        WriteCanonicalBytes(registers, order, bytes, 4);
        return BinaryPrimitives.ReadInt64BigEndian(bytes);
    }

    /// <summary>Double64로 변환한다 (레지스터 4개).</summary>
    /// <param name="registers">수신 레지스터 4개.</param>
    /// <param name="order">워드오더.</param>
    public static double ToDouble64(ReadOnlySpan<ushort> registers, WordOrder order)
    {
        Span<byte> bytes = stackalloc byte[8];
        WriteCanonicalBytes(registers, order, bytes, 4);
        return BinaryPrimitives.ReadDoubleBigEndian(bytes);
    }

    /// <summary>S16으로 변환한다 (2의 보수).</summary>
    /// <param name="register">수신 레지스터.</param>
    public static short ToS16(ushort register) => unchecked((short)register);

    /// <summary>
    /// 포맷에 따라 하나의 값을 문자열로 변환한다.
    /// <paramref name="registers"/>는 <see cref="RegistersPerValue"/> 이상이어야 하며
    /// ASCII는 전달된 전체 레지스터를 소비한다.
    /// </summary>
    /// <param name="registers">값을 구성하는 레지스터들.</param>
    /// <param name="format">포맷.</param>
    /// <param name="order">워드오더 (32bit 이상에서 적용).</param>
    /// <returns>표시 문자열.</returns>
    public static string Format(ReadOnlySpan<ushort> registers, RegisterFormat format, WordOrder order)
    {
        if (registers.Length < RegistersPerValue(format))
        {
            throw new ArgumentException(
                $"{format} 포맷은 레지스터 {RegistersPerValue(format)}개가 필요합니다.", nameof(registers));
        }

        return format switch
        {
            RegisterFormat.U16 => registers[0].ToString(CultureInfo.InvariantCulture),
            RegisterFormat.S16 => ToS16(registers[0]).ToString(CultureInfo.InvariantCulture),
            RegisterFormat.Hex16 => $"0x{registers[0]:X4}",
            RegisterFormat.Bin16 => Convert.ToString(registers[0], 2).PadLeft(16, '0'),
            RegisterFormat.U32 => ToU32(registers, order).ToString(CultureInfo.InvariantCulture),
            RegisterFormat.S32 => ToS32(registers, order).ToString(CultureInfo.InvariantCulture),
            RegisterFormat.Float32 => ToFloat32(registers, order).ToString("R", CultureInfo.InvariantCulture),
            RegisterFormat.S64 => ToS64(registers, order).ToString(CultureInfo.InvariantCulture),
            RegisterFormat.Double64 => ToDouble64(registers, order).ToString("R", CultureInfo.InvariantCulture),
            RegisterFormat.Ascii => FormatAscii(registers),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "지원하지 않는 포맷"),
        };
    }

    private static string FormatAscii(ReadOnlySpan<ushort> registers)
    {
        var sb = new StringBuilder(registers.Length * 2);
        foreach (var register in registers)
        {
            AppendAsciiChar(sb, (byte)(register >> 8));
            AppendAsciiChar(sb, (byte)register);
        }

        return sb.ToString();
    }

    private static void AppendAsciiChar(StringBuilder sb, byte value) =>
        sb.Append(value is >= 0x20 and <= 0x7E ? (char)value : '.');

    private static void WriteCanonicalBytes(
        ReadOnlySpan<ushort> registers, WordOrder order, Span<byte> destination, int wordCount)
    {
        if (registers.Length < wordCount)
        {
            throw new ArgumentException($"레지스터 {wordCount}개가 필요합니다.", nameof(registers));
        }

        var reverseWords = order is WordOrder.CDAB or WordOrder.DCBA;
        var swapBytes = order is WordOrder.BADC or WordOrder.DCBA;
        for (var i = 0; i < wordCount; i++)
        {
            var word = registers[reverseWords ? wordCount - 1 - i : i];
            var hi = (byte)(word >> 8);
            var lo = (byte)word;
            destination[i * 2] = swapBytes ? lo : hi;
            destination[(i * 2) + 1] = swapBytes ? hi : lo;
        }
    }
}
