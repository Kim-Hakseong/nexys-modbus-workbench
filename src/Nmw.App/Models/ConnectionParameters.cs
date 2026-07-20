using System.IO.Ports;

namespace Nmw.App.Models;

/// <summary>연결 모드.</summary>
public enum ConnectionMode
{
    /// <summary>Modbus TCP.</summary>
    Tcp,

    /// <summary>Modbus RTU (시리얼).</summary>
    Rtu,

    /// <summary>RTU over TCP 게이트웨이.</summary>
    RtuOverTcp,
}

/// <summary>연결 다이얼로그가 만들어내는 채널 접속 파라미터.</summary>
public sealed record ConnectionParameters
{
    /// <summary>연결 모드.</summary>
    public ConnectionMode Mode { get; init; } = ConnectionMode.Tcp;

    /// <summary>TCP 호스트.</summary>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>TCP 포트.</summary>
    public int Port { get; init; } = 502;

    /// <summary>시리얼 포트 이름.</summary>
    public string SerialPortName { get; init; } = "";

    /// <summary>보레이트.</summary>
    public int BaudRate { get; init; } = 9600;

    /// <summary>패리티.</summary>
    public Parity Parity { get; init; } = Parity.None;

    /// <summary>데이터 비트.</summary>
    public int DataBits { get; init; } = 8;

    /// <summary>정지 비트.</summary>
    public StopBits StopBits { get; init; } = StopBits.One;

    /// <summary>응답 타임아웃(ms).</summary>
    public int TimeoutMs { get; init; } = 1000;

    /// <summary>재시도 횟수.</summary>
    public int Retries { get; init; } = 1;

    /// <summary>프레임간 지연(ms). null이면 자동(3.5 char time).</summary>
    public int? InterFrameDelayMs { get; init; }

    /// <summary>상태바에 표시할 접속 설명.</summary>
    public string Describe() => Mode switch
    {
        ConnectionMode.Tcp => $"TCP {Host}:{Port}",
        ConnectionMode.RtuOverTcp => $"RTU over TCP {Host}:{Port}",
        _ => $"RTU {SerialPortName} {BaudRate}bps",
    };
}
