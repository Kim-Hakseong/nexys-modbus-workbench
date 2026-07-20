namespace Nmw.Core.Client;

/// <summary>ModbusMaster 동작 옵션.</summary>
public sealed record ModbusMasterOptions
{
    /// <summary>응답 타임아웃(ms). 기본 1000.</summary>
    public int TimeoutMs { get; init; } = 1000;

    /// <summary>Timeout/CRC 오류에 한한 재시도 횟수. 기본 1.</summary>
    public int Retries { get; init; } = 1;

    /// <summary>
    /// 요청 간 최소 지연(ms). null이면 트랜스포트의 권장값(3.5 char time) 사용.
    /// RTU 프레이밍에서만 적용된다.
    /// </summary>
    public int? InterFrameDelayMs { get; init; }
}
