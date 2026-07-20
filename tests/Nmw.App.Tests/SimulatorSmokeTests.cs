using Avalonia.Headless.XUnit;
using Nmw.App.Models;
using Nmw.App.ViewModels;
using Nmw.App.Views;
using Xunit;

namespace Nmw.App.Tests;

/// <summary>내장 시뮬레이터 스모크: 장비 없이 앱 단독으로 폴링/쓰기 전체 흐름 검증.</summary>
public sealed class SimulatorSmokeTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Assert.True(condition(), "대기 조건이 시간 내에 충족되지 않았습니다.");
    }

    [AvaloniaFact]
    public async Task SimulatorOnly_FullLoop_PollAndWriteWithoutRealDevice()
    {
        var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        // 1. 시뮬레이터 시작 (임시 포트)
        var simulator = viewModel.Simulator;
        simulator.PortText = "0";
        await simulator.ToggleCommand.ExecuteAsync(null);
        Assert.True(simulator.IsRunning, simulator.Message);
        Assert.True(simulator.ActualPort > 0);
        Assert.Contains("127.0.0.1", simulator.StatusText, StringComparison.Ordinal);

        // 2. 시뮬레이터 그리드에서 값 편집 (홀딩 0 = 123, 코일 2 = ON)
        simulator.HoldingRows[0].ValueText = "123";
        simulator.CoilRows[2].Value = true;

        // 3. 마스터를 시뮬레이터에 연결 → 폴링 → 값 확인
        await viewModel.ApplyConnectionAsync(new ConnectionParameters
        {
            Mode = ConnectionMode.Tcp,
            Host = "127.0.0.1",
            Port = simulator.ActualPort,
        });
        Assert.True(viewModel.IsConnected);

        var poll = viewModel.Polls[0];
        poll.QuantityText = "3";
        poll.ScanRateText = "50";
        await poll.TogglePollCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => poll.Rows[0].Value == "123");

        // 4. 마스터에서 쓰기 → 시뮬레이터에 반영 확인
        var write = await viewModel.Master!.WriteSingleRegisterAsync(1, 1, 456);
        Assert.True(write.IsSuccess, write.Error?.Text);
        simulator.RefreshNow();
        Assert.Equal("456", simulator.HoldingRows[1].ValueText);
        await WaitUntilAsync(() => poll.Rows[1].Value == "456");

        // 5. 코일 폴로 시뮬레이터 코일 확인
        viewModel.AddPollCommand.Execute(null);
        var coilPoll = viewModel.Polls[1];
        coilPoll.FunctionIndex = 0; // FC01
        coilPoll.QuantityText = "4";
        coilPoll.ScanRateText = "50";
        await coilPoll.TogglePollCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => coilPoll.Rows[2].Value == Strings.On);

        // 6. 정리: 해제 → 시뮬레이터 정지
        await viewModel.DisconnectCommand.ExecuteAsync(null);
        await simulator.StopAsync();
        Assert.False(simulator.IsRunning);
        Assert.Equal("정지됨", simulator.StatusText);
        window.Close();
    }

    [AvaloniaFact]
    public async Task AutoChange_MakesPolledValuesMove()
    {
        var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        var simulator = viewModel.Simulator;
        simulator.PortText = "0";
        await simulator.ToggleCommand.ExecuteAsync(null);
        Assert.True(simulator.IsRunning, simulator.Message);
        simulator.AutoChange = true;

        await viewModel.ApplyConnectionAsync(new ConnectionParameters
        {
            Mode = ConnectionMode.Tcp,
            Host = "127.0.0.1",
            Port = simulator.ActualPort,
        });
        var poll = viewModel.Polls[0];
        poll.QuantityText = "1";
        poll.ScanRateText = "50";
        await poll.TogglePollCommand.ExecuteAsync(null);

        // 자동 변화(RefreshNow마다 +1)로 폴 값이 계속 바뀌어야 한다
        simulator.RefreshNow();
        await WaitUntilAsync(() => poll.Rows[0].Value != "" && poll.Rows[0].Value != "0");
        var first = poll.Rows[0].Value;
        simulator.RefreshNow();
        simulator.RefreshNow();
        await WaitUntilAsync(() => poll.Rows[0].Value != first);

        await viewModel.DisconnectCommand.ExecuteAsync(null);
        await simulator.StopAsync();
        window.Close();
    }

    [AvaloniaFact]
    public async Task RtuMode_WithoutPortName_ShowsMessage()
    {
        var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        var simulator = viewModel.Simulator;
        simulator.ModeIndex = 1; // RTU 시리얼
        Assert.True(simulator.IsRtuMode);
        Assert.False(simulator.IsTcpMode);

        simulator.SerialPortText = "";
        await simulator.ToggleCommand.ExecuteAsync(null);

        Assert.False(simulator.IsRunning);
        Assert.Contains("시리얼 포트", simulator.Message, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public async Task DataStore_PersistsAcrossSimulatorRestart()
    {
        var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        var simulator = viewModel.Simulator;
        simulator.PortText = "0";
        await simulator.ToggleCommand.ExecuteAsync(null);
        simulator.HoldingRows[0].ValueText = "777";
        await simulator.StopAsync();

        // 재시작해도 공용 저장소 값 유지 (TCP/RTU 모드가 같은 저장소 공유)
        await simulator.ToggleCommand.ExecuteAsync(null);
        simulator.RefreshNow();
        Assert.Equal("777", simulator.HoldingRows[0].ValueText);

        await simulator.StopAsync();
        window.Close();
    }

    [AvaloniaFact]
    public async Task SimulatorStart_InvalidPort_ShowsMessage()
    {
        var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        var simulator = viewModel.Simulator;
        simulator.PortText = "99999";
        await simulator.ToggleCommand.ExecuteAsync(null);

        Assert.False(simulator.IsRunning);
        Assert.NotEqual("", simulator.Message);
        window.Close();
    }
}
