using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nmw.Core.Data;
using Nmw.Core.Polling;
using Nmw.Core.Protocol;
using Nmw.Core.Workspace;

namespace Nmw.App.ViewModels;

/// <summary>탭 하나에 해당하는 폴 뷰모델 (설정 + 그리드 + 통계).</summary>
public sealed partial class PollViewModel : ObservableObject
{
    private readonly MainWindowViewModel _owner;
    private readonly Dictionary<ushort, string> _aliases = [];

    private RegisterFormat _activeFormat = RegisterFormat.U16;
    private WordOrder _activeOrder = WordOrder.ABCD;
    private FunctionCode _activeFunction = FunctionCode.ReadHoldingRegisters;
    private bool _activeIsBits;

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private string unitIdText = "1";

    [ObservableProperty]
    private string startAddressText = "0";

    [ObservableProperty]
    private string quantityText = "10";

    [ObservableProperty]
    private string scanRateText = "500";

    [ObservableProperty]
    private int functionIndex = 2; // FC03

    [ObservableProperty]
    private int formatIndex; // U16

    [ObservableProperty]
    private int wordOrderIndex; // ABCD

    [ObservableProperty]
    private bool isPolling;

    [ObservableProperty]
    private string statsText = Strings.PollStopped;

    [ObservableProperty]
    private string lastError = "";

    /// <summary>폴 식별자 (엔진/워크스페이스 키).</summary>
    public string Id { get; }

    /// <summary>소유 뷰모델과 ID, 이름으로 폴 뷰모델을 만든다.</summary>
    /// <param name="owner">메인 뷰모델.</param>
    /// <param name="id">폴 ID.</param>
    /// <param name="name">표시 이름.</param>
    public PollViewModel(MainWindowViewModel owner, string id, string name)
    {
        _owner = owner;
        Id = id;
        this.name = name;
    }

    /// <summary>function 콤보 항목.</summary>
    public IReadOnlyList<string> FunctionOptions => PollOptions.FunctionOptions;

    /// <summary>포맷 콤보 항목.</summary>
    public IReadOnlyList<string> FormatOptions => PollOptions.FormatOptions;

    /// <summary>워드오더 콤보 항목.</summary>
    public IReadOnlyList<string> WordOrderOptions => PollOptions.WordOrderOptions;

    /// <summary>폴 그리드 행 (항목 재사용).</summary>
    public ObservableCollection<RegisterRowViewModel> Rows { get; } = [];

    /// <summary>현재 활성 폴이 비트(코일/접점) 폴인지.</summary>
    public bool ActivePollIsBits => _activeIsBits;

    /// <summary>현재 Unit ID 입력값 (파싱 실패 시 1).</summary>
    public byte CurrentUnitId => byte.TryParse(UnitIdText, out var unitId) ? unitId : (byte)1;

    /// <summary>이 폴의 별칭 사본 (워크스페이스 저장용).</summary>
    public Dictionary<ushort, string> AliasesSnapshot => new(_aliases);

    /// <summary>폴링을 시작/정지한다.</summary>
    [RelayCommand]
    public async Task TogglePollAsync()
    {
        if (IsPolling)
        {
            if (_owner.Engine is { } running)
            {
                await running.StopPollAsync(Id);
            }

            IsPolling = false;
            return;
        }

        if (_owner.Engine is not { } engine)
        {
            LastError = Strings.NotConnectedError;
            return;
        }

        var function = PollOptions.FunctionByIndex[
            Math.Clamp(FunctionIndex, 0, PollOptions.FunctionByIndex.Length - 1)];
        if (!byte.TryParse(UnitIdText, out var unitId) ||
            !AddressNotation.TryParse(
                StartAddressText, AddressNotation.AreaOf(function), _owner.CurrentAddressBase,
                out var startAddress) ||
            !ushort.TryParse(QuantityText, out var quantity) || quantity == 0 ||
            !int.TryParse(ScanRateText, out var scanRate))
        {
            LastError = $"{Strings.InvalidInput}: Unit ID/주소/개수/주기를 확인하세요";
            return;
        }

        _activeFunction = function;
        _activeFormat = PollOptions.FormatByIndex[
            Math.Clamp(FormatIndex, 0, PollOptions.FormatByIndex.Length - 1)];
        _activeOrder = PollOptions.WordOrderByIndex[
            Math.Clamp(WordOrderIndex, 0, PollOptions.WordOrderByIndex.Length - 1)];
        _activeIsBits = function is FunctionCode.ReadCoils or FunctionCode.ReadDiscreteInputs;

        BuildRows(startAddress, quantity);

        try
        {
            engine.StartPoll(new PollDefinition
            {
                Id = Id,
                Name = Name,
                UnitId = unitId,
                Function = function,
                StartAddress = startAddress,
                Quantity = quantity,
                ScanRateMs = scanRate,
                Format = _activeFormat,
                WordOrder = _activeOrder,
                Aliases = new Dictionary<ushort, string>(_aliases),
            });
        }
        catch (Exception ex)
        {
            LastError = $"{Strings.InvalidInput}: {ex.Message}";
            return;
        }

        LastError = "";
        IsPolling = true;
    }

    /// <summary>연결 해제 등으로 폴링 상태를 강제로 정지 표시한다.</summary>
    public void MarkStopped()
    {
        IsPolling = false;
        StatsText = Strings.PollStopped;
    }

    /// <summary>주소 표기 변경 시 행 주소를 다시 표기한다.</summary>
    /// <param name="addressBase">새 표기 방식.</param>
    public void ReformatAddresses(AddressBase addressBase)
    {
        var area = AddressNotation.AreaOf(_activeFunction);
        foreach (var row in Rows)
        {
            row.Address = AddressNotation.Format(row.ProtocolAddress, area, addressBase);
        }
    }

    /// <summary>주소 표기 변경 시 시작 주소 입력값을 새 표기로 변환한다.</summary>
    /// <param name="from">이전 표기 방식.</param>
    /// <param name="to">새 표기 방식.</param>
    public void ConvertStartAddressBase(AddressBase from, AddressBase to)
    {
        var function = PollOptions.FunctionByIndex[
            Math.Clamp(FunctionIndex, 0, PollOptions.FunctionByIndex.Length - 1)];
        var area = AddressNotation.AreaOf(function);
        if (AddressNotation.TryParse(StartAddressText, area, from, out var protocolAddress))
        {
            StartAddressText = AddressNotation.Format(protocolAddress, area, to);
        }
    }

    /// <summary>이 폴의 스냅샷을 그리드/통계에 반영한다 (UI 스레드에서 호출).</summary>
    /// <param name="snapshot">엔진이 발행한 스냅샷.</param>
    public void ApplySnapshot(PollSnapshot snapshot)
    {
        var stats = snapshot.Stats;
        StatsText =
            $"Tx {stats.TxCount} | OK {stats.ValidRx} | Err {stats.ErrorCount} | " +
            $"{stats.LastResponseMs:F0} ms";
        LastError = snapshot.LastError?.Text ?? "";
        if (snapshot.LastError is { } error)
        {
            _owner.TrafficLog.AddError(error.Text, snapshot.At);
        }

        if (snapshot.Bits is { } bits)
        {
            var count = Math.Min(Rows.Count, bits.Length);
            for (var i = 0; i < count; i++)
            {
                Rows[i].RawBit = bits[i];
                Rows[i].Value = bits[i] ? Strings.On : Strings.Off;
            }

            return;
        }

        if (snapshot.Registers is { } registers)
        {
            if (_activeFormat == RegisterFormat.Ascii)
            {
                if (Rows.Count > 0)
                {
                    Rows[0].RawRegister = registers.Length > 0 ? registers[0] : (ushort)0;
                    Rows[0].Value = RegisterFormatter.Format(registers, RegisterFormat.Ascii, _activeOrder);
                }

                return;
            }

            var perValue = RegisterFormatter.RegistersPerValue(_activeFormat);
            for (var i = 0; i < Rows.Count; i++)
            {
                var offset = i * perValue;
                if (offset + perValue <= registers.Length)
                {
                    Rows[i].RawRegister = registers[offset];
                    Rows[i].Value = RegisterFormatter.Format(
                        registers.AsSpan(offset, perValue), _activeFormat, _activeOrder);
                }
            }
        }
    }

    /// <summary>워크스페이스 폴 설정으로 이 뷰모델을 채운다.</summary>
    /// <param name="config">폴 설정.</param>
    /// <param name="addressBase">시작 주소 표기에 사용할 방식.</param>
    public void ApplyConfig(PollConfig config, AddressBase addressBase)
    {
        Name = config.Name.Length > 0 ? config.Name : Name;
        UnitIdText = config.UnitId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        FunctionIndex = PollOptions.IndexOfFunction((FunctionCode)config.Function);
        StartAddressText = AddressNotation.Format(
            config.StartAddress, AddressNotation.AreaOf((FunctionCode)config.Function), addressBase);
        QuantityText = config.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ScanRateText = config.ScanRateMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        FormatIndex = PollOptions.IndexOfFormat(config.Format);
        WordOrderIndex = PollOptions.IndexOfWordOrder(config.WordOrder);
        _aliases.Clear();
        foreach (var (address, alias) in config.Aliases)
        {
            _aliases[address] = alias;
        }
    }

    /// <summary>현재 설정을 워크스페이스 폴 설정으로 변환한다.</summary>
    /// <param name="channelId">소속 채널 ID.</param>
    public PollConfig ToConfig(string channelId)
    {
        var function = PollOptions.FunctionByIndex[
            Math.Clamp(FunctionIndex, 0, PollOptions.FunctionByIndex.Length - 1)];
        AddressNotation.TryParse(
            StartAddressText, AddressNotation.AreaOf(function), _owner.CurrentAddressBase,
            out var startAddress);
        _ = byte.TryParse(UnitIdText, out var unitId);
        _ = ushort.TryParse(QuantityText, out var quantity);
        _ = int.TryParse(ScanRateText, out var scanRate);
        return new PollConfig
        {
            ChannelId = channelId,
            Name = Name,
            UnitId = unitId == 0 ? (byte)1 : unitId,
            Function = (int)function,
            StartAddress = startAddress,
            Quantity = quantity == 0 ? (ushort)10 : quantity,
            ScanRateMs = scanRate == 0 ? 500 : scanRate,
            Format = PollOptions.FormatByIndex[
                Math.Clamp(FormatIndex, 0, PollOptions.FormatByIndex.Length - 1)],
            WordOrder = PollOptions.WordOrderByIndex[
                Math.Clamp(WordOrderIndex, 0, PollOptions.WordOrderByIndex.Length - 1)],
            Aliases = new Dictionary<ushort, string>(_aliases),
        };
    }

    private void BuildRows(ushort startAddress, ushort quantity)
    {
        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        Rows.Clear();
        var area = AddressNotation.AreaOf(_activeFunction);

        void AddRow(ushort protocolAddress)
        {
            var row = new RegisterRowViewModel
            {
                ProtocolAddress = protocolAddress,
                Address = AddressNotation.Format(protocolAddress, area, _owner.CurrentAddressBase),
                Alias = _aliases.GetValueOrDefault(protocolAddress, ""),
            };
            row.PropertyChanged += OnRowPropertyChanged;
            Rows.Add(row);
        }

        if (_activeIsBits)
        {
            for (var i = 0; i < quantity; i++)
            {
                AddRow((ushort)(startAddress + i));
            }

            return;
        }

        if (_activeFormat == RegisterFormat.Ascii)
        {
            AddRow(startAddress);
            return;
        }

        var perValue = RegisterFormatter.RegistersPerValue(_activeFormat);
        for (var i = 0; i + perValue <= quantity; i += perValue)
        {
            AddRow((ushort)(startAddress + i));
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RegisterRowViewModel.Alias) &&
            sender is RegisterRowViewModel row)
        {
            _aliases[row.ProtocolAddress] = row.Alias;
        }
    }
}
