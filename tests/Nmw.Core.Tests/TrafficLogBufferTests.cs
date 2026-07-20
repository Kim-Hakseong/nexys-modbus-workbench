using Nmw.Core.Client;

namespace Nmw.Core.Tests;

/// <summary>트래픽 로그 링버퍼 테스트 (M8 DoD).</summary>
public sealed class TrafficLogBufferTests
{
    private static TrafficLogEntry Entry(TrafficLogKind kind, string text = "01 03") =>
        new(new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero), kind, text);

    [Fact]
    public void Add_OverCapacity_DropsOldest()
    {
        var buffer = new TrafficLogBuffer(capacity: 5);
        for (var i = 0; i < 8; i++)
        {
            buffer.Add(Entry(TrafficLogKind.Tx, $"frame {i}"));
        }

        Assert.Equal(5, buffer.Count);
        var snapshot = buffer.Snapshot();
        Assert.Equal("frame 3", snapshot[0].Text);
        Assert.Equal("frame 7", snapshot[^1].Text);
    }

    [Fact]
    public void Add_ReturnsDroppedEntry()
    {
        var buffer = new TrafficLogBuffer(capacity: 1);
        Assert.Null(buffer.Add(Entry(TrafficLogKind.Tx, "first")));
        var dropped = buffer.Add(Entry(TrafficLogKind.Tx, "second"));
        Assert.Equal("first", dropped!.Text);
    }

    [Fact]
    public void ErrorCount_TracksErrorsIncludingOverflowDrop()
    {
        var buffer = new TrafficLogBuffer(capacity: 3);
        buffer.Add(Entry(TrafficLogKind.Error, "Timeout"));
        buffer.Add(Entry(TrafficLogKind.Tx));
        buffer.Add(Entry(TrafficLogKind.Rx));
        Assert.Equal(1, buffer.ErrorCount);

        // 오류 엔트리가 링에서 밀려나면 카운트 감소
        buffer.Add(Entry(TrafficLogKind.Tx));
        Assert.Equal(0, buffer.ErrorCount);
    }

    [Fact]
    public void Snapshot_ErrorsOnly_FiltersNonErrors()
    {
        var buffer = new TrafficLogBuffer();
        buffer.Add(Entry(TrafficLogKind.Tx));
        buffer.Add(Entry(TrafficLogKind.Error, "CRC Mismatch"));
        buffer.Add(Entry(TrafficLogKind.Rx));

        var errors = buffer.Snapshot(errorsOnly: true);
        Assert.Single(errors);
        Assert.Equal("CRC Mismatch", errors[0].Text);
    }

    [Fact]
    public void Clear_ResetsCountAndErrors()
    {
        var buffer = new TrafficLogBuffer();
        buffer.Add(Entry(TrafficLogKind.Error));
        buffer.Add(Entry(TrafficLogKind.Tx));
        buffer.Clear();

        Assert.Equal(0, buffer.Count);
        Assert.Equal(0, buffer.ErrorCount);
        Assert.Empty(buffer.Snapshot());
    }

    [Fact]
    public void WriteTo_WritesFormattedLines()
    {
        var buffer = new TrafficLogBuffer();
        buffer.Add(Entry(TrafficLogKind.Tx, "00 01 00 00 00 06 11 03 00 6B 00 03"));
        buffer.Add(Entry(TrafficLogKind.Error, "Timeout"));

        using var writer = new StringWriter();
        buffer.WriteTo(writer);
        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Contains("TX  00 01 00 00 00 06 11 03 00 6B 00 03", lines[0], StringComparison.Ordinal);
        Assert.Contains("ERR Timeout", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("12:00:00.000", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ToHexString_FormatsUpperHexWithSpaces()
    {
        Assert.Equal("01 83 02 C0 F1", TrafficLogBuffer.ToHexString(TestHex.Bytes("01 83 02 C0 F1")));
        Assert.Equal("", TrafficLogBuffer.ToHexString(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void InvalidCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrafficLogBuffer(0));
    }
}
