using Avalonia.Headless.XUnit;
using Nmw.App.Models;
using Nmw.App.ViewModels;
using Nmw.App.Views;
using Xunit;

namespace Nmw.App.Tests;

/// <summary>
/// M6 스모크: 테스트 슬레이브를 띄우고 헤드리스로 앱 윈도우를 실행해
/// 연결 → 폴 시작 → 그리드 값 갱신 → 정지 → 해제 시나리오를 검증한다.
/// </summary>
public sealed class MainWindowSmokeTests
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
    public async Task ConnectPollStopDisconnect_Scenario()
    {
        await using var slave = new Integration.Tests.TestSlave.TestSlave();
        slave.Start();
        for (var i = 0; i < 10; i++)
        {
            slave.HoldingRegisters[i] = (ushort)(i * 10);
        }

        var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        // 연결 (다이얼로그 결과를 직접 적용)
        await viewModel.ApplyConnectionAsync(new ConnectionParameters
        {
            Mode = ConnectionMode.Tcp,
            Host = "127.0.0.1",
            Port = slave.Port,
            TimeoutMs = 1000,
            Retries = 1,
        });
        Assert.True(viewModel.IsConnected);
        Assert.Contains("TCP", viewModel.ConnectionStatus, StringComparison.Ordinal);

        // 폴 시작 (FC03, 주소 0, 10개, 50ms)
        var poll = viewModel.Polls[0];
        poll.UnitIdText = "1";
        poll.StartAddressText = "0";
        poll.QuantityText = "10";
        poll.ScanRateText = "50";
        poll.FunctionIndex = 2;
        await poll.TogglePollCommand.ExecuteAsync(null);
        Assert.True(poll.IsPolling);
        Assert.Equal(10, poll.Rows.Count);

        // 값 갱신 대기 (스냅샷 → Dispatcher 마샬링)
        await WaitUntilAsync(() => poll.Rows[9].Value.Length > 0);
        Assert.Equal("0", poll.Rows[0].Value);
        Assert.Equal("50", poll.Rows[5].Value);
        Assert.Equal("90", poll.Rows[9].Value);
        Assert.Contains("Tx", poll.StatsText, StringComparison.Ordinal);
        Assert.Equal("", poll.LastError);

        // 정지 → 해제
        await poll.TogglePollCommand.ExecuteAsync(null);
        Assert.False(poll.IsPolling);
        await viewModel.DisconnectCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsConnected);

        window.Close();
    }

    [AvaloniaFact]
    public async Task PollWithoutConnection_ShowsError()
    {
        var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        var poll = viewModel.Polls[0];
        await poll.TogglePollCommand.ExecuteAsync(null);

        Assert.False(poll.IsPolling);
        Assert.Equal(Strings.NotConnectedError, poll.LastError);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ExceptionFromSlave_ShownInErrorArea()
    {
        await using var slave = new Integration.Tests.TestSlave.TestSlave();
        slave.Start();

        var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        await viewModel.ApplyConnectionAsync(new ConnectionParameters
        {
            Mode = ConnectionMode.Tcp,
            Host = "127.0.0.1",
            Port = slave.Port,
        });

        // 슬레이브 맵(0..99) 밖 주소 → Illegal Data Address
        var poll = viewModel.Polls[0];
        poll.StartAddressText = "500";
        poll.QuantityText = "10";
        poll.ScanRateText = "50";
        await poll.TogglePollCommand.ExecuteAsync(null);

        await WaitUntilAsync(() => poll.LastError.Length > 0);
        Assert.Equal("Illegal Data Address", poll.LastError);

        await viewModel.DisconnectCommand.ExecuteAsync(null);
        window.Close();
    }
}
