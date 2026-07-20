using System.Globalization;

namespace Nmw.App.Models;

/// <summary>쓰기 다이얼로그 입력 파서 (10진/0x 16진, 콤마·공백 구분 목록).</summary>
public static class WriteInputParser
{
    private static readonly char[] Separators = [',', ' ', ';', '\t'];

    /// <summary>레지스터 값 하나를 파싱한다 (10진 또는 0x 16진).</summary>
    /// <param name="text">입력.</param>
    /// <param name="value">파싱된 값.</param>
    public static bool TryParseRegister(string text, out ushort value)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ushort.TryParse(
                text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        return ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>레지스터 값 목록을 파싱한다 (1개 이상).</summary>
    /// <param name="text">입력 (예: "10, 0x0102").</param>
    /// <param name="values">파싱된 값들.</param>
    public static bool TryParseRegisterList(string text, out ushort[] values)
    {
        var tokens = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        values = new ushort[tokens.Length];
        if (tokens.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < tokens.Length; i++)
        {
            if (!TryParseRegister(tokens[i], out values[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>비트 값 하나를 파싱한다 (1/0/on/off/true/false).</summary>
    /// <param name="text">입력.</param>
    /// <param name="on">파싱된 값.</param>
    public static bool TryParseBit(string text, out bool on)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "1" or "on" or "true":
                on = true;
                return true;
            case "0" or "off" or "false":
                on = false;
                return true;
            default:
                on = false;
                return false;
        }
    }

    /// <summary>비트 값 목록을 파싱한다 (1개 이상).</summary>
    /// <param name="text">입력 (예: "1,0,1,1").</param>
    /// <param name="values">파싱된 값들.</param>
    public static bool TryParseBitList(string text, out bool[] values)
    {
        var tokens = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        values = new bool[tokens.Length];
        if (tokens.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < tokens.Length; i++)
        {
            if (!TryParseBit(tokens[i], out values[i]))
            {
                return false;
            }
        }

        return true;
    }
}
