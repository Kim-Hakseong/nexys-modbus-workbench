using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Nmw.App.ViewModels;
using Nmw.Core.Data;

namespace Nmw.App.Views;

/// <summary>메인 윈도우.</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>메인 윈도우를 만든다.</summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    private SimulatorWindow? _simulatorWindow;
    private HelpWindow? _helpWindow;

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ShowHelp(); // 실행 시 간단 사용설명서 자동 표시
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _helpWindow?.Close();
        _simulatorWindow?.Close();
        base.OnClosed(e);
    }

    private void OnHelpClick(object? sender, RoutedEventArgs e) => ShowHelp();

    private void ShowHelp()
    {
        if (_helpWindow is { IsVisible: true })
        {
            _helpWindow.Activate();
            return;
        }

        _helpWindow = new HelpWindow();
        _helpWindow.Closed += (_, _) => _helpWindow = null;
        _helpWindow.Show(this);
    }

    private void OnSimulatorClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (_simulatorWindow is { IsVisible: true })
        {
            _simulatorWindow.Activate();
            return;
        }

        _simulatorWindow = new SimulatorWindow { DataContext = viewModel.Simulator };
        _simulatorWindow.Closed += (_, _) => _simulatorWindow = null;
        _simulatorWindow.Show(this);
    }

    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var dialog = new ConnectionDialog();
        var parameters = await dialog.ShowDialog<Models.ConnectionParameters?>(this);
        if (parameters is not null)
        {
            await viewModel.ApplyConnectionAsync(parameters);
        }
    }

    private async void OnWriteClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { Master: { } master } viewModel)
        {
            return;
        }

        var dialog = new WriteDialog(master, viewModel.CurrentAddressBase, viewModel.CurrentUnitId);
        await dialog.ShowDialog(this);
    }

    private async void OnSaveLogClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "트래픽 로그 저장",
            SuggestedFileName = $"nmw-traffic-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            FileTypeChoices = [new FilePickerFileType("텍스트 파일") { Patterns = ["*.txt"] }],
        });
        if (file is null)
        {
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        await viewModel.TrafficLog.SaveAsync(stream);
    }

    private async void OnSaveWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "워크스페이스 저장",
            SuggestedFileName = "workspace.nmw",
            FileTypeChoices = [new FilePickerFileType("Nexys Modbus 워크스페이스") { Patterns = ["*.nmw"] }],
        });
        if (file is null)
        {
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync(viewModel.SaveWorkspaceJson());
    }

    private async void OnOpenWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "워크스페이스 열기",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Nexys Modbus 워크스페이스") { Patterns = ["*.nmw"] }],
        });
        if (files.Count == 0)
        {
            return;
        }

        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = await reader.ReadToEndAsync();
        await viewModel.LoadWorkspaceJsonAsync(json);
    }

    private async void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is not { Master: { } master } viewModel ||
            viewModel.SelectedPoll is not { } poll ||
            sender is not DataGrid grid ||
            grid.SelectedItem is not RegisterRowViewModel row)
        {
            return;
        }

        // 비트 폴이면 FC05, 레지스터 폴이면 FC06으로 주소·현재값 프리필 (F-15)
        var dialog = new WriteDialog(master, viewModel.CurrentAddressBase, poll.CurrentUnitId);
        if (poll.ActivePollIsBits)
        {
            var address = AddressNotation.Format(
                row.ProtocolAddress, AddressArea.Coil, viewModel.CurrentAddressBase);
            dialog.Prefill(0, address, row.RawBit ? "1" : "0");
        }
        else
        {
            var address = AddressNotation.Format(
                row.ProtocolAddress, AddressArea.HoldingRegister, viewModel.CurrentAddressBase);
            dialog.Prefill(1, address, row.RawRegister.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }

        await dialog.ShowDialog(this);
    }
}
