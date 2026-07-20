using Nmw.Core.Protocol;

namespace Nmw.Core.Polling;

/// <summary>폴 통계. 엔진 내부에서만 갱신되고 스냅샷으로 불변 발행된다.</summary>
/// <param name="TxCount">폴이 발행한 요청 수 (마스터 내부 재시도 제외).</param>
/// <param name="ValidRx">유효 응답 수.</param>
/// <param name="ErrorCount">오류 수.</param>
/// <param name="LastResponseMs">마지막 유효 응답 시간(ms).</param>
/// <param name="LastErrorText">마지막 오류 명칭 (없으면 null).</param>
public sealed record PollStats(
    long TxCount,
    long ValidRx,
    long ErrorCount,
    double LastResponseMs,
    string? LastErrorText);

/// <summary>
/// 폴 1회 수행 결과의 불변 스냅샷. 엔진 → UI로 전달되는 유일한 데이터.
/// FC03/04는 <see cref="Registers"/>, FC01/02는 <see cref="Bits"/>가 채워진다.
/// </summary>
/// <param name="PollId">폴 식별자.</param>
/// <param name="Registers">레지스터 원본 값 (읽기 실패 시 null).</param>
/// <param name="Bits">코일/접점 원본 값 (읽기 실패 시 null).</param>
/// <param name="Stats">누적 통계.</param>
/// <param name="At">스냅샷 시각.</param>
/// <param name="LastError">이번 수행의 오류 (성공 시 null).</param>
public sealed record PollSnapshot(
    string PollId,
    ushort[]? Registers,
    bool[]? Bits,
    PollStats Stats,
    DateTimeOffset At,
    ModbusError? LastError);
