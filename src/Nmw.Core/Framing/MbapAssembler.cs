using System.Diagnostics.CodeAnalysis;

namespace Nmw.Core.Framing;

/// <summary>MBAP 스트림에서 조립된 하나의 완전한 프레임.</summary>
/// <param name="TransactionId">트랜잭션 ID.</param>
/// <param name="UnitId">Unit ID.</param>
/// <param name="Pdu">응답 PDU.</param>
public sealed record MbapFrame(ushort TransactionId, byte UnitId, byte[] Pdu);

/// <summary>
/// TCP 스트림의 부분 수신을 처리하는 MBAP 프레임 조립 상태머신.
/// 헤더 7바이트를 완독한 뒤 Length 만큼 추가로 완독해 프레임을 만든다.
/// 규격 위반(ProtocolId≠0, Length 범위 초과)이 감지되면 손상 상태가 되며,
/// 이후 스트림은 신뢰할 수 없으므로 호출측은 연결을 재수립해야 한다.
/// </summary>
public sealed class MbapAssembler
{
    private const int MinLengthField = 2;    // UnitId(1) + PDU 최소 1바이트
    private const int MaxLengthField = 254;  // UnitId(1) + PDU 최대 253바이트

    private readonly List<byte> _buffer = [];

    /// <summary>스트림 손상 여부. true면 이후 프레임을 생산하지 않는다.</summary>
    public bool IsCorrupted { get; private set; }

    /// <summary>손상 사유(있으면).</summary>
    public string? CorruptReason { get; private set; }

    /// <summary>수신 바이트를 조립 버퍼에 추가한다.</summary>
    /// <param name="data">수신 데이터.</param>
    public void Feed(ReadOnlySpan<byte> data)
    {
        if (IsCorrupted)
        {
            return;
        }

        foreach (var b in data)
        {
            _buffer.Add(b);
        }
    }

    /// <summary>완성된 프레임이 있으면 꺼낸다.</summary>
    /// <param name="frame">꺼낸 프레임.</param>
    /// <returns>프레임을 꺼냈으면 true.</returns>
    public bool TryTakeFrame([NotNullWhen(true)] out MbapFrame? frame)
    {
        frame = null;
        if (IsCorrupted || _buffer.Count < MbapFraming.HeaderLength)
        {
            return false;
        }

        var protocolId = (ushort)((_buffer[2] << 8) | _buffer[3]);
        if (protocolId != 0)
        {
            MarkCorrupted($"ProtocolId가 0이 아닙니다 (0x{protocolId:X4})");
            return false;
        }

        var length = (_buffer[4] << 8) | _buffer[5];
        if (length < MinLengthField || length > MaxLengthField)
        {
            MarkCorrupted($"Length 필드 범위 오류 ({length})");
            return false;
        }

        var totalLength = 6 + length;
        if (_buffer.Count < totalLength)
        {
            return false;
        }

        var transactionId = (ushort)((_buffer[0] << 8) | _buffer[1]);
        var unitId = _buffer[6];
        var pdu = new byte[length - 1];
        _buffer.CopyTo(MbapFraming.HeaderLength, pdu, 0, pdu.Length);
        _buffer.RemoveRange(0, totalLength);

        frame = new MbapFrame(transactionId, unitId, pdu);
        return true;
    }

    /// <summary>조립 상태를 초기화한다 (재연결 시 사용).</summary>
    public void Reset()
    {
        _buffer.Clear();
        IsCorrupted = false;
        CorruptReason = null;
    }

    private void MarkCorrupted(string reason)
    {
        IsCorrupted = true;
        CorruptReason = reason;
        _buffer.Clear();
    }
}
