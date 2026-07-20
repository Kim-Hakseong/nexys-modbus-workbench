namespace Nmw.Core.Protocol;

/// <summary>Modbus 통신/파싱 오류의 종류.</summary>
public enum ModbusErrorKind
{
    /// <summary>응답 타임아웃.</summary>
    Timeout,

    /// <summary>RTU CRC 불일치.</summary>
    CrcMismatch,

    /// <summary>슬레이브가 반환한 Modbus exception 응답.</summary>
    Exception,

    /// <summary>트랜스포트가 닫혀 요청을 수행할 수 없음.</summary>
    TransportClosed,

    /// <summary>프로토콜 규격에 맞지 않는 응답.</summary>
    InvalidResponse,
}

/// <summary>
/// Modbus 통신 오류. 예외를 던지는 대신 결과 타입에 실어 폴링 루프가 통계로 축적한다.
/// </summary>
public sealed record ModbusError
{
    private ModbusError(ModbusErrorKind kind, ModbusExceptionCode? exceptionCode, string? detail)
    {
        Kind = kind;
        ExceptionCode = exceptionCode;
        Detail = detail;
    }

    /// <summary>오류 종류.</summary>
    public ModbusErrorKind Kind { get; }

    /// <summary><see cref="ModbusErrorKind.Exception"/>일 때의 exception code.</summary>
    public ModbusExceptionCode? ExceptionCode { get; }

    /// <summary>부가 설명(있으면).</summary>
    public string? Detail { get; }

    /// <summary>UI에 표시할 오류 문자열.</summary>
    public string Text => Kind switch
    {
        ModbusErrorKind.Timeout => "Timeout",
        ModbusErrorKind.CrcMismatch => "CRC Mismatch",
        ModbusErrorKind.Exception when ExceptionCode is { } code => code.GetDisplayName(),
        ModbusErrorKind.Exception => "Exception",
        ModbusErrorKind.TransportClosed => "Transport Closed",
        ModbusErrorKind.InvalidResponse => Detail is null ? "Invalid Response" : $"Invalid Response: {Detail}",
        _ => Kind.ToString(),
    };

    /// <summary>타임아웃 오류를 생성한다.</summary>
    public static ModbusError Timeout() => new(ModbusErrorKind.Timeout, null, null);

    /// <summary>CRC 불일치 오류를 생성한다.</summary>
    public static ModbusError CrcMismatch() => new(ModbusErrorKind.CrcMismatch, null, null);

    /// <summary>Modbus exception 응답 오류를 생성한다.</summary>
    /// <param name="code">슬레이브가 반환한 exception code.</param>
    public static ModbusError FromException(ModbusExceptionCode code) =>
        new(ModbusErrorKind.Exception, code, null);

    /// <summary>트랜스포트 닫힘 오류를 생성한다.</summary>
    /// <param name="detail">부가 설명(있으면).</param>
    public static ModbusError TransportClosed(string? detail = null) =>
        new(ModbusErrorKind.TransportClosed, null, detail);

    /// <summary>규격 위반 응답 오류를 생성한다.</summary>
    /// <param name="detail">위반 내용 설명.</param>
    public static ModbusError InvalidResponse(string? detail = null) =>
        new(ModbusErrorKind.InvalidResponse, null, detail);
}
