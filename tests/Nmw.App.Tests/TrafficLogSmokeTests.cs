using System.Text;
using Avalonia.Headless.XUnit;
using Nmw.App.Models;
using Nmw.App.ViewModels;
using Nmw.App.Views;
using Nmw.Core.Client;
using Xunit;

namespace Nmw.App.Tests;

/// <summary>M8 스모크: 트래픽 로그 표시/필터/클리어/저장.</summary>
public sealed class TrafficLogSmokeTests
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
    public async Task Polling_ProducesTxRxHexLines()
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

        var poll = viewModel.Polls[0];
        poll.QuantityText = "2";
        poll.ScanRateText = "50";
        await poll.TogglePollCommand.ExecuteAsync(null);

        await WaitUntilAsync(() => viewModel.TrafficLog.VisibleLines.Count >= 4);
        Assert.Contains(viewModel.TrafficLog.VisibleLines, l => l.Contains(" TX  ", StringComparison.Ordinal));
        Assert.Contains(viewModel.TrafficLog.VisibleLines, l => l.Contains(" RX  ", StringComparison.Ordinal));
        // 요청 PDU hex 03 00 00 00 02가 포함되어야 한다
        Assert.Contains(viewModel.TrafficLog.VisibleLines,
            l => l.Contains("03 00 00 00 02", StringComparison.Ordinal));

        await viewModel.DisconnectCommand.ExecuteAsync(null);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ErrorPolling_ShowsErrLines_AndFilterWorks()
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

        var poll = viewModel.Polls[0];
        poll.StartAddressText = "500"; // Illegal Data Address 유발
        poll.QuantityText = "1";
        poll.ScanRateText = "50";
        await poll.TogglePollCommand.ExecuteAsync(null);

        await WaitUntilAsync(() => viewModel.TrafficLog.VisibleLines.Any(
            l => l.Contains("ERR Illegal Data Address", StringComparison.Ordinal)));

        // 에러만 필터
        viewModel.TrafficLog.ErrorsOnly = true;
        Assert.NotEmpty(viewModel.TrafficLog.VisibleLines);
        Assert.All(viewModel.TrafficLog.VisibleLines,
            l => Assert.Contains("ERR", l, StringComparison.Ordinal));

        // 클리어
        viewModel.TrafficLog.Clear();
        Assert.Empty(viewModel.TrafficLog.VisibleLines);

        await viewModel.DisconnectCommand.ExecuteAsync(null);
        window.Close();
    }

    [Fact]
    public async Task Save_WritesVisibleLogToStream()
    {
        var log = new TrafficLogViewModel(capacity: 100);
        log.AddTraffic(new TrafficEvent(
            TrafficDirection.Tx, [0x01, 0x03], new DateTimeOffset(2026, 7, 20, 1, 2, 3, TimeSpan.Zero)));
        log.AddError("Timeout", new DateTimeOffset(2026, 7, 20, 1, 2, 4, TimeSpan.Zero));

        using var stream = new MemoryStream();
        await log.SaveAsync(stream);
        var text = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Contains("TX  01 03", text, StringComparison.Ordinal);
        Assert.Contains("ERR Timeout", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RingLimit_VisibleLinesDoNotExceedCapacity()
    {
        var log = new TrafficLogViewModel(capacity: 10);
        for (var i = 0; i < 30; i++)
        {
            log.AddTraffic(new TrafficEvent(
                TrafficDirection.Tx, [(byte)i], DateTimeOffset.Now));
        }

        Assert.Equal(10, log.VisibleLines.Count);
        Assert.Contains("10 라인", log.SummaryText, StringComparison.Ordinal);
    }
}
