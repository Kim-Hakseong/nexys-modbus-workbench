using Nmw.Core.Data;
using Nmw.Core.Protocol;

namespace Nmw.Core.Polling;

/// <summary>하나의 주기 폴 정의. 워크스페이스에 저장되는 단위.</summary>
public sealed record PollDefinition
{
    /// <summary>폴 식별자 (워크스페이스 내 유일).</summary>
    public required string Id { get; init; }

    /// <summary>표시 이름.</summary>
    public string Name { get; init; } = "";

    /// <summary>슬레이브 Unit ID.</summary>
    public byte UnitId { get; init; } = 1;

    /// <summary>읽기 function (FC01~04).</summary>
    public FunctionCode Function { get; init; } = FunctionCode.ReadHoldingRegisters;

    /// <summary>시작 주소 (0-base 프로토콜 주소).</summary>
    public ushort StartAddress { get; init; }

    /// <summary>읽을 개수.</summary>
    public ushort Quantity { get; init; } = 10;

    /// <summary>폴 주기(ms). 하한 10ms.</summary>
    public int ScanRateMs { get; init; } = 500;

    /// <summary>표시 포맷.</summary>
    public RegisterFormat Format { get; init; } = RegisterFormat.U16;

    /// <summary>워드오더 (32bit 이상 포맷에서 적용).</summary>
    public WordOrder WordOrder { get; init; } = WordOrder.ABCD;

    /// <summary>주소별 별칭 (0-base 주소 → 이름).</summary>
    public Dictionary<ushort, string> Aliases { get; init; } = [];
}
