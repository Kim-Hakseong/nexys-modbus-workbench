using Avalonia.Headless.XUnit;
using Nmw.App.Models;
using Nmw.App.ViewModels;
using Nmw.App.Views;
using Xunit;

namespace Nmw.App.Tests;

/// <summary>M9 스모크: 멀티 폴 탭 + 워크스페이스 저장→로드 라운드트립.</summary>
public sealed class WorkspaceSmokeTests
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
    public async Task MultiplePollTabs_PollConcurrentlyOnSharedChannel()
    {
        await using var slave = new Integration.Tests.TestSlave.TestSlave();
        slave.Start();
        slave.HoldingRegisters[0] = 11;
        slave.HoldingRegisters[50] = 22;

        var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        await viewModel.ApplyConnectionAsync(new ConnectionParameters
        {
            Mode = ConnectionMode.Tcp,
            Host = "127.0.0.1",
            Port = slave.Port,
        });

        var first = viewModel.Polls[0];
        first.QuantityText = "1";
        first.ScanRateText = "50";
        await first.TogglePollCommand.ExecuteAsync(null);

        viewModel.AddPollCommand.Execute(null);
        Assert.Equal(2, viewModel.Polls.Count);
        var second = viewModel.Polls[1];
        second.StartAddressText = "50";
        second.QuantityText = "1";
        second.ScanRateText = "50";
        await second.TogglePollCommand.ExecuteAsync(null);

        await WaitUntilAsync(() => first.Rows[0].Value == "11" && second.Rows[0].Value == "22");
        Assert.True(first.IsPolling);
        Assert.True(second.IsPolling);

        await viewModel.DisconnectCommand.ExecuteAsync(null);
        Assert.False(first.IsPolling);
        Assert.False(second.IsPolling);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Workspace_SaveThenLoad_RestoresEverything()
    {
        await using var slave = new Integration.Tests.TestSlave.TestSlave();
        slave.Start();
        slave.HoldingRegisters[3] = 333;

        // 원본 VM 구성: 연결 + 폴 2개 + 별칭 + 1-base 표기
        var source = new MainWindowViewModel();
        var sourceWindow = new MainWindow { DataContext = source };
        sourceWindow.Show();
        await source.ApplyConnectionAsync(new ConnectionParameters
        {
            Mode = ConnectionMode.Tcp,
            Host = "127.0.0.1",
            Port = slave.Port,
            TimeoutMs = 700,
            Retries = 2,
        });

        var pollA = source.Polls[0];
        pollA.Name = "온도 폴";
        pollA.StartAddressText = "3";
        pollA.QuantityText = "4";
        pollA.ScanRateText = "50";
        await pollA.TogglePollCommand.ExecuteAsync(null);
        pollA.Rows[0].Alias = "급수온도";

        source.AddPollCommand.Execute(null);
        var pollB = source.Polls[1];
        pollB.Name = "코일 폴";
        pollB.FunctionIndex = 0; // FC01
        pollB.QuantityText = "8";
        pollB.ScanRateText = "100";

        source.OneBasedAddress = true;
        var json = source.SaveWorkspaceJson();
        await source.DisconnectCommand.ExecuteAsync(null);
        sourceWindow.Close();

        // JSON 내용 확인
        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.Contains("온도 폴", json, StringComparison.Ordinal);
        Assert.Contains("급수온도", json, StringComparison.Ordinal);
        Assert.Contains("\"addressBase\": \"OneBase\"", json, StringComparison.Ordinal);

        // 새 VM에 로드 → 복원 + 자동 연결
        var target = new MainWindowViewModel();
        var targetWindow = new MainWindow { DataContext = target };
        targetWindow.Show();
        Assert.True(await target.LoadWorkspaceJsonAsync(json), target.GlobalMessage);

        Assert.True(target.OneBasedAddress);
        Assert.Equal(2, target.Polls.Count);
        var restoredA = target.Polls[0];
        Assert.Equal("온도 폴", restoredA.Name);
        Assert.Equal("40004", restoredA.StartAddressText); // 프로토콜 3 → 1-base 표기
        Assert.Equal("4", restoredA.QuantityText);
        Assert.Equal("50", restoredA.ScanRateText);
        Assert.Equal("코일 폴", target.Polls[1].Name);
        Assert.Equal(0, target.Polls[1].FunctionIndex);

        // 자동 연결 후 폴 시작 → 별칭·값 복원 확인
        Assert.True(target.IsConnected);
        await restoredA.TogglePollCommand.ExecuteAsync(null);
        Assert.True(restoredA.IsPolling, restoredA.LastError);
        Assert.Equal("급수온도", restoredA.Rows[0].Alias);
        await WaitUntilAsync(() => restoredA.Rows[0].Value == "333");

        await target.DisconnectCommand.ExecuteAsync(null);
        targetWindow.Close();
    }

    [AvaloniaFact]
    public async Task Workspace_LoadCorrupted_FailsWholesaleWithMessage()
    {
        var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        viewModel.Polls[0].Name = "기존 폴";

        Assert.False(await viewModel.LoadWorkspaceJsonAsync("{ broken"));
        Assert.NotEqual("", viewModel.GlobalMessage);
        // 부분 로드 금지 — 기존 상태 유지
        Assert.Equal("기존 폴", viewModel.Polls[0].Name);

        Assert.False(await viewModel.LoadWorkspaceJsonAsync("""{ "schemaVersion": 42 }"""));
        Assert.Contains("스키마 버전", viewModel.GlobalMessage, StringComparison.Ordinal);

        window.Close();
    }

    [AvaloniaFact]
    public async Task RemovePoll_KeepsAtLeastOne()
    {
        var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        await viewModel.RemoveSelectedPollCommand.ExecuteAsync(null);
        Assert.Single(viewModel.Polls); // 마지막 1개는 유지

        viewModel.AddPollCommand.Execute(null);
        Assert.Equal(2, viewModel.Polls.Count);
        await viewModel.RemoveSelectedPollCommand.ExecuteAsync(null);
        Assert.Single(viewModel.Polls);

        window.Close();
    }
}
