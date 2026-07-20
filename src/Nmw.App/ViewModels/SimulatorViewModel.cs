using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nmw.App.Models;
using Nmw.Core.Simulator;

namespace Nmw.App.ViewModels;

/// <summary>시뮬레이터 레지스터 행 (홀딩/입력 레지스터, 값 편집 가능).</summary>
public sealed partial class SimRegisterRowViewModel : ObservableObject
{
    private readonly Action<int, ushort> _apply;
    private bool _updating;

    [ObservableProperty]
    private string valueText = "0";

    /// <summary>주소와 값 반영 콜백으로 행을 만든다.</summary>
    /// <param name="address">주소 (0-base).</param>
    /// <param name="apply">값 변경 시 시뮬레이터에 반영하는 콜백.</param>
    public SimRegisterRowViewModel(int address, Action<int, ushort> apply)
    {
        Address = address;
        _apply = apply;
    }

    /// <summary>주소 (0-base).</summary>
    public int Address { get; }

    /// <summary>시뮬레이터 현재 값으로 표시를 갱신한다 (콜백 재호출 없음).</summary>
    /// <param name="value">시뮬레이터의 현재 값.</param>
    public void UpdateFromSimulator(ushort value)
    {
        var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (ValueText == text)
        {
            return;
        }

        _updating = true;
        try
        {
            ValueText = text;
        }
        finally
        {
            _updating = false;
        }
    }

    partial void OnValueTextChanged(string value)
    {
        if (!_updating && WriteInputParser.TryParseRegister(value, out var parsed))
        {
            _apply(Address, parsed);
        }
    }
}

/// <summary>시뮬레이터 비트 행 (코일/접점, 체크박스 편집 가능).</summary>
public sealed partial class SimBitRowViewModel : ObservableObject
{
    private readonly Action<int, bool> _apply;
    private bool _updating;

    [ObservableProperty]
    private bool value;

    /// <summary>주소와 값 반영 콜백으로 행을 만든다.</summary>
    /// <param name="address">주소 (0-base).</param>
    /// <param name="apply">값 변경 시 시뮬레이터에 반영하는 콜백.</param>
    public SimBitRowViewModel(int address, Action<int, bool> apply)
    {
        Address = address;
        _apply = apply;
    }

    /// <summary>주소 (0-base).</summary>
    public int Address { get; }

    /// <summary>시뮬레이터 현재 값으로 표시를 갱신한다 (콜백 재호출 없음).</summary>
    /// <param name="newValue">시뮬레이터의 현재 값.</param>
    public void UpdateFromSimulator(bool newValue)
    {
        if (Value == newValue)
        {
            return;
        }

        _updating = true;
        try
        {
            Value = newValue;
        }
        finally
        {
            _updating = false;
        }
    }

    partial void OnValueChanged(bool value)
    {
        if (!_updating)
        {
            _apply(Address, value);
        }
    }
}

/// <summary>
/// 내장 슬레이브 시뮬레이터 뷰모델. 물리 장비 없이 마스터 기능을 테스트하기 위해
/// 127.0.0.1에 Modbus TCP 슬레이브를 띄우고, 4개 데이터 영역의 값을 편집/관찰한다.
/// </summary>
public sealed partial class SimulatorViewModel : ObservableObject, IAsyncDisposable
{
    private ModbusTcpSimulator? _simulator;
    private DispatcherTimer? _timer;
    private int _displayCount = 20;

    [ObservableProperty]
    private string portText = "1502";

    [ObservableProperty]
    private string displayCountText = "20";

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private string statusText = "정지됨";

    [ObservableProperty]
    private bool autoChange;

    [ObservableProperty]
    private string message = "";

    /// <summary>홀딩 레지스터 행 (편집 가능, FC03/06/16 대상).</summary>
    public ObservableCollection<SimRegisterRowViewModel> HoldingRows { get; } = [];

    /// <summary>입력 레지스터 행 (편집 가능, FC04 대상).</summary>
    public ObservableCollection<SimRegisterRowViewModel> InputRows { get; } = [];

    /// <summary>코일 행 (편집 가능, FC01/05/15 대상).</summary>
    public ObservableCollection<SimBitRowViewModel> CoilRows { get; } = [];

    /// <summary>접점 행 (편집 가능, FC02 대상).</summary>
    public ObservableCollection<SimBitRowViewModel> DiscreteRows { get; } = [];

    /// <summary>실행 중인 시뮬레이터의 실제 포트 (테스트/표시용).</summary>
    public int ActualPort => _simulator?.Port ?? 0;

    /// <summary>시뮬레이터를 시작/정지한다.</summary>
    [RelayCommand]
    public async Task ToggleAsync()
    {
        if (IsRunning)
        {
            await StopAsync();
            return;
        }

        if (!int.TryParse(PortText, out var port) || port is < 0 or > 65535)
        {
            Message = $"{Strings.InvalidInput}: 포트 (0~65535)";
            return;
        }

        if (!int.TryParse(DisplayCountText, out var displayCount) || displayCount < 1)
        {
            Message = $"{Strings.InvalidInput}: 표시 개수";
            return;
        }

        var simulator = new ModbusTcpSimulator(new SimulatorOptions { Port = port });
        try
        {
            simulator.Start();
        }
        catch (Exception ex)
        {
            Message = $"시뮬레이터 시작 실패: {ex.Message}";
            await simulator.DisposeAsync();
            return;
        }

        _simulator = simulator;
        _displayCount = Math.Min(displayCount, simulator.Options.AreaSize);
        BuildRows();
        Message = "";
        IsRunning = true;
        UpdateStatus();
        OnPropertyChanged(nameof(ActualPort));

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => RefreshNow();
        _timer.Start();
    }

    /// <summary>시뮬레이터를 정지한다.</summary>
    public async Task StopAsync()
    {
        _timer?.Stop();
        _timer = null;
        if (_simulator is { } simulator)
        {
            await simulator.DisposeAsync();
            _simulator = null;
        }

        IsRunning = false;
        StatusText = "정지됨";
    }

    /// <summary>정지 후 자원을 정리한다.</summary>
    public async ValueTask DisposeAsync() => await StopAsync();

    /// <summary>
    /// 표시 값을 시뮬레이터 현재 값으로 갱신한다 (자동 변화가 켜져 있으면 먼저 증가).
    /// 타이머(500ms)와 테스트에서 호출한다.
    /// </summary>
    public void RefreshNow()
    {
        if (_simulator is not { } simulator)
        {
            return;
        }

        if (AutoChange)
        {
            simulator.IncrementRegisters(_displayCount);
        }

        foreach (var row in HoldingRows)
        {
            row.UpdateFromSimulator(simulator.GetHoldingRegister(row.Address));
        }

        foreach (var row in InputRows)
        {
            row.UpdateFromSimulator(simulator.GetInputRegister(row.Address));
        }

        foreach (var row in CoilRows)
        {
            row.UpdateFromSimulator(simulator.GetCoil(row.Address));
        }

        foreach (var row in DiscreteRows)
        {
            row.UpdateFromSimulator(simulator.GetDiscreteInput(row.Address));
        }

        UpdateStatus();
    }

    private void BuildRows()
    {
        HoldingRows.Clear();
        InputRows.Clear();
        CoilRows.Clear();
        DiscreteRows.Clear();
        for (var i = 0; i < _displayCount; i++)
        {
            HoldingRows.Add(new SimRegisterRowViewModel(
                i, (address, value) => _simulator?.SetHoldingRegister(address, value)));
            InputRows.Add(new SimRegisterRowViewModel(
                i, (address, value) => _simulator?.SetInputRegister(address, value)));
            CoilRows.Add(new SimBitRowViewModel(
                i, (address, value) => _simulator?.SetCoil(address, value)));
            DiscreteRows.Add(new SimBitRowViewModel(
                i, (address, value) => _simulator?.SetDiscreteInput(address, value)));
        }
    }

    private void UpdateStatus()
    {
        if (_simulator is { } simulator)
        {
            StatusText =
                $"수신 중 — 127.0.0.1:{simulator.Port} | 클라이언트 {simulator.ClientCount} | " +
                $"요청 {simulator.RequestCount}";
        }
    }
}
