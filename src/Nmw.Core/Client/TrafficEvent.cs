namespace Nmw.Core.Client;

/// <summary>트래픽 방향.</summary>
public enum TrafficDirection
{
    /// <summary>마스터 → 슬레이브 송신.</summary>
    Tx,

    /// <summary>슬레이브 → 마스터 수신.</summary>
    Rx,
}

/// <summary>트래픽 로그 뷰가 구독하는 raw 프레임 이벤트.</summary>
/// <param name="Direction">방향.</param>
/// <param name="Data">전송/수신된 raw 프레임 바이트.</param>
/// <param name="Timestamp">발생 시각.</param>
public sealed record TrafficEvent(TrafficDirection Direction, byte[] Data, DateTimeOffset Timestamp);
