namespace Nmw.Core.Framing;

/// <summary>
/// Modbus RTU 프레이밍. 프레임 = UnitId(1) | PDU | CRC(2, low 먼저).
/// </summary>
public static class RtuFraming
{
    /// <summary>RTU ADU 최대 길이 (Modbus 스펙 256바이트).</summary>
    public const int MaxFrameLength = 256;

    /// <summary>RTU 요청 프레임을 생성한다.</summary>
    /// <param name="unitId">슬레이브 Unit ID.</param>
    /// <param name="pdu">요청 PDU (1..253바이트).</param>
    /// <returns>CRC가 붙은 전송 프레임.</returns>
    public static byte[] BuildFrame(byte unitId, ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length < 1 || pdu.Length > MaxFrameLength - 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pdu), pdu.Length, $"PDU 길이는 1..{MaxFrameLength - 3} 범위여야 합니다.");
        }

        var frame = new byte[1 + pdu.Length + 2];
        frame[0] = unitId;
        pdu.CopyTo(frame.AsSpan(1));
        var (lo, hi) = Crc16.ComputeBytes(frame.AsSpan(0, 1 + pdu.Length));
        frame[^2] = lo;
        frame[^1] = hi;
        return frame;
    }
}
