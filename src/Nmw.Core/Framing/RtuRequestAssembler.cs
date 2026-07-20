using System.Runtime.InteropServices;

namespace Nmw.Core.Framing;

/// <summary>RTU 요청 조립 상태.</summary>
public enum RtuRequestStatus
{
    /// <summary>기대 길이에 도달하지 않아 추가 수신이 필요함.</summary>
    NeedMoreData,

    /// <summary>요청 프레임 완성 + CRC 유효.</summary>
    Complete,

    /// <summary>요청 프레임 완성이나 CRC 불일치 (슬레이브는 무응답).</summary>
    CrcMismatch,

    /// <summary>지원하지 않는 FC 등 길이 예측이 불가능한 요청.</summary>
    Invalid,
}

/// <summary>
/// 슬레이브(시뮬레이터) 측 RTU 요청 수신 조립기. FC로 요청 길이를 예측한다:
/// FC01~06은 고정 8바이트(unit+pdu5+crc2), FC15/16은 byteCount(7번째 바이트) 수신 후
/// 9+byteCount로 확정. 완성 시 CRC를 검증한다. Reset()으로 재사용한다.
/// </summary>
public sealed class RtuRequestAssembler
{
    private readonly List<byte> _buffer = [];
    private int _expectedLength = -1;

    /// <summary>현재 조립 상태.</summary>
    public RtuRequestStatus Status { get; private set; } = RtuRequestStatus.NeedMoreData;

    /// <summary>완성된 요청의 UnitId. Complete 상태에서만 접근한다.</summary>
    public byte UnitId => Status == RtuRequestStatus.Complete
        ? _buffer[0]
        : throw new InvalidOperationException("완성(Complete) 상태가 아닙니다.");

    /// <summary>완성된 요청의 PDU(UnitId·CRC 제외). Complete 상태에서만 접근한다.</summary>
    public byte[] GetPdu() => Status == RtuRequestStatus.Complete
        ? _buffer.GetRange(1, _buffer.Count - 3).ToArray()
        : throw new InvalidOperationException("완성(Complete) 상태가 아닙니다.");

    /// <summary>다음 요청 수신을 위해 상태를 초기화한다.</summary>
    public void Reset()
    {
        _buffer.Clear();
        _expectedLength = -1;
        Status = RtuRequestStatus.NeedMoreData;
    }

    /// <summary>수신 바이트 1개를 공급한다. 종결 상태 이후에는 Reset이 필요하다.</summary>
    /// <param name="value">수신 바이트.</param>
    /// <returns>공급 후의 조립 상태.</returns>
    public RtuRequestStatus FeedByte(byte value)
    {
        if (Status != RtuRequestStatus.NeedMoreData)
        {
            throw new InvalidOperationException("종결 상태입니다. Reset 후 사용하세요.");
        }

        _buffer.Add(value);

        if (_buffer.Count == 2)
        {
            _expectedLength = _buffer[1] switch
            {
                0x01 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 => 8, // unit + fc + addr2 + qty/value2 + crc2
                0x0F or 0x10 => -1,                                // byteCount 수신 후 확정
                _ => 0,
            };
            if (_expectedLength == 0)
            {
                Status = RtuRequestStatus.Invalid;
            }

            return Status;
        }

        if (_buffer.Count == 7 && _expectedLength == -1)
        {
            var byteCount = _buffer[6];
            if (byteCount == 0 || 9 + byteCount > RtuFraming.MaxFrameLength)
            {
                Status = RtuRequestStatus.Invalid;
                return Status;
            }

            _expectedLength = 9 + byteCount; // unit + fc + addr2 + qty2 + byteCount1 + data + crc2
            return Status;
        }

        if (_expectedLength > 0 && _buffer.Count == _expectedLength)
        {
            Status = Crc16.Validate(CollectionsMarshal.AsSpan(_buffer))
                ? RtuRequestStatus.Complete
                : RtuRequestStatus.CrcMismatch;
        }

        return Status;
    }
}
