using System.IO.Ports;
using Nmw.Core.Data;

namespace Nmw.Core.Workspace;

/// <summary>채널 종류.</summary>
public enum ChannelType
{
    /// <summary>Modbus TCP.</summary>
    Tcp,

    /// <summary>Modbus RTU (시리얼).</summary>
    Rtu,

    /// <summary>RTU over TCP.</summary>
    RtuOverTcp,
}

/// <summary>워크스페이스에 저장되는 채널 접속 설정.</summary>
public sealed record ChannelConfig
{
    /// <summary>채널 식별자.</summary>
    public required string Id { get; init; }

    /// <summary>채널 종류.</summary>
    public ChannelType Type { get; init; } = ChannelType.Tcp;

    /// <summary>TCP 호스트.</summary>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>TCP 포트.</summary>
    public int Port { get; init; } = 502;

    /// <summary>시리얼 포트 이름.</summary>
    public string PortName { get; init; } = "";

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

    /// <summary>프레임간 지연(ms). null=자동.</summary>
    public int? InterFrameDelayMs { get; init; }
}

/// <summary>워크스페이스에 저장되는 폴 정의.</summary>
public sealed record PollConfig
{
    /// <summary>소속 채널 ID.</summary>
    public required string ChannelId { get; init; }

    /// <summary>폴 이름.</summary>
    public string Name { get; init; } = "";

    /// <summary>슬레이브 Unit ID.</summary>
    public byte UnitId { get; init; } = 1;

    /// <summary>function code 숫자 (1~4).</summary>
    public int Function { get; init; } = 3;

    /// <summary>시작 주소(0-base).</summary>
    public ushort StartAddress { get; init; }

    /// <summary>읽을 개수.</summary>
    public ushort Quantity { get; init; } = 10;

    /// <summary>폴 주기(ms).</summary>
    public int ScanRateMs { get; init; } = 500;

    /// <summary>표시 포맷.</summary>
    public RegisterFormat Format { get; init; } = RegisterFormat.U16;

    /// <summary>워드오더.</summary>
    public WordOrder WordOrder { get; init; } = WordOrder.ABCD;

    /// <summary>주소별 별칭 (0-base 주소 → 이름).</summary>
    public Dictionary<ushort, string> Aliases { get; init; } = [];
}

/// <summary>UI 상태 설정.</summary>
public sealed record UiConfig
{
    /// <summary>주소 표기 방식.</summary>
    public AddressBase AddressBase { get; init; } = AddressBase.ZeroBase;
}

/// <summary>워크스페이스 문서 (.nmw JSON 루트).</summary>
public sealed record WorkspaceDocument
{
    /// <summary>현재 스키마 버전.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>스키마 버전.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>채널 목록.</summary>
    public List<ChannelConfig> Channels { get; init; } = [];

    /// <summary>폴 목록.</summary>
    public List<PollConfig> Polls { get; init; } = [];

    /// <summary>UI 상태.</summary>
    public UiConfig Ui { get; init; } = new();
}
