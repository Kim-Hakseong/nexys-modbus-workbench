using Nmw.Core.Protocol;

namespace Nmw.Core.Client;

/// <summary>
/// Modbus 요청 결과. 성공 시 값과 소요 시간, 실패 시 <see cref="ModbusError"/>를 갖는다.
/// 통신 오류는 예외 대신 이 타입으로 반환된다.
/// </summary>
/// <typeparam name="T">응답 값 타입.</typeparam>
public sealed class ModbusResult<T>
{
    private readonly T? _value;

    private ModbusResult(bool isSuccess, T? value, TimeSpan elapsed, ModbusError? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        Elapsed = elapsed;
        Error = error;
    }

    /// <summary>성공 여부.</summary>
    public bool IsSuccess { get; }

    /// <summary>응답 값. 실패 결과에서 접근하면 <see cref="InvalidOperationException"/>.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("실패한 결과에서 Value에 접근했습니다.");

    /// <summary>요청 전송부터 응답 수신까지의 소요 시간 (성공 시).</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>실패 시의 오류. 성공이면 null.</summary>
    public ModbusError? Error { get; }

    /// <summary>성공 결과를 생성한다.</summary>
    /// <param name="value">응답 값.</param>
    /// <param name="elapsed">소요 시간.</param>
    public static ModbusResult<T> Ok(T value, TimeSpan elapsed) => new(true, value, elapsed, null);

    /// <summary>실패 결과를 생성한다.</summary>
    /// <param name="error">오류.</param>
    public static ModbusResult<T> Fail(ModbusError error) => new(false, default, TimeSpan.Zero, error);
}
