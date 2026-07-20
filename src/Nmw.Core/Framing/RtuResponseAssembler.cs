using Nmw.Core.Protocol;

namespace Nmw.Core.Framing;

/// <summary>RTU 응답 조립 상태.</summary>
public enum RtuAssemblyStatus
{
    /// <summary>기대 길이에 도달하지 않아 추가 수신이 필요함.</summary>
    NeedMoreData,

    /// <summary>프레임 완성 + CRC 유효.</summary>
    Complete,

    /// <summary>프레임 완성이나 CRC 불일치.</summary>
    CrcMismatch,

    /// <summary>function code가 요청과 맞지 않는 등 길이 예측이 불가능한 응답.</summary>
    Invalid,
}

/// <summary>
/// RTU 응답 수신 조립기. 문자간 침묵 검출 대신 <b>길이 예측 + 타임아웃</b> 방식:
/// UnitId+FC 수신 → FC로 기대 길이 계산(읽기 응답은 byteCount 수신 후 확정,
/// 에코형은 고정 8바이트, exception은 5바이트) → 기대 길이 도달 시 CRC 검증.
/// 타임아웃은 트랜스포트 계층이 담당한다. 요청 1건마다 새 인스턴스를 사용한다.
/// </summary>
public sealed class RtuResponseAssembler
{
    private readonly byte _requestFunction;
    private readonly List<byte> _buffer = [];
    private int _expectedLength = -1;

    /// <summary>요청에 사용한 function code 기준으로 조립기를 만든다.</summary>
    /// <param name="requestFunction">요청 function code.</param>
    public RtuResponseAssembler(FunctionCode requestFunction)
    {
        _requestFunction = (byte)requestFunction;
    }

    /// <summary>현재 조립 상태.</summary>
    public RtuAssemblyStatus Status { get; private set; } = RtuAssemblyStatus.NeedMoreData;

    /// <summary>완성된 프레임의 UnitId. 완성 전 접근 금지.</summary>
    public byte UnitId => Status is RtuAssemblyStatus.Complete or RtuAssemblyStatus.CrcMismatch
        ? _buffer[0]
        : throw new InvalidOperationException("프레임이 완성되지 않았습니다.");

    /// <summary>완성된 프레임의 PDU(UnitId·CRC 제외). Complete 상태에서만 접근한다.</summary>
    public byte[] GetPdu() => Status == RtuAssemblyStatus.Complete
        ? _buffer.GetRange(1, _buffer.Count - 3).ToArray()
        : throw new InvalidOperationException("완성(Complete) 상태가 아닙니다.");

    /// <summary>수신 바이트들을 조립기에 공급한다.</summary>
    /// <param name="data">수신 데이터.</param>
    /// <returns>공급 후의 조립 상태.</returns>
    public RtuAssemblyStatus Feed(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            if (Status != RtuAssemblyStatus.NeedMoreData)
            {
                return Status;
            }

            FeedByte(b);
        }

        return Status;
    }

    private void FeedByte(byte value)
    {
        _buffer.Add(value);

        if (_buffer.Count == 2)
        {
            var fc = _buffer[1];
            if ((fc & 0x80) != 0)
            {
                if ((fc & 0x7F) != _requestFunction)
                {
                    Status = RtuAssemblyStatus.Invalid;
                    return;
                }

                _expectedLength = 5; // unit + fc + exception code + crc2
                return;
            }

            if (fc != _requestFunction)
            {
                Status = RtuAssemblyStatus.Invalid;
                return;
            }

            _expectedLength = (FunctionCode)fc switch
            {
                FunctionCode.ReadCoils or FunctionCode.ReadDiscreteInputs or
                FunctionCode.ReadHoldingRegisters or FunctionCode.ReadInputRegisters => -1, // byteCount 수신 후 확정
                FunctionCode.WriteSingleCoil or FunctionCode.WriteSingleRegister or
                FunctionCode.WriteMultipleCoils or FunctionCode.WriteMultipleRegisters => 8, // unit + pdu5 + crc2
                _ => 0,
            };

            if (_expectedLength == 0)
            {
                Status = RtuAssemblyStatus.Invalid;
            }

            return;
        }

        if (_buffer.Count == 3 && _expectedLength == -1)
        {
            var byteCount = _buffer[2];
            if (byteCount == 0 || 3 + byteCount + 2 > RtuFraming.MaxFrameLength)
            {
                Status = RtuAssemblyStatus.Invalid;
                return;
            }

            _expectedLength = 3 + byteCount + 2; // unit + fc + byteCount + data + crc2
            return;
        }

        if (_expectedLength > 0 && _buffer.Count == _expectedLength)
        {
            Status = Crc16.Validate(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buffer))
                ? RtuAssemblyStatus.Complete
                : RtuAssemblyStatus.CrcMismatch;
        }
    }
}
