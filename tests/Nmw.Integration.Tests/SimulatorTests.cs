using Nmw.Core.Client;
using Nmw.Core.Protocol;
using Nmw.Core.Simulator;
using Nmw.Core.Transport;

namespace Nmw.Integration.Tests;

/// <summary>내장 슬레이브 시뮬레이터 통합 테스트 (실제 마스터 왕복).</summary>
public sealed class SimulatorTests
{
    private static async Task<ModbusMaster> ConnectAsync(int port)
    {
        var transport = new TcpTransport(new TcpTransportSettings("127.0.0.1", port));
        await transport.ConnectAsync(CancellationToken.None);
        return new ModbusMaster(transport, new ModbusMasterOptions { TimeoutMs = 1000, Retries = 1 });
    }

    [Fact]
    public async Task AllFunctionCodes_RoundTrip()
    {
        await using var simulator = new ModbusTcpSimulator(new SimulatorOptions { Port = 0 });
        simulator.Start();
        simulator.SetInputRegister(2, 0x0AB0);
        simulator.SetDiscreteInput(1, true);
        await using var master = await ConnectAsync(simulator.Port);

        // FC06 → FC03
        Assert.True((await master.WriteSingleRegisterAsync(1, 5, 0xBEEF)).IsSuccess);
        var holding = await master.ReadHoldingRegistersAsync(1, 5, 1);
        Assert.True(holding.IsSuccess, holding.Error?.Text);
        Assert.Equal(0xBEEF, holding.Value[0]);
        Assert.Equal(0xBEEF, simulator.GetHoldingRegister(5));

        // FC16 → FC03
        Assert.True((await master.WriteMultipleRegistersAsync(1, 10, new ushort[] { 1, 2, 3 })).IsSuccess);
        var multi = await master.ReadHoldingRegistersAsync(1, 10, 3);
        Assert.Equal(new ushort[] { 1, 2, 3 }, multi.Value);

        // FC05 → FC01
        Assert.True((await master.WriteSingleCoilAsync(1, 7, on: true)).IsSuccess);
        var coils = await master.ReadCoilsAsync(1, 7, 1);
        Assert.True(coils.Value[0]);

        // FC15 → FC01
        Assert.True((await master.WriteMultipleCoilsAsync(1, 20, new[] { true, false, true })).IsSuccess);
        var multiCoils = await master.ReadCoilsAsync(1, 20, 3);
        Assert.Equal(new[] { true, false, true }, multiCoils.Value);

        // FC04 / FC02 (API로 세팅한 값)
        var input = await master.ReadInputRegistersAsync(1, 2, 1);
        Assert.Equal(0x0AB0, input.Value[0]);
        var discrete = await master.ReadDiscreteInputsAsync(1, 0, 3);
        Assert.Equal(new[] { false, true, false }, discrete.Value);
    }

    [Fact]
    public async Task OutOfRangeAddress_ReturnsIllegalDataAddress()
    {
        await using var simulator = new ModbusTcpSimulator(new SimulatorOptions { Port = 0, AreaSize = 100 });
        simulator.Start();
        await using var master = await ConnectAsync(simulator.Port);

        var read = await master.ReadHoldingRegistersAsync(1, 200, 10);
        Assert.False(read.IsSuccess);
        Assert.Equal(ModbusExceptionCode.IllegalDataAddress, read.Error!.ExceptionCode);

        var write = await master.WriteSingleRegisterAsync(1, 200, 1);
        Assert.False(write.IsSuccess);
        Assert.Equal(ModbusExceptionCode.IllegalDataAddress, write.Error!.ExceptionCode);
    }

    [Fact]
    public async Task TwoClients_Concurrently()
    {
        await using var simulator = new ModbusTcpSimulator(new SimulatorOptions { Port = 0 });
        simulator.Start();
        simulator.SetHoldingRegister(0, 42);
        await using var first = await ConnectAsync(simulator.Port);
        await using var second = await ConnectAsync(simulator.Port);

        var tasks = new[]
        {
            first.ReadHoldingRegistersAsync(1, 0, 1),
            second.ReadHoldingRegistersAsync(1, 0, 1),
        };
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r =>
        {
            Assert.True(r.IsSuccess, r.Error?.Text);
            Assert.Equal(42, r.Value[0]);
        });
        Assert.True(simulator.RequestCount >= 2);
    }

    [Fact]
    public async Task DataChangedByMaster_RaisedOnWrite()
    {
        await using var simulator = new ModbusTcpSimulator(new SimulatorOptions { Port = 0 });
        simulator.Start();
        var raised = 0;
        simulator.DataChangedByMaster += (_, _) => Interlocked.Increment(ref raised);
        await using var master = await ConnectAsync(simulator.Port);

        Assert.True((await master.WriteSingleRegisterAsync(1, 0, 1)).IsSuccess);
        Assert.True((await master.ReadHoldingRegistersAsync(1, 0, 1)).IsSuccess); // 읽기는 미발생

        Assert.Equal(1, Volatile.Read(ref raised));
    }

    [Fact]
    public async Task IncrementRegisters_ChangesValues()
    {
        await using var simulator = new ModbusTcpSimulator(new SimulatorOptions { Port = 0 });
        simulator.Start();
        simulator.SetHoldingRegister(0, 100);

        simulator.IncrementRegisters(5);
        simulator.IncrementRegisters(5);

        Assert.Equal(102, simulator.GetHoldingRegister(0));
        Assert.Equal(2, simulator.GetInputRegister(4));
        Assert.Equal(0, simulator.GetHoldingRegister(5)); // 범위 밖 주소는 불변
    }

    [Fact]
    public async Task StopThenRestart_Works()
    {
        var simulator = new ModbusTcpSimulator(new SimulatorOptions { Port = 0 });
        simulator.Start();
        var firstPort = simulator.Port;
        await using (var master = await ConnectAsync(firstPort))
        {
            Assert.True((await master.ReadHoldingRegistersAsync(1, 0, 1)).IsSuccess);
        }

        await simulator.StopAsync();
        Assert.False(simulator.IsRunning);
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var probe = new System.Net.Sockets.TcpClient();
            using var cts = new CancellationTokenSource(500);
            await probe.ConnectAsync("127.0.0.1", firstPort, cts.Token);
        });

        simulator.Start();
        Assert.True(simulator.IsRunning);
        await using (var master = await ConnectAsync(simulator.Port))
        {
            Assert.True((await master.ReadHoldingRegistersAsync(1, 0, 1)).IsSuccess);
        }

        await simulator.DisposeAsync();
    }

    [Fact]
    public async Task RespondsToAnyUnitId()
    {
        await using var simulator = new ModbusTcpSimulator(new SimulatorOptions { Port = 0 });
        simulator.Start();
        simulator.SetHoldingRegister(0, 7);
        await using var master = await ConnectAsync(simulator.Port);

        foreach (byte unitId in new byte[] { 1, 17, 247 })
        {
            var result = await master.ReadHoldingRegistersAsync(unitId, 0, 1);
            Assert.True(result.IsSuccess, result.Error?.Text);
            Assert.Equal(7, result.Value[0]);
        }
    }
}
