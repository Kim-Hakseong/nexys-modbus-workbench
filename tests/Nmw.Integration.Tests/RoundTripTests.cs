using Nmw.Core.Client;
using Nmw.Core.Protocol;
using Nmw.Core.Transport;

namespace Nmw.Integration.Tests;

/// <summary>DESIGN.md §7.6 통합 테스트 시나리오 (TestSlave 상대 실제 TCP 왕복).</summary>
public sealed class RoundTripTests : IAsyncLifetime
{
    private TestSlave.TestSlave _slave = null!;
    private ModbusMaster _master = null!;

    public async Task InitializeAsync()
    {
        _slave = new TestSlave.TestSlave();
        _slave.Start();
        var transport = new TcpTransport(new TcpTransportSettings("127.0.0.1", _slave.Port));
        await transport.ConnectAsync(CancellationToken.None);
        _master = new ModbusMaster(transport, new ModbusMasterOptions { TimeoutMs = 1000, Retries = 1 });
    }

    public async Task DisposeAsync()
    {
        await _master.DisposeAsync();
        await _slave.DisposeAsync();
    }

    [Fact]
    public async Task Scenario1_ReadHoldingRegisters_MatchesSetValues()
    {
        for (var i = 0; i < 10; i++)
        {
            _slave.HoldingRegisters[i] = (ushort)(i * 10);
        }

        var result = await _master.ReadHoldingRegistersAsync(1, 0, 10);

        Assert.True(result.IsSuccess, result.Error?.Text);
        Assert.Equal(new ushort[] { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90 }, result.Value);
    }

    [Fact]
    public async Task Scenario2_WriteSingleRegister_ThenReadBack()
    {
        var write = await _master.WriteSingleRegisterAsync(1, 5, 0xBEEF);
        Assert.True(write.IsSuccess, write.Error?.Text);

        var read = await _master.ReadHoldingRegistersAsync(1, 5, 1);
        Assert.True(read.IsSuccess, read.Error?.Text);
        Assert.Equal(new ushort[] { 0xBEEF }, read.Value);
    }

    [Fact]
    public async Task Scenario3_WriteMultipleRegisters_ThenReadBack()
    {
        var write = await _master.WriteMultipleRegistersAsync(1, 0, new ushort[] { 1, 2, 3 });
        Assert.True(write.IsSuccess, write.Error?.Text);

        var read = await _master.ReadHoldingRegistersAsync(1, 0, 3);
        Assert.True(read.IsSuccess, read.Error?.Text);
        Assert.Equal(new ushort[] { 1, 2, 3 }, read.Value);
    }

    [Fact]
    public async Task Scenario4_WriteSingleCoilOn_ThenReadBack()
    {
        var write = await _master.WriteSingleCoilAsync(1, 3, on: true);
        Assert.True(write.IsSuccess, write.Error?.Text);

        var read = await _master.ReadCoilsAsync(1, 3, 1);
        Assert.True(read.IsSuccess, read.Error?.Text);
        Assert.True(read.Value[0]);
    }

    [Fact]
    public async Task Scenario5_WriteMultipleCoils_ThenReadBack()
    {
        var values = new[] { true, false, true, true };
        var write = await _master.WriteMultipleCoilsAsync(1, 0, values);
        Assert.True(write.IsSuccess, write.Error?.Text);

        var read = await _master.ReadCoilsAsync(1, 0, 4);
        Assert.True(read.IsSuccess, read.Error?.Text);
        Assert.Equal(values, read.Value);
    }

    [Fact]
    public async Task Scenario6_ReadUnmappedAddress_ReturnsIllegalDataAddress()
    {
        var result = await _master.ReadHoldingRegistersAsync(1, 500, 10);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModbusErrorKind.Exception, result.Error!.Kind);
        Assert.Equal(ModbusExceptionCode.IllegalDataAddress, result.Error.ExceptionCode);
        Assert.Equal("Illegal Data Address", result.Error.Text);
    }

    [Fact]
    public async Task Scenario7_DelayedResponse_TimesOutAndRetries()
    {
        await using var slave = new TestSlave.TestSlave();
        slave.Start();
        slave.ResponseDelay = TimeSpan.FromMilliseconds(500);
        var transport = new TcpTransport(new TcpTransportSettings("127.0.0.1", slave.Port));
        await transport.ConnectAsync(CancellationToken.None);
        await using var master = new ModbusMaster(
            transport, new ModbusMasterOptions { TimeoutMs = 100, Retries = 1 });

        var result = await master.ReadHoldingRegistersAsync(1, 0, 1);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModbusErrorKind.Timeout, result.Error!.Kind);
        Assert.Equal("Timeout", result.Error.Text);

        // 재시도 1회 → 총 2회 요청이 슬레이브에 도달해야 한다.
        // 슬레이브는 첫 요청의 지연 응답이 끝나야 두 번째 요청을 읽으므로 잠시 대기한다.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (slave.RequestCount < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Equal(2, slave.RequestCount);
    }

    [Fact]
    public async Task Scenario8_WrongTransactionIdInjected_DiscardedAndCorrectAccepted()
    {
        _slave.HoldingRegisters[0] = 0x1234;
        _slave.InjectWrongTransactionIdOnce = true;

        var result = await _master.ReadHoldingRegistersAsync(1, 0, 1);

        Assert.True(result.IsSuccess, result.Error?.Text);
        Assert.Equal(new ushort[] { 0x1234 }, result.Value);
    }

    [Fact]
    public async Task ReadDiscreteInputsAndInputRegisters_RoundTrip()
    {
        _slave.DiscreteInputs[2] = true;
        _slave.InputRegisters[7] = 0x0AB0;

        var bits = await _master.ReadDiscreteInputsAsync(1, 0, 4);
        Assert.True(bits.IsSuccess, bits.Error?.Text);
        Assert.Equal(new[] { false, false, true, false }, bits.Value);

        var regs = await _master.ReadInputRegistersAsync(1, 7, 1);
        Assert.True(regs.IsSuccess, regs.Error?.Text);
        Assert.Equal(new ushort[] { 0x0AB0 }, regs.Value);
    }

    [Fact]
    public async Task SequentialMixedOperations_AllSucceed()
    {
        for (var i = 0; i < 20; i++)
        {
            var write = await _master.WriteSingleRegisterAsync(1, (ushort)(i % 10), (ushort)i);
            Assert.True(write.IsSuccess, write.Error?.Text);
            var read = await _master.ReadHoldingRegistersAsync(1, (ushort)(i % 10), 1);
            Assert.True(read.IsSuccess, read.Error?.Text);
            Assert.Equal((ushort)i, read.Value[0]);
        }
    }
}
