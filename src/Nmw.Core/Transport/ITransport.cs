namespace Nmw.Core.Transport;

/// <summary>트랜스포트가 사용하는 프레이밍 방식.</summary>
public enum ModbusFramingMode
{
    /// <summary>Modbus TCP MBAP 헤더 프레이밍.</summary>
    Mbap,

    /// <summary>RTU 프레이밍 (UnitId | PDU | CRC).</summary>
    Rtu,
}

/// <summary>
/// Modbus 마스터가 사용하는 바이트 스트림 트랜스포트.
/// 프레임 조립은 <see cref="Client.ModbusMaster"/>가 담당하고,
/// 트랜스포트는 연결 관리와 원시 송수신만 제공한다.
/// </summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>이 트랜스포트의 프레이밍 방식.</summary>
    ModbusFramingMode FramingMode { get; }

    /// <summary>연결 여부.</summary>
    bool IsConnected { get; }

    /// <summary>권장 요청 간 최소 지연 (RTU 3.5 char time). TCP는 Zero.</summary>
    TimeSpan InterFrameDelayHint { get; }

    /// <summary>연결한다. 이미 연결되어 있으면 기존 연결을 닫고 재연결한다.</summary>
    /// <param name="ct">취소 토큰.</param>
    Task ConnectAsync(CancellationToken ct);

    /// <summary>연결을 닫는다.</summary>
    /// <param name="ct">취소 토큰.</param>
    Task CloseAsync(CancellationToken ct);

    /// <summary>데이터를 전송한다.</summary>
    /// <param name="data">전송할 바이트열.</param>
    /// <param name="ct">취소 토큰.</param>
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct);

    /// <summary>수신 데이터를 버퍼에 읽는다. 0 반환은 원격 종료를 의미한다.</summary>
    /// <param name="buffer">수신 버퍼.</param>
    /// <param name="ct">취소 토큰.</param>
    /// <returns>읽은 바이트 수.</returns>
    ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct);

    /// <summary>수신 버퍼에 남아 있는 데이터를 폐기한다 (RTU 요청 전 잔류 바이트 제거용).</summary>
    void DiscardReceiveBuffer();
}
