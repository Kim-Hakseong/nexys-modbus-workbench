using Nmw.Core.Data;
using Nmw.Core.Protocol;

namespace Nmw.App.ViewModels;

/// <summary>폴 설정 콤보 항목과 인덱스 매핑 (뷰모델 공용).</summary>
public static class PollOptions
{
    /// <summary>콤보 인덱스 → function code.</summary>
    public static readonly FunctionCode[] FunctionByIndex =
    [
        FunctionCode.ReadCoils,
        FunctionCode.ReadDiscreteInputs,
        FunctionCode.ReadHoldingRegisters,
        FunctionCode.ReadInputRegisters,
    ];

    /// <summary>콤보 인덱스 → 포맷.</summary>
    public static readonly RegisterFormat[] FormatByIndex =
    [
        RegisterFormat.U16, RegisterFormat.S16, RegisterFormat.Hex16, RegisterFormat.Bin16,
        RegisterFormat.U32, RegisterFormat.S32, RegisterFormat.Float32,
        RegisterFormat.S64, RegisterFormat.Double64, RegisterFormat.Ascii,
    ];

    /// <summary>콤보 인덱스 → 워드오더.</summary>
    public static readonly WordOrder[] WordOrderByIndex =
        [WordOrder.ABCD, WordOrder.CDAB, WordOrder.BADC, WordOrder.DCBA];

    /// <summary>function 콤보 표시 문자열.</summary>
    public static IReadOnlyList<string> FunctionOptions { get; } =
        ["FC01 코일", "FC02 접점", "FC03 홀딩 레지스터", "FC04 입력 레지스터"];

    /// <summary>포맷 콤보 표시 문자열.</summary>
    public static IReadOnlyList<string> FormatOptions { get; } =
        ["U16", "S16", "Hex16", "Bin16", "U32", "S32", "Float32", "S64", "Double64", "ASCII"];

    /// <summary>워드오더 콤보 표시 문자열.</summary>
    public static IReadOnlyList<string> WordOrderOptions { get; } =
        ["ABCD (Big-endian)", "CDAB (Word swap)", "BADC (Byte swap)", "DCBA (Little-endian)"];

    /// <summary>function code의 콤보 인덱스 (미지원이면 FC03).</summary>
    /// <param name="function">function code.</param>
    public static int IndexOfFunction(FunctionCode function)
    {
        var index = Array.IndexOf(FunctionByIndex, function);
        return index >= 0 ? index : 2;
    }

    /// <summary>포맷의 콤보 인덱스.</summary>
    /// <param name="format">포맷.</param>
    public static int IndexOfFormat(RegisterFormat format) =>
        Math.Max(0, Array.IndexOf(FormatByIndex, format));

    /// <summary>워드오더의 콤보 인덱스.</summary>
    /// <param name="order">워드오더.</param>
    public static int IndexOfWordOrder(WordOrder order) =>
        Math.Max(0, Array.IndexOf(WordOrderByIndex, order));
}
