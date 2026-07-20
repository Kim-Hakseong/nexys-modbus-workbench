using Avalonia.Headless.XUnit;
using Nmw.App.Models;
using Nmw.App.ViewModels;
using Nmw.App.Views;
using Xunit;

namespace Nmw.App.Tests;

/// <summary>M7 스모크: 쓰기 왕복 + 주소 base 토글 + 별칭.</summary>
public sealed class WriteAndAddressSmokeTests
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

    private static async Task<(Integration.Tests.TestSlave.TestSlave Slave, MainWindowViewModel Vm, MainWindow Window)>
        SetupConnectedAsync()
    {
        var slave = new Integration.Tests.TestSlave.TestSlave();
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
        Assert.True(viewModel.IsConnected);
        return (slave, viewModel, window);
    }

    [AvaloniaFact]
    public async Task WriteRoundTrip_ValueAppearsInGrid()
    {
        var (slave, viewModel, window) = await SetupConnectedAsync();
        await using var _ = slave;

        var poll = viewModel.Polls[0];
        poll.ScanRateText = "50";
        await poll.TogglePollCommand.ExecuteAsync(null);
        Assert.True(poll.IsPolling);

        // 마스터로 FC06 쓰기 (쓰기 다이얼로그의 전송 경로와 동일 API)
        var write = await viewModel.Master!.WriteSingleRegisterAsync(1, 5, 0xBEEF);
        Assert.True(write.IsSuccess, write.Error?.Text);

        // 폴 그리드에 반영 대기 (U16 → 48879)
        await WaitUntilAsync(() => poll.Rows[5].Value == "48879");
        Assert.Equal(0xBEEF, poll.Rows[5].RawRegister);

        await viewModel.DisconnectCommand.ExecuteAsync(null);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WriteMultiple_ThenExceptionShown_ForBadAddress()
    {
        var (slave, viewModel, window) = await SetupConnectedAsync();
        await using var _ = slave;

        var ok = await viewModel.Master!.WriteMultipleRegistersAsync(1, 0, new ushort[] { 1, 2, 3 });
        Assert.True(ok.IsSuccess, ok.Error?.Text);
        Assert.Equal(1, slave.HoldingRegisters[0]);
        Assert.Equal(3, slave.HoldingRegisters[2]);

        var bad = await viewModel.Master!.WriteSingleRegisterAsync(1, 500, 1);
        Assert.False(bad.IsSuccess);
        Assert.Equal("Illegal Data Address", bad.Error!.Text);

        await viewModel.DisconnectCommand.ExecuteAsync(null);
        window.Close();
    }

    [AvaloniaFact]
    public async Task AddressBaseToggle_ReformatsRowAddresses()
    {
        var (slave, viewModel, window) = await SetupConnectedAsync();
        await using var _ = slave;

        var poll = viewModel.Polls[0];
        poll.StartAddressText = "0";
        poll.QuantityText = "3";
        poll.ScanRateText = "100";
        await poll.TogglePollCommand.ExecuteAsync(null);
        Assert.Equal("0", poll.Rows[0].Address);
        Assert.Equal("2", poll.Rows[2].Address);

        viewModel.OneBasedAddress = true;
        Assert.Equal("40001", poll.Rows[0].Address);
        Assert.Equal("40003", poll.Rows[2].Address);

        viewModel.OneBasedAddress = false;
        Assert.Equal("0", poll.Rows[0].Address);

        await viewModel.DisconnectCommand.ExecuteAsync(null);
        window.Close();
    }

    [AvaloniaFact]
    public async Task OneBaseInput_ParsedAsProtocolAddress()
    {
        var (slave, viewModel, window) = await SetupConnectedAsync();
        await using var _ = slave;
        slave.HoldingRegisters[9] = 777;

        viewModel.OneBasedAddress = true;
        var poll = viewModel.Polls[0];
        poll.StartAddressText = "40010"; // 프로토콜 주소 9
        poll.QuantityText = "1";
        poll.ScanRateText = "50";
        await poll.TogglePollCommand.ExecuteAsync(null);
        Assert.True(poll.IsPolling, poll.LastError);

        await WaitUntilAsync(() => poll.Rows[0].Value == "777");
        Assert.Equal(9, poll.Rows[0].ProtocolAddress);

        await viewModel.DisconnectCommand.ExecuteAsync(null);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Alias_EditPersistsAcrossPollRestart()
    {
        var (slave, viewModel, window) = await SetupConnectedAsync();
        await using var _ = slave;

        var poll = viewModel.Polls[0];
        poll.QuantityText = "3";
        poll.ScanRateText = "100";
        await poll.TogglePollCommand.ExecuteAsync(null);

        poll.Rows[1].Alias = "보일러 온도";

        // 폴 재시작 후에도 별칭 유지
        await poll.TogglePollCommand.ExecuteAsync(null); // 정지
        await poll.TogglePollCommand.ExecuteAsync(null); // 재시작
        Assert.Equal("보일러 온도", poll.Rows[1].Alias);

        await viewModel.DisconnectCommand.ExecuteAsync(null);
        window.Close();
    }
}
