using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nmw.App.Models;
using Nmw.Core.Client;
using Nmw.Core.Data;
using Nmw.Core.Polling;
using Nmw.Core.Transport;
using Nmw.Core.Workspace;

namespace Nmw.App.ViewModels;

/// <summary>메인 윈도우 뷰모델: 채널 연결 + 멀티 폴 탭 + 워크스페이스 + 트래픽 로그.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private const string ChannelId = "ch1";

    private ModbusMaster? _master;
    private PollEngine? _engine;
    private ConnectionParameters? _lastParameters;
    private int _pollCounter;

    [ObservableProperty]
    private bool isConnected;

    [ObservableProperty]
    private string connectionStatus = Strings.NotConnected;

    [ObservableProperty]
    private bool oneBasedAddress;

    [ObservableProperty]
    private PollViewModel? selectedPoll;

    [ObservableProperty]
    private string globalMessage = "";

    /// <summary>폴 탭들.</summary>
    public ObservableCollection<PollViewModel> Polls { get; } = [];

    /// <summary>트래픽 로그 (TX/RX raw hex + 오류, 링버퍼 5000라인).</summary>
    public TrafficLogViewModel TrafficLog { get; } = new();

    /// <summary>내장 슬레이브 시뮬레이터 (물리 장비 없는 테스트용).</summary>
    public SimulatorViewModel Simulator { get; } = new();

    /// <summary>기본 폴 1개로 뷰모델을 만든다.</summary>
    public MainWindowViewModel()
    {
        AddPoll();
    }

    /// <summary>현재 채널의 마스터 (쓰기 다이얼로그에서 사용).</summary>
    public ModbusMaster? Master => _master;

    /// <summary>현재 채널의 폴링 엔진 (폴 뷰모델에서 사용).</summary>
    public PollEngine? Engine => _engine;

    /// <summary>현재 주소 표기 방식.</summary>
    public AddressBase CurrentAddressBase =>
        OneBasedAddress ? AddressBase.OneBase : AddressBase.ZeroBase;

    /// <summary>쓰기 다이얼로그 기본 Unit ID (선택된 폴 기준).</summary>
    public byte CurrentUnitId => SelectedPoll?.CurrentUnitId ?? 1;

    /// <summary>폴 탭을 추가한다.</summary>
    [RelayCommand]
    public void AddPoll()
    {
        _pollCounter++;
        var poll = new PollViewModel(this, $"poll-{_pollCounter}", $"폴 {_pollCounter}");
        Polls.Add(poll);
        SelectedPoll = poll;
    }

    /// <summary>선택된 폴 탭을 제거한다 (마지막 1개는 유지).</summary>
    [RelayCommand]
    public async Task RemoveSelectedPollAsync()
    {
        if (SelectedPoll is not { } poll || Polls.Count <= 1)
        {
            return;
        }

        if (_engine is { } engine)
        {
            await engine.StopPollAsync(poll.Id);
        }

        var index = Polls.IndexOf(poll);
        Polls.Remove(poll);
        SelectedPoll = Polls[Math.Clamp(index, 0, Polls.Count - 1)];
    }

    /// <summary>연결 파라미터를 적용해 채널을 연결한다.</summary>
    /// <param name="parameters">연결 파라미터.</param>
    public async Task ApplyConnectionAsync(ConnectionParameters parameters)
    {
        await DisconnectAsync();

        ITransport transport = parameters.Mode switch
        {
            ConnectionMode.Tcp => new TcpTransport(new TcpTransportSettings(parameters.Host, parameters.Port)),
            ConnectionMode.RtuOverTcp =>
                new RtuOverTcpTransport(new TcpTransportSettings(parameters.Host, parameters.Port)),
            _ => new SerialTransport(new SerialTransportSettings(
                parameters.SerialPortName, parameters.BaudRate, parameters.Parity,
                parameters.DataBits, parameters.StopBits)),
        };

        try
        {
            await transport.ConnectAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            GlobalMessage = $"{Strings.ConnectFailed}: {ex.Message}";
            ConnectionStatus = Strings.ConnectFailed;
            await transport.DisposeAsync();
            return;
        }

        _master = new ModbusMaster(transport, new ModbusMasterOptions
        {
            TimeoutMs = parameters.TimeoutMs,
            Retries = parameters.Retries,
            InterFrameDelayMs = parameters.InterFrameDelayMs,
        });
        _master.Traffic += OnTraffic;
        _engine = new PollEngine(_master);
        _engine.SnapshotPublished += OnSnapshotPublished;

        _lastParameters = parameters;
        IsConnected = true;
        ConnectionStatus = parameters.Describe();
        GlobalMessage = "";
    }

    /// <summary>채널 연결을 해제한다.</summary>
    [RelayCommand]
    public async Task DisconnectAsync()
    {
        if (_engine is { } engine)
        {
            engine.SnapshotPublished -= OnSnapshotPublished;
            await engine.DisposeAsync();
            _engine = null;
        }

        if (_master is { } master)
        {
            master.Traffic -= OnTraffic;
            await master.DisposeAsync();
            _master = null;
        }

        foreach (var poll in Polls)
        {
            poll.MarkStopped();
        }

        IsConnected = false;
        ConnectionStatus = Strings.NotConnected;
    }

    /// <summary>현재 상태를 워크스페이스 JSON으로 직렬화한다.</summary>
    public string SaveWorkspaceJson()
    {
        var document = new WorkspaceDocument
        {
            Channels = _lastParameters is { } parameters
                ? [WorkspaceMapping.ToChannelConfig(parameters, ChannelId)]
                : [],
            Polls = [.. Polls.Select(p => p.ToConfig(ChannelId))],
            Ui = new UiConfig { AddressBase = CurrentAddressBase },
        };
        return WorkspaceSerializer.Serialize(document);
    }

    /// <summary>
    /// 워크스페이스 JSON을 로드해 연결 설정·폴 탭·별칭·주소 표기를 복원한다.
    /// 채널이 있으면 자동 연결을 시도한다 (실패해도 폴 정의는 복원).
    /// </summary>
    /// <param name="json">.nmw 파일 내용.</param>
    /// <returns>성공 여부 (형식 오류 시 false, 메시지는 GlobalMessage).</returns>
    public async Task<bool> LoadWorkspaceJsonAsync(string json)
    {
        WorkspaceDocument document;
        try
        {
            document = WorkspaceSerializer.Deserialize(json);
        }
        catch (WorkspaceFormatException ex)
        {
            GlobalMessage = ex.Message;
            return false;
        }

        await DisconnectAsync();
        OneBasedAddress = document.Ui.AddressBase == AddressBase.OneBase;

        Polls.Clear();
        _pollCounter = 0;
        foreach (var pollConfig in document.Polls)
        {
            _pollCounter++;
            var poll = new PollViewModel(this, $"poll-{_pollCounter}", $"폴 {_pollCounter}");
            poll.ApplyConfig(pollConfig, CurrentAddressBase);
            Polls.Add(poll);
        }

        if (Polls.Count == 0)
        {
            AddPoll();
        }

        SelectedPoll = Polls[0];

        if (document.Channels.Count > 0)
        {
            await ApplyConnectionAsync(WorkspaceMapping.ToParameters(document.Channels[0]));
        }

        return true;
    }

    partial void OnOneBasedAddressChanged(bool value)
    {
        var previous = value ? AddressBase.ZeroBase : AddressBase.OneBase;
        foreach (var poll in Polls)
        {
            poll.ConvertStartAddressBase(previous, CurrentAddressBase);
            poll.ReformatAddresses(CurrentAddressBase);
        }
    }

    private void OnTraffic(object? sender, TrafficEvent trafficEvent) =>
        Dispatcher.UIThread.Post(() => TrafficLog.AddTraffic(trafficEvent));

    private void OnSnapshotPublished(object? sender, PollSnapshot snapshot) =>
        Dispatcher.UIThread.Post(() =>
        {
            var poll = Polls.FirstOrDefault(p => p.Id == snapshot.PollId);
            poll?.ApplySnapshot(snapshot);
        });
}
