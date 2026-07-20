namespace Nmw.Core.Framing;

/// <summary>
/// Modbus RTU CRC-16 계산기.
/// 다항식 0xA001(reflected 0x8005), 초기값 0xFFFF, XOR out 없음.
/// 프레임 끝에는 low byte가 먼저 붙는다.
/// </summary>
public static class Crc16
{
    private static readonly ushort[] Table = BuildTable();

    private static ushort[] BuildTable()
    {
        var table = new ushort[256];
        for (var i = 0; i < 256; i++)
        {
            var value = (ushort)i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 0x0001) != 0
                    ? (ushort)((value >> 1) ^ 0xA001)
                    : (ushort)(value >> 1);
            }

            table[i] = value;
        }

        return table;
    }

    /// <summary>주어진 바이트열의 CRC16 값을 계산한다.</summary>
    /// <param name="data">CRC를 계산할 프레임 바이트열(CRC 제외).</param>
    /// <returns>계산된 CRC16 값.</returns>
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc = (ushort)((crc >> 8) ^ Table[(crc ^ b) & 0xFF]);
        }

        return crc;
    }

    /// <summary>프레임 뒤에 붙일 CRC 2바이트를 전송 순서(low, high)로 반환한다.</summary>
    /// <param name="data">CRC를 계산할 프레임 바이트열(CRC 제외).</param>
    /// <returns>전송 순서의 (low byte, high byte) 튜플.</returns>
    public static (byte Lo, byte Hi) ComputeBytes(ReadOnlySpan<byte> data)
    {
        var crc = Compute(data);
        return ((byte)(crc & 0xFF), (byte)(crc >> 8));
    }

    /// <summary>마지막 2바이트가 CRC(lo, hi)인 전체 프레임의 CRC 유효성을 검사한다.</summary>
    /// <param name="frameWithCrc">CRC 2바이트를 포함한 전체 프레임.</param>
    /// <returns>CRC가 일치하면 true.</returns>
    public static bool Validate(ReadOnlySpan<byte> frameWithCrc)
    {
        if (frameWithCrc.Length < 3)
        {
            return false;
        }

        var crc = Compute(frameWithCrc[..^2]);
        return frameWithCrc[^2] == (byte)(crc & 0xFF)
            && frameWithCrc[^1] == (byte)(crc >> 8);
    }
}
