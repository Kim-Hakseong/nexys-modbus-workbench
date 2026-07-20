using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Nmw.App.Models;
using Nmw.Core.Client;
using Nmw.Core.Data;
using Nmw.Core.Protocol;

namespace Nmw.App.Views;

/// <summary>FC05/06/15/16 쓰기 다이얼로그. 결과(성공/exception 명칭)를 표시한다.</summary>
public sealed partial class WriteDialog : Window
{
    private static readonly string[] Hints =
    [
        "값: 1(ON) 또는 0(OFF)",
        "값: 0~65535 또는 0xFFFF 형식",
        "값 목록: 1,0,1,1 (콤마/공백 구분)",
        "값 목록: 10, 0x0102 (콤마/공백 구분)",
    ];

    private static readonly string[] ValueWatermarks =
    [
        "예: 1 (ON) / 0 (OFF)",
        "예: 123 또는 0xBEEF",
        "예: 1,0,1,1",
        "예: 10, 0x0102, 3",
    ];

    private readonly ModbusMaster? _master;
    private readonly AddressBase _addressBase;

    /// <summary>디자이너/프레임워크용 기본 생성자.</summary>
    public WriteDialog()
        : this(null, AddressBase.ZeroBase, 1)
    {
    }

    /// <summary>마스터와 주소 표기, 기본 Unit ID로 다이얼로그를 만든다.</summary>
    /// <param name="master">쓰기 요청을 보낼 마스터 (null이면 전송 불가).</param>
    /// <param name="addressBase">주소 입력 표기 방식.</param>
    /// <param name="unitId">기본 Unit ID.</param>
    public WriteDialog(ModbusMaster? master, AddressBase addressBase, byte unitId)
    {
        InitializeComponent();
        _master = master;
        _addressBase = addressBase;
        UnitIdBox.Text = unitId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        UpdateHint();
    }

    /// <summary>그리드 더블클릭 프리필: function/주소/값을 채운다.</summary>
    /// <param name="functionIndex">0=FC05, 1=FC06, 2=FC15, 3=FC16.</param>
    /// <param name="addressText">주소 문자열 (현재 표기 기준).</param>
    /// <param name="valueText">값 문자열.</param>
    public void Prefill(int functionIndex, string addressText, string valueText)
    {
        FunctionBox.SelectedIndex = Math.Clamp(functionIndex, 0, 3);
        AddressBox.Text = addressText;
        ValueBox.Text = valueText;
        UpdateHint();
    }

    private void OnFunctionChanged(object? sender, SelectionChangedEventArgs e) => UpdateHint();

    private void UpdateHint()
    {
        // InitializeComponent 이전 이벤트 호출 대비
        if (HintText is null)
        {
            return;
        }

        var index = Math.Clamp(FunctionBox.SelectedIndex, 0, Hints.Length - 1);
        HintText.Text = Hints[index];
        ValueBox.Watermark = ValueWatermarks[index];
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void ShowResult(string message, bool isError)
    {
        ResultText.Text = message;
        ResultText.Foreground = isError ? Brushes.OrangeRed : Brushes.Green;
    }

    private async void OnSendClick(object? sender, RoutedEventArgs e)
    {
        if (_master is null)
        {
            ShowResult(Strings.NotConnectedError, isError: true);
            return;
        }

        if (!byte.TryParse(UnitIdBox.Text, out var unitId))
        {
            ShowResult($"{Strings.InvalidInput}: Unit ID", isError: true);
            return;
        }

        var functionIndex = FunctionBox.SelectedIndex;
        var area = functionIndex is 0 or 2 ? AddressArea.Coil : AddressArea.HoldingRegister;
        if (!AddressNotation.TryParse(AddressBox.Text ?? "", area, _addressBase, out var address))
        {
            ShowResult($"{Strings.InvalidInput}: 주소", isError: true);
            return;
        }

        var valueText = ValueBox.Text ?? "";
        SendButton.IsEnabled = false;
        try
        {
            ModbusResult<Unit>? result = null;
            switch (functionIndex)
            {
                case 0:
                    if (!WriteInputParser.TryParseBit(valueText, out var on))
                    {
                        ShowResult($"{Strings.InvalidInput}: 값 (1/0)", isError: true);
                        return;
                    }

                    result = await _master.WriteSingleCoilAsync(unitId, address, on);
                    break;

                case 2:
                    if (!WriteInputParser.TryParseBitList(valueText, out var bits))
                    {
                        ShowResult($"{Strings.InvalidInput}: 값 목록 (1,0,...)", isError: true);
                        return;
                    }

                    result = await _master.WriteMultipleCoilsAsync(unitId, address, bits);
                    break;

                case 3:
                    if (!WriteInputParser.TryParseRegisterList(valueText, out var registers))
                    {
                        ShowResult($"{Strings.InvalidInput}: 값 목록", isError: true);
                        return;
                    }

                    result = await _master.WriteMultipleRegistersAsync(unitId, address, registers);
                    break;

                default:
                    if (!WriteInputParser.TryParseRegister(valueText, out var registerValue))
                    {
                        ShowResult($"{Strings.InvalidInput}: 값", isError: true);
                        return;
                    }

                    result = await _master.WriteSingleRegisterAsync(unitId, address, registerValue);
                    break;
            }

            if (result.IsSuccess)
            {
                ShowResult($"성공 ({result.Elapsed.TotalMilliseconds:F0} ms)", isError: false);
            }
            else
            {
                ShowResult($"실패: {result.Error!.Text}", isError: true);
            }
        }
        catch (ArgumentOutOfRangeException ex)
        {
            ShowResult($"{Strings.InvalidInput}: {ex.Message}", isError: true);
        }
        finally
        {
            SendButton.IsEnabled = true;
        }
    }
}
