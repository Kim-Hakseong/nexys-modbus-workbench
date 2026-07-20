namespace Nmw.Core.Protocol;

/// <summary>
/// Modbus 요청 PDU 빌더. 모든 멀티바이트 필드는 big-endian.
/// 규격 위반 입력은 <see cref="ArgumentOutOfRangeException"/>으로 즉시 거부한다.
/// </summary>
public static class PduBuilder
{
    /// <summary>FC01/02 읽기 요청의 최대 개수.</summary>
    public const ushort MaxReadBits = 2000;

    /// <summary>FC03/04 읽기 요청의 최대 개수.</summary>
    public const ushort MaxReadRegisters = 125;

    /// <summary>FC15 쓰기 요청의 최대 코일 개수 (byteCount 1바이트 제약, Modbus 스펙 0x07B0).</summary>
    public const ushort MaxWriteBits = 1968;

    /// <summary>FC16 쓰기 요청의 최대 레지스터 개수 (Modbus 스펙 0x7B).</summary>
    public const ushort MaxWriteRegisters = 123;

    /// <summary>FC01~04 읽기 요청 PDU를 생성한다.</summary>
    /// <param name="function">읽기 function code (FC01~04).</param>
    /// <param name="startAddress">시작 주소(0-base 프로토콜 주소).</param>
    /// <param name="quantity">읽을 개수 (코일 1..2000, 레지스터 1..125).</param>
    /// <returns>요청 PDU 바이트열.</returns>
    public static byte[] BuildReadRequest(FunctionCode function, ushort startAddress, ushort quantity)
    {
        var max = function switch
        {
            FunctionCode.ReadCoils or FunctionCode.ReadDiscreteInputs => MaxReadBits,
            FunctionCode.ReadHoldingRegisters or FunctionCode.ReadInputRegisters => MaxReadRegisters,
            _ => throw new ArgumentOutOfRangeException(
                nameof(function), function, "읽기 function code(FC01~04)가 아닙니다."),
        };

        if (quantity < 1 || quantity > max)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, $"{function} 개수는 1..{max} 범위여야 합니다.");
        }

        return
        [
            (byte)function,
            (byte)(startAddress >> 8), (byte)startAddress,
            (byte)(quantity >> 8), (byte)quantity,
        ];
    }

    /// <summary>FC05 Write Single Coil 요청 PDU를 생성한다.</summary>
    /// <param name="address">코일 주소(0-base).</param>
    /// <param name="on">true면 0xFF00, false면 0x0000.</param>
    /// <returns>요청 PDU 바이트열.</returns>
    public static byte[] BuildWriteSingleCoil(ushort address, bool on)
    {
        return
        [
            (byte)FunctionCode.WriteSingleCoil,
            (byte)(address >> 8), (byte)address,
            on ? (byte)0xFF : (byte)0x00, 0x00,
        ];
    }

    /// <summary>FC06 Write Single Register 요청 PDU를 생성한다.</summary>
    /// <param name="address">레지스터 주소(0-base).</param>
    /// <param name="value">쓸 값.</param>
    /// <returns>요청 PDU 바이트열.</returns>
    public static byte[] BuildWriteSingleRegister(ushort address, ushort value)
    {
        return
        [
            (byte)FunctionCode.WriteSingleRegister,
            (byte)(address >> 8), (byte)address,
            (byte)(value >> 8), (byte)value,
        ];
    }

    /// <summary>FC15 Write Multiple Coils 요청 PDU를 생성한다.</summary>
    /// <param name="startAddress">시작 주소(0-base).</param>
    /// <param name="values">쓸 코일 값들 (1..1968개).</param>
    /// <returns>요청 PDU 바이트열.</returns>
    public static byte[] BuildWriteMultipleCoils(ushort startAddress, IReadOnlyList<bool> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < 1 || values.Count > MaxWriteBits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(values), values.Count, $"코일 개수는 1..{MaxWriteBits} 범위여야 합니다.");
        }

        var quantity = (ushort)values.Count;
        var byteCount = (byte)((quantity + 7) / 8);
        var pdu = new byte[6 + byteCount];
        pdu[0] = (byte)FunctionCode.WriteMultipleCoils;
        pdu[1] = (byte)(startAddress >> 8);
        pdu[2] = (byte)startAddress;
        pdu[3] = (byte)(quantity >> 8);
        pdu[4] = (byte)quantity;
        pdu[5] = byteCount;
        for (var i = 0; i < quantity; i++)
        {
            if (values[i])
            {
                pdu[6 + (i / 8)] |= (byte)(1 << (i % 8));
            }
        }

        return pdu;
    }

    /// <summary>FC16 Write Multiple Registers 요청 PDU를 생성한다.</summary>
    /// <param name="startAddress">시작 주소(0-base).</param>
    /// <param name="values">쓸 레지스터 값들 (1..123개).</param>
    /// <returns>요청 PDU 바이트열.</returns>
    public static byte[] BuildWriteMultipleRegisters(ushort startAddress, IReadOnlyList<ushort> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < 1 || values.Count > MaxWriteRegisters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(values), values.Count, $"레지스터 개수는 1..{MaxWriteRegisters} 범위여야 합니다.");
        }

        var quantity = (ushort)values.Count;
        var byteCount = (byte)(quantity * 2);
        var pdu = new byte[6 + byteCount];
        pdu[0] = (byte)FunctionCode.WriteMultipleRegisters;
        pdu[1] = (byte)(startAddress >> 8);
        pdu[2] = (byte)startAddress;
        pdu[3] = (byte)(quantity >> 8);
        pdu[4] = (byte)quantity;
        pdu[5] = byteCount;
        for (var i = 0; i < quantity; i++)
        {
            pdu[6 + (i * 2)] = (byte)(values[i] >> 8);
            pdu[7 + (i * 2)] = (byte)values[i];
        }

        return pdu;
    }
}
