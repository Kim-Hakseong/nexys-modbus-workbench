namespace Nmw.Core.Protocol;

/// <summary>
/// Modbus 응답 PDU 파서. 요청 컨텍스트(function, 개수 등)와 대조해 검증하며,
/// 규격 위반은 <see cref="ModbusError"/>로 반환한다 (예외를 던지지 않음).
/// </summary>
public static class PduParser
{
    /// <summary>FC01/02 읽기 응답 PDU를 파싱해 비트 배열을 반환한다.</summary>
    /// <param name="pdu">응답 PDU.</param>
    /// <param name="requestFunction">요청에 사용한 function code (FC01/02).</param>
    /// <param name="quantity">요청한 비트 개수.</param>
    /// <returns>요청 개수만큼의 비트 배열 또는 오류.</returns>
    public static PduParseResult<bool[]> ParseReadBitsResponse(
        ReadOnlySpan<byte> pdu, FunctionCode requestFunction, ushort quantity)
    {
        if (CheckHeader(pdu, requestFunction) is { } error)
        {
            return PduParseResult<bool[]>.Fail(error);
        }

        var expectedByteCount = (quantity + 7) / 8;
        var byteCount = pdu[1];
        if (byteCount != expectedByteCount || pdu.Length != 2 + byteCount)
        {
            return PduParseResult<bool[]>.Fail(ModbusError.InvalidResponse(
                $"byte count 불일치 (기대 {expectedByteCount}, 수신 {byteCount}, PDU {pdu.Length}바이트)"));
        }

        var bits = new bool[quantity];
        for (var i = 0; i < quantity; i++)
        {
            bits[i] = (pdu[2 + (i / 8)] & (1 << (i % 8))) != 0;
        }

        return PduParseResult<bool[]>.Ok(bits);
    }

    /// <summary>FC03/04 읽기 응답 PDU를 파싱해 레지스터 배열을 반환한다.</summary>
    /// <param name="pdu">응답 PDU.</param>
    /// <param name="requestFunction">요청에 사용한 function code (FC03/04).</param>
    /// <param name="quantity">요청한 레지스터 개수.</param>
    /// <returns>요청 개수만큼의 레지스터 배열 또는 오류.</returns>
    public static PduParseResult<ushort[]> ParseReadRegistersResponse(
        ReadOnlySpan<byte> pdu, FunctionCode requestFunction, ushort quantity)
    {
        if (CheckHeader(pdu, requestFunction) is { } error)
        {
            return PduParseResult<ushort[]>.Fail(error);
        }

        var expectedByteCount = quantity * 2;
        var byteCount = pdu[1];
        if (byteCount != expectedByteCount || pdu.Length != 2 + byteCount)
        {
            return PduParseResult<ushort[]>.Fail(ModbusError.InvalidResponse(
                $"byte count 불일치 (기대 {expectedByteCount}, 수신 {byteCount}, PDU {pdu.Length}바이트)"));
        }

        var registers = new ushort[quantity];
        for (var i = 0; i < quantity; i++)
        {
            registers[i] = (ushort)((pdu[2 + (i * 2)] << 8) | pdu[3 + (i * 2)]);
        }

        return PduParseResult<ushort[]>.Ok(registers);
    }

    /// <summary>FC05/06 쓰기 응답(요청 에코)을 검증한다.</summary>
    /// <param name="pdu">응답 PDU.</param>
    /// <param name="requestPdu">전송했던 요청 PDU.</param>
    /// <returns>에코 일치 시 성공, 아니면 오류.</returns>
    public static PduParseResult<Unit> ParseWriteSingleResponse(
        ReadOnlySpan<byte> pdu, ReadOnlySpan<byte> requestPdu)
    {
        if (requestPdu.Length != 5)
        {
            return PduParseResult<Unit>.Fail(ModbusError.InvalidResponse("요청 PDU가 FC05/06 형식(5바이트)이 아닙니다."));
        }

        if (CheckHeader(pdu, (FunctionCode)requestPdu[0]) is { } error)
        {
            return PduParseResult<Unit>.Fail(error);
        }

        if (!pdu.SequenceEqual(requestPdu))
        {
            return PduParseResult<Unit>.Fail(ModbusError.InvalidResponse("쓰기 응답이 요청 에코와 일치하지 않습니다."));
        }

        return PduParseResult<Unit>.Ok(Unit.Value);
    }

    /// <summary>FC15/16 쓰기 응답(FC, addr, qty)을 검증한다.</summary>
    /// <param name="pdu">응답 PDU.</param>
    /// <param name="requestFunction">요청에 사용한 function code (FC15/16).</param>
    /// <param name="startAddress">요청한 시작 주소.</param>
    /// <param name="quantity">요청한 쓰기 개수.</param>
    /// <returns>응답 필드 일치 시 성공, 아니면 오류.</returns>
    public static PduParseResult<Unit> ParseWriteMultipleResponse(
        ReadOnlySpan<byte> pdu, FunctionCode requestFunction, ushort startAddress, ushort quantity)
    {
        if (CheckHeader(pdu, requestFunction) is { } error)
        {
            return PduParseResult<Unit>.Fail(error);
        }

        if (pdu.Length != 5)
        {
            return PduParseResult<Unit>.Fail(ModbusError.InvalidResponse(
                $"FC15/16 응답 길이 오류 (기대 5, 수신 {pdu.Length}바이트)"));
        }

        var responseAddress = (ushort)((pdu[1] << 8) | pdu[2]);
        var responseQuantity = (ushort)((pdu[3] << 8) | pdu[4]);
        if (responseAddress != startAddress || responseQuantity != quantity)
        {
            return PduParseResult<Unit>.Fail(ModbusError.InvalidResponse(
                "쓰기 응답의 주소/개수가 요청과 일치하지 않습니다."));
        }

        return PduParseResult<Unit>.Ok(Unit.Value);
    }

    /// <summary>
    /// 공통 헤더 검증: 빈 PDU, exception 응답(FC MSB 0x80), function code 불일치를 확인한다.
    /// 문제가 없으면 null을 반환한다.
    /// </summary>
    private static ModbusError? CheckHeader(ReadOnlySpan<byte> pdu, FunctionCode requestFunction)
    {
        if (pdu.Length < 2)
        {
            return ModbusError.InvalidResponse($"PDU가 너무 짧습니다 ({pdu.Length}바이트)");
        }

        if ((pdu[0] & 0x80) != 0)
        {
            if ((pdu[0] & 0x7F) != (byte)requestFunction)
            {
                return ModbusError.InvalidResponse(
                    $"exception 응답의 function code 불일치 (기대 0x{(byte)requestFunction:X2}, 수신 0x{pdu[0]:X2})");
            }

            return ModbusError.FromException((ModbusExceptionCode)pdu[1]);
        }

        if (pdu[0] != (byte)requestFunction)
        {
            return ModbusError.InvalidResponse(
                $"function code 불일치 (기대 0x{(byte)requestFunction:X2}, 수신 0x{pdu[0]:X2})");
        }

        return null;
    }
}
