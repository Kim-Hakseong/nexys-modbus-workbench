using System.IO.Ports;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Nmw.App.Models;

namespace Nmw.App.Views;

/// <summary>채널 연결 다이얼로그. 확인 시 <see cref="ConnectionParameters"/>를 반환한다.</summary>
public sealed partial class ConnectionDialog : Window
{
    /// <summary>다이얼로그를 만든다.</summary>
    public ConnectionDialog()
    {
        InitializeComponent();
        SerialPortBox.ItemsSource = SerialPort.GetPortNames();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var mode = RtuRadio.IsChecked == true
            ? ConnectionMode.Rtu
            : RtuOverTcpRadio.IsChecked == true
                ? ConnectionMode.RtuOverTcp
                : ConnectionMode.Tcp;

        if (!int.TryParse(TimeoutBox.Text, out var timeoutMs) || timeoutMs < 1 ||
            !int.TryParse(RetriesBox.Text, out var retries) || retries < 0)
        {
            ShowError("타임아웃/재시도 값을 확인하세요");
            return;
        }

        int? interFrameDelayMs = null;
        if (!string.IsNullOrWhiteSpace(InterFrameBox.Text))
        {
            if (!int.TryParse(InterFrameBox.Text, out var delay) || delay < 0)
            {
                ShowError("프레임간 지연 값을 확인하세요");
                return;
            }

            interFrameDelayMs = delay;
        }

        if (mode is ConnectionMode.Tcp or ConnectionMode.RtuOverTcp)
        {
            var host = HostBox.Text?.Trim() ?? "";
            if (host.Length == 0 || !int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535)
            {
                ShowError("호스트/포트를 확인하세요");
                return;
            }

            Close(new ConnectionParameters
            {
                Mode = mode,
                Host = host,
                Port = port,
                TimeoutMs = timeoutMs,
                Retries = retries,
                InterFrameDelayMs = interFrameDelayMs,
            });
            return;
        }

        var portName = SerialPortBox.Text?.Trim() ?? "";
        if (portName.Length == 0)
        {
            ShowError("시리얼 포트를 선택하세요");
            return;
        }

        var baud = int.Parse(GetComboText(BaudBox, "9600"), System.Globalization.CultureInfo.InvariantCulture);
        var parity = GetComboText(ParityBox, "None") switch
        {
            "Even" => Parity.Even,
            "Odd" => Parity.Odd,
            _ => Parity.None,
        };
        var dataBits = GetComboText(DataBitsBox, "8") == "7" ? 7 : 8;
        var stopBits = GetComboText(StopBitsBox, "1") == "2" ? StopBits.Two : StopBits.One;

        Close(new ConnectionParameters
        {
            Mode = ConnectionMode.Rtu,
            SerialPortName = portName,
            BaudRate = baud,
            Parity = parity,
            DataBits = dataBits,
            StopBits = stopBits,
            TimeoutMs = timeoutMs,
            Retries = retries,
            InterFrameDelayMs = interFrameDelayMs,
        });
    }

    private static string GetComboText(ComboBox comboBox, string fallback) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? fallback;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
