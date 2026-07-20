namespace Nmw.Core.Protocol;

/// <summary>값이 없는 성공 결과를 표현하는 단위 타입.</summary>
public readonly struct Unit
{
    /// <summary>단일 인스턴스.</summary>
    public static readonly Unit Value = default;
}

/// <summary>PDU 파싱 결과. 성공 시 <see cref="Value"/>, 실패 시 <see cref="Error"/>를 갖는다.</summary>
/// <typeparam name="T">파싱된 값의 타입.</typeparam>
public sealed class PduParseResult<T>
{
    private readonly T? _value;

    private PduParseResult(bool isSuccess, T? value, ModbusError? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    /// <summary>파싱 성공 여부.</summary>
    public bool IsSuccess { get; }

    /// <summary>파싱된 값. 실패 결과에서 접근하면 <see cref="InvalidOperationException"/>.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("실패한 파싱 결과에서 Value에 접근했습니다.");

    /// <summary>실패 시의 오류. 성공이면 null.</summary>
    public ModbusError? Error { get; }

    /// <summary>성공 결과를 생성한다.</summary>
    /// <param name="value">파싱된 값.</param>
    public static PduParseResult<T> Ok(T value) => new(true, value, null);

    /// <summary>실패 결과를 생성한다.</summary>
    /// <param name="error">오류.</param>
    public static PduParseResult<T> Fail(ModbusError error) => new(false, default, error);
}
