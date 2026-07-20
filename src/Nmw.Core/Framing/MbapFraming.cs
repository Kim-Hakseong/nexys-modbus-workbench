namespace Nmw.Core.Framing;

/// <summary>
/// Modbus TCP MBAP 헤더 프레이밍.
/// ADU = TransactionId(2) | ProtocolId(2)=0x0000 | Length(2) | UnitId(1) | PDU.
/// Length = UnitId(1) + PDU 길이. 모든 필드 big-endian.
/// </summary>
public static class MbapFraming
{
    /// <summary>MBAP 헤더 길이(바이트).</summary>
    public const int HeaderLength = 7;

    /// <summary>PDU 최대 길이 (Modbus 스펙 253바이트).</summary>
    public const int MaxPduLength = 253;

    /// <summary>MBAP ADU를 생성한다.</summary>
    /// <param name="transactionId">트랜잭션 ID.</param>
    /// <param name="unitId">Unit ID.</param>
    /// <param name="pdu">요청 PDU (1..253바이트).</param>
    /// <returns>전송할 ADU 바이트열.</returns>
    public static byte[] BuildAdu(ushort transactionId, byte unitId, ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length < 1 || pdu.Length > MaxPduLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pdu), pdu.Length, $"PDU 길이는 1..{MaxPduLength} 범위여야 합니다.");
        }

        var length = (ushort)(1 + pdu.Length);
        var adu = new byte[HeaderLength + pdu.Length];
        adu[0] = (byte)(transactionId >> 8);
        adu[1] = (byte)transactionId;
        adu[2] = 0x00;
        adu[3] = 0x00;
        adu[4] = (byte)(length >> 8);
        adu[5] = (byte)length;
        adu[6] = unitId;
        pdu.CopyTo(adu.AsSpan(HeaderLength));
        return adu;
    }
}
