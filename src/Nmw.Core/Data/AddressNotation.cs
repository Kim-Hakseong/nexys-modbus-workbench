using Nmw.Core.Protocol;

namespace Nmw.Core.Data;

/// <summary>주소 표기 방식.</summary>
public enum AddressBase
{
    /// <summary>프로토콜 주소 (0-base).</summary>
    ZeroBase,

    /// <summary>PLC 표기 (1-base, 40001 스타일).</summary>
    OneBase,
}

/// <summary>Modbus 데이터 영역.</summary>
public enum AddressArea
{
    /// <summary>코일 (0xxxx).</summary>
    Coil,

    /// <summary>접점 (1xxxx).</summary>
    DiscreteInput,

    /// <summary>입력 레지스터 (3xxxx).</summary>
    InputRegister,

    /// <summary>홀딩 레지스터 (4xxxx).</summary>
    HoldingRegister,
}

/// <summary>
/// 주소 표기 변환. 내부 저장은 항상 프로토콜 주소(0-base)이고 표시/입력 시점에만 변환한다.
/// PLC 표기 = 영역 프리픽스 + (주소+1). 5자리/6자리(400001 스타일) 입력 모두 허용.
/// </summary>
public static class AddressNotation
{
    /// <summary>function code가 다루는 데이터 영역을 반환한다.</summary>
    /// <param name="function">function code.</param>
    public static AddressArea AreaOf(FunctionCode function) => function switch
    {
        FunctionCode.ReadCoils or FunctionCode.WriteSingleCoil or FunctionCode.WriteMultipleCoils =>
            AddressArea.Coil,
        FunctionCode.ReadDiscreteInputs => AddressArea.DiscreteInput,
        FunctionCode.ReadInputRegisters => AddressArea.InputRegister,
        _ => AddressArea.HoldingRegister,
    };

    private static int PrefixOf(AddressArea area) => area switch
    {
        AddressArea.Coil => 0,
        AddressArea.DiscreteInput => 1,
        AddressArea.InputRegister => 3,
        _ => 4,
    };

    /// <summary>프로토콜 주소를 표기 문자열로 변환한다.</summary>
    /// <param name="protocolAddress">0-base 프로토콜 주소.</param>
    /// <param name="area">데이터 영역.</param>
    /// <param name="addressBase">표기 방식.</param>
    /// <returns>표시 문자열 (예: OneBase 홀딩 주소 0 → "40001").</returns>
    public static string Format(ushort protocolAddress, AddressArea area, AddressBase addressBase)
    {
        if (addressBase == AddressBase.ZeroBase)
        {
            return protocolAddress.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var offset = protocolAddress + 1;
        var prefix = PrefixOf(area);
        return offset <= 9999 ? $"{prefix}{offset:D4}" : $"{prefix}{offset:D5}";
    }

    /// <summary>표기 문자열을 프로토콜 주소로 파싱한다.</summary>
    /// <param name="text">입력 문자열.</param>
    /// <param name="area">데이터 영역 (OneBase 프리픽스 검증에 사용).</param>
    /// <param name="addressBase">표기 방식.</param>
    /// <param name="protocolAddress">파싱된 0-base 프로토콜 주소.</param>
    /// <returns>파싱 성공 여부.</returns>
    public static bool TryParse(
        string text, AddressArea area, AddressBase addressBase, out ushort protocolAddress)
    {
        protocolAddress = 0;
        text = text.Trim();
        if (text.Length == 0 || !text.All(char.IsAsciiDigit))
        {
            return false;
        }

        if (addressBase == AddressBase.ZeroBase)
        {
            return ushort.TryParse(text, out protocolAddress);
        }

        if (text.Length >= 5)
        {
            // 프리픽스 표기 (5자리: X####, 6자리: X#####)
            var prefix = text[0] - '0';
            if (prefix != PrefixOf(area))
            {
                return false;
            }

            if (!int.TryParse(text[1..], out var offset) || offset < 1 || offset > 65536)
            {
                return false;
            }

            protocolAddress = (ushort)(offset - 1);
            return true;
        }

        // 프리픽스 없는 1-base 입력
        if (!int.TryParse(text, out var plain) || plain < 1 || plain > 65536)
        {
            return false;
        }

        protocolAddress = (ushort)(plain - 1);
        return true;
    }
}
