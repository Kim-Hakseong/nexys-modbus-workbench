using Nmw.Core.Data;
using Nmw.Core.Workspace;

namespace Nmw.Core.Tests;

/// <summary>워크스페이스(.nmw) 직렬화 라운드트립 테스트 (M9 DoD).</summary>
public sealed class WorkspaceSerializerTests
{
    private static WorkspaceDocument SampleDocument() => new()
    {
        Channels =
        [
            new ChannelConfig
            {
                Id = "ch1",
                Type = ChannelType.Tcp,
                Host = "192.168.0.10",
                Port = 502,
                TimeoutMs = 1000,
                Retries = 1,
            },
        ],
        Polls =
        [
            new PollConfig
            {
                ChannelId = "ch1",
                Name = "보일러 온도",
                UnitId = 1,
                Function = 3,
                StartAddress = 0,
                Quantity = 10,
                ScanRateMs = 500,
                Format = RegisterFormat.Float32,
                WordOrder = WordOrder.CDAB,
                Aliases = new Dictionary<ushort, string> { [0] = "급수온도", [2] = "출구온도" },
            },
        ],
        Ui = new UiConfig { AddressBase = AddressBase.OneBase },
    };

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var json = WorkspaceSerializer.Serialize(SampleDocument());
        var loaded = WorkspaceSerializer.Deserialize(json);

        Assert.Equal(1, loaded.SchemaVersion);
        var channel = Assert.Single(loaded.Channels);
        Assert.Equal("ch1", channel.Id);
        Assert.Equal(ChannelType.Tcp, channel.Type);
        Assert.Equal("192.168.0.10", channel.Host);
        Assert.Equal(502, channel.Port);

        var poll = Assert.Single(loaded.Polls);
        Assert.Equal("보일러 온도", poll.Name);
        Assert.Equal(3, poll.Function);
        Assert.Equal(RegisterFormat.Float32, poll.Format);
        Assert.Equal(WordOrder.CDAB, poll.WordOrder);
        Assert.Equal("급수온도", poll.Aliases[0]);
        Assert.Equal("출구온도", poll.Aliases[2]);

        Assert.Equal(AddressBase.OneBase, loaded.Ui.AddressBase);
    }

    [Fact]
    public void Serialize_MatchesDesignStyle()
    {
        var json = WorkspaceSerializer.Serialize(SampleDocument());

        // DESIGN §8 스타일: camelCase 키, enum 문자열, function 숫자, 별칭 키 문자열
        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"Tcp\"", json, StringComparison.Ordinal);
        Assert.Contains("\"function\": 3", json, StringComparison.Ordinal);
        Assert.Contains("\"format\": \"Float32\"", json, StringComparison.Ordinal);
        Assert.Contains("\"wordOrder\": \"CDAB\"", json, StringComparison.Ordinal);
        Assert.Contains("\"addressBase\": \"OneBase\"", json, StringComparison.Ordinal);
        Assert.Contains("\"0\": \"급수온도\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_DesignExampleJson()
    {
        // DESIGN.md §8 예시 (수정 금지)
        const string json = """
        {
          "schemaVersion": 1,
          "channels": [
            { "id": "ch1", "type": "Tcp", "host": "192.168.0.10", "port": 502,
              "timeoutMs": 1000, "retries": 1, "interFrameDelayMs": 0 }
          ],
          "polls": [
            { "channelId": "ch1", "name": "보일러 온도", "unitId": 1, "function": 3,
              "startAddress": 0, "quantity": 10, "scanRateMs": 500,
              "format": "Float32", "wordOrder": "CDAB",
              "aliases": { "0": "급수온도", "2": "출구온도" } }
          ],
          "ui": { "addressBase": "OneBase" }
        }
        """;

        var document = WorkspaceSerializer.Deserialize(json);

        Assert.Equal("ch1", document.Channels[0].Id);
        Assert.Equal(0, document.Channels[0].InterFrameDelayMs);
        Assert.Equal(10, document.Polls[0].Quantity);
        Assert.Equal(500, document.Polls[0].ScanRateMs);
        Assert.Equal("급수온도", document.Polls[0].Aliases[0]);
        Assert.Equal(AddressBase.OneBase, document.Ui.AddressBase);
    }

    [Fact]
    public void Deserialize_UnsupportedSchemaVersion_ThrowsClearError()
    {
        var ex = Assert.Throws<WorkspaceFormatException>(
            () => WorkspaceSerializer.Deserialize("""{ "schemaVersion": 99 }"""));
        Assert.Contains("스키마 버전", ex.Message, StringComparison.Ordinal);
        Assert.Contains("99", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ broken json")]
    [InlineData("""{ "schemaVersion": 1, "polls": [ { "quantity": "abc" } ] }""")]
    [InlineData("null")]
    public void Deserialize_Corrupted_FailsWholesale(string json)
    {
        Assert.Throws<WorkspaceFormatException>(() => WorkspaceSerializer.Deserialize(json));
    }

    [Fact]
    public void RoundTrip_SerialChannel()
    {
        var document = new WorkspaceDocument
        {
            Channels =
            [
                new ChannelConfig
                {
                    Id = "ch1",
                    Type = ChannelType.Rtu,
                    PortName = "COM3",
                    BaudRate = 19200,
                    Parity = System.IO.Ports.Parity.Even,
                    DataBits = 8,
                    StopBits = System.IO.Ports.StopBits.Two,
                    InterFrameDelayMs = 5,
                },
            ],
        };

        var loaded = WorkspaceSerializer.Deserialize(WorkspaceSerializer.Serialize(document));
        var channel = loaded.Channels[0];

        Assert.Equal(ChannelType.Rtu, channel.Type);
        Assert.Equal("COM3", channel.PortName);
        Assert.Equal(19200, channel.BaudRate);
        Assert.Equal(System.IO.Ports.Parity.Even, channel.Parity);
        Assert.Equal(System.IO.Ports.StopBits.Two, channel.StopBits);
        Assert.Equal(5, channel.InterFrameDelayMs);
    }
}
