using System.Collections.ObjectModel;
using System.IO.Ports;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nmw.App.Models;
using Nmw.Core.Simulator;
using Nmw.Core.Transport;

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
/// Modbus TCP(127.0.0.1) 또는 RTU 시리얼(가상 COM 포트 쌍/컨버터 2개) 슬레이브를 띄우고,
/// 공용 데이터 저장소의 값을 편집/관찰한다.
/// </summary>
public sealed partial class SimulatorViewModel : ObservableObject, IAsyncDisposable
{
    private readonly SimulatorDataStore _store = new(1000);
    private ModbusTcpSimulator? _tcpSimulator;
    private ModbusRtuSlaveSimulator? _rtuSimulator;
    private DispatcherTimer? _timer;
    private int _displayStart;
    private int _displayCount = 20;

    private static readonly int[] BaudByIndex = [1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200];
    private static readonly Parity[] ParityByIndex = [Parity.None, Parity.Even, Parity.Odd];
    private static readonly StopBits[] StopBitsByIndex = [StopBits.One, StopBits.Two];

    [ObservableProperty]
    private int modeIndex; // 0=TCP, 1=RTU 시리얼

    [ObservableProperty]
    private string portText = "1502";

    [ObservableProperty]
    private string serialPortText = "";

    [ObservableProperty]
    private int baudIndex = 3; // 9600

    [ObservableProperty]
    private int parityIndex; // None

    [ObservableProperty]
    private int stopBitsIndex; // One

    [ObservableProperty]
    private string displayStartText = "0";

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

    /// <summary>모드 콤보 항목.</summary>
    public IReadOnlyList<string> ModeOptions { get; } = ["Modbus TCP (127.0.0.1)", "RTU 시리얼 (COM 포트)"];

    /// <summary>보레이트 콤보 항목.</summary>
    public IReadOnlyList<string> BaudOptions { get; } =
        ["1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200"];

    /// <summary>패리티 콤보 항목.</summary>
    public IReadOnlyList<string> ParityOptions { get; } = ["None", "Even", "Odd"];

    /// <summary>정지 비트 콤보 항목.</summary>
    public IReadOnlyList<string> StopBitsOptions { get; } = ["1", "2"];

    /// <summary>사용 가능한 시리얼 포트 목록.</summary>
    public IReadOnlyList<string> SerialPortNames => SerialPort.GetPortNames();

    /// <summary>TCP 모드 여부 (패널 표시용).</summary>
    public bool IsTcpMode => ModeIndex == 0;

    /// <summary>RTU 시리얼 모드 여부 (패널 표시용).</summary>
    public bool IsRtuMode => ModeIndex == 1;

    /// <summary>홀딩 레지스터 행 (편집 가능, FC03/06/16 대상).</summary>
    public ObservableCollection<SimRegisterRowViewModel> HoldingRows { get; } = [];

    /// <summary>입력 레지스터 행 (편집 가능, FC04 대상).</summary>
    public ObservableCollection<SimRegisterRowViewModel> InputRows { get; } = [];

    /// <summary>코일 행 (편집 가능, FC01/05/15 대상).</summary>
    public ObservableCollection<SimBitRowViewModel> CoilRows { get; } = [];

    /// <summary>접점 행 (편집 가능, FC02 대상).</summary>
    public ObservableCollection<SimBitRowViewModel> DiscreteRows { get; } = [];

    /// <summary>실행 중인 TCP 시뮬레이터의 실제 포트 (테스트/표시용).</summary>
    public int ActualPort => _tcpSimulator?.Port ?? 0;

    /// <summary>기본 표시 범위(0~19)로 그리드 행을 미리 구성한다.</summary>
    public SimulatorViewModel()
    {
        ApplyDisplayRange();
    }

    /// <summary>시뮬레이터를 시작/정지한다.</summary>
    [RelayCommand]
    public async Task ToggleAsync()
    {
        if (IsRunning)
        {
            await StopAsync();
            return;
        }

        if (IsTcpMode)
        {
            if (!int.TryParse(PortText, out var port) || port is < 0 or > 65535)
            {
                Message = $"{Strings.InvalidInput}: 포트 (0~65535)";
                return;
            }

            var simulator = new ModbusTcpSimulator(new SimulatorOptions { Port = port }, _store);
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

            _tcpSimulator = simulator;
        }
        else
        {
            var portName = SerialPortText.Trim();
            if (portName.Length == 0)
            {
                Message = $"{Strings.InvalidInput}: 시리얼 포트를 선택하세요";
                return;
            }

            var settings = new SerialTransportSettings(
                portName,
                BaudByIndex[Math.Clamp(BaudIndex, 0, BaudByIndex.Length - 1)],
                ParityByIndex[Math.Clamp(ParityIndex, 0, ParityByIndex.Length - 1)],
                8,
                StopBitsByIndex[Math.Clamp(StopBitsIndex, 0, StopBitsByIndex.Length - 1)]);
            var simulator = new ModbusRtuSlaveSimulator(settings, _store);
            try
            {
                simulator.Start();
            }
            catch (Exception ex)
            {
                Message = $"시리얼 포트 열기 실패: {ex.Message}";
                await simulator.DisposeAsync();
                return;
            }

            _rtuSimulator = simulator;
        }

        ApplyDisplayRange();
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
        if (_tcpSimulator is { } tcp)
        {
            await tcp.DisposeAsync();
            _tcpSimulator = null;
        }

        if (_rtuSimulator is { } rtu)
        {
            await rtu.DisposeAsync();
            _rtuSimulator = null;
        }

        IsRunning = false;
        StatusText = "정지됨";
    }

    /// <summary>정지 후 자원을 정리한다.</summary>
    public async ValueTask DisposeAsync() => await StopAsync();

    /// <summary>
    /// 표시 값을 저장소 현재 값으로 갱신한다 (자동 변화가 켜져 있으면 먼저 증가).
    /// 타이머(500ms)와 테스트에서 호출한다.
    /// </summary>
    public void RefreshNow()
    {
        if (!IsRunning)
        {
            return;
        }

        if (AutoChange)
        {
            _store.IncrementRegisters(_displayStart, _displayCount);
        }

        RefreshValues();
        UpdateStatus();
    }

    /// <summary>그리드 행 표시 값을 저장소 현재 값으로 갱신한다 (실행 여부 무관).</summary>
    public void RefreshValues()
    {
        foreach (var row in HoldingRows)
        {
            row.UpdateFromSimulator(_store.GetHoldingRegister(row.Address));
        }

        foreach (var row in InputRows)
        {
            row.UpdateFromSimulator(_store.GetInputRegister(row.Address));
        }

        foreach (var row in CoilRows)
        {
            row.UpdateFromSimulator(_store.GetCoil(row.Address));
        }

        foreach (var row in DiscreteRows)
        {
            row.UpdateFromSimulator(_store.GetDiscreteInput(row.Address));
        }
    }

    /// <summary>
    /// 표시 시작 주소/개수 입력을 파싱해 그리드 행을 다시 만든다.
    /// 실행 중에도 즉시 반영되며, 값은 저장소에 있으므로 범위를 옮겨도 유지된다.
    /// </summary>
    public void ApplyDisplayRange()
    {
        if (!int.TryParse(DisplayStartText, out var start) ||
            start < 0 || start >= _store.AreaSize ||
            !int.TryParse(DisplayCountText, out var count) || count < 1)
        {
            return; // 입력 중 미완성 값은 무시 (마지막 유효 범위 유지)
        }

        _displayStart = start;
        _displayCount = Math.Min(count, _store.AreaSize - start);
        BuildRows();
        RefreshValues();
    }

    partial void OnDisplayStartTextChanged(string value) => ApplyDisplayRange();

    partial void OnDisplayCountTextChanged(string value) => ApplyDisplayRange();

    partial void OnModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTcpMode));
        OnPropertyChanged(nameof(IsRtuMode));
        OnPropertyChanged(nameof(SerialPortNames));
    }

    private void BuildRows()
    {
        HoldingRows.Clear();
        InputRows.Clear();
        CoilRows.Clear();
        DiscreteRows.Clear();
        for (var i = 0; i < _displayCount; i++)
        {
            var address = _displayStart + i;
            HoldingRows.Add(new SimRegisterRowViewModel(
                address, (a, value) => _store.SetHoldingRegister(a, value)));
            InputRows.Add(new SimRegisterRowViewModel(
                address, (a, value) => _store.SetInputRegister(a, value)));
            CoilRows.Add(new SimBitRowViewModel(
                address, (a, value) => _store.SetCoil(a, value)));
            DiscreteRows.Add(new SimBitRowViewModel(
                address, (a, value) => _store.SetDiscreteInput(a, value)));
        }
    }

    private void UpdateStatus()
    {
        if (_tcpSimulator is { } tcp)
        {
            StatusText =
                $"수신 중 — 127.0.0.1:{tcp.Port} | 클라이언트 {tcp.ClientCount} | 요청 {tcp.RequestCount}";
        }
        else if (_rtuSimulator is { } rtu)
        {
            StatusText =
                $"수신 중 — {rtu.PortName} ({rtu.BaudRate}bps) | 요청 {rtu.RequestCount}";
        }
    }
}
