using System.Text;

namespace Nmw.Core.Client;

/// <summary>트래픽 로그 엔트리 종류.</summary>
public enum TrafficLogKind
{
    /// <summary>송신 프레임.</summary>
    Tx,

    /// <summary>수신 프레임.</summary>
    Rx,

    /// <summary>통신 오류.</summary>
    Error,
}

/// <summary>트래픽 로그 한 줄.</summary>
/// <param name="At">발생 시각.</param>
/// <param name="Kind">종류.</param>
/// <param name="Text">hex 프레임 또는 오류 메시지.</param>
public sealed record TrafficLogEntry(DateTimeOffset At, TrafficLogKind Kind, string Text)
{
    /// <summary>로그 파일/뷰에 쓰는 한 줄 문자열.</summary>
    public string Line =>
        $"{At:HH:mm:ss.fff} {Kind switch { TrafficLogKind.Tx => "TX ", TrafficLogKind.Rx => "RX ", _ => "ERR" }} {Text}";
}

/// <summary>
/// 고정 용량 링버퍼 트래픽 로그. 용량 초과 시 가장 오래된 엔트리를 버린다
/// (24시간 연속 폴링 메모리 누수 방지, 기본 5000라인).
/// </summary>
public sealed class TrafficLogBuffer
{
    /// <summary>기본 최대 라인 수.</summary>
    public const int DefaultCapacity = 5000;

    private readonly object _gate = new();
    private readonly Queue<TrafficLogEntry> _entries;
    private int _errorCount;

    /// <summary>지정 용량의 버퍼를 만든다.</summary>
    /// <param name="capacity">최대 라인 수 (1 이상).</param>
    public TrafficLogBuffer(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "용량은 1 이상이어야 합니다.");
        }

        Capacity = capacity;
        _entries = new Queue<TrafficLogEntry>(capacity);
    }

    /// <summary>최대 라인 수.</summary>
    public int Capacity { get; }

    /// <summary>현재 라인 수.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>버퍼에 남아 있는 오류 엔트리 수.</summary>
    public int ErrorCount
    {
        get
        {
            lock (_gate)
            {
                return _errorCount;
            }
        }
    }

    /// <summary>바이트열을 "01 03 00 0A" 형식 hex 문자열로 변환한다.</summary>
    /// <param name="data">바이트열.</param>
    public static string ToHexString(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(data.Length * 3);
        for (var i = 0; i < data.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(data[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>엔트리를 추가한다. 용량 초과 시 가장 오래된 엔트리를 버리고 반환한다.</summary>
    /// <param name="entry">추가할 엔트리.</param>
    /// <returns>버려진 엔트리 (없으면 null).</returns>
    public TrafficLogEntry? Add(TrafficLogEntry entry)
    {
        lock (_gate)
        {
            TrafficLogEntry? dropped = null;
            if (_entries.Count >= Capacity)
            {
                dropped = _entries.Dequeue();
                if (dropped.Kind == TrafficLogKind.Error)
                {
                    _errorCount--;
                }
            }

            _entries.Enqueue(entry);
            if (entry.Kind == TrafficLogKind.Error)
            {
                _errorCount++;
            }

            return dropped;
        }
    }

    /// <summary>현재 엔트리의 스냅샷을 반환한다.</summary>
    /// <param name="errorsOnly">true면 오류 엔트리만.</param>
    public IReadOnlyList<TrafficLogEntry> Snapshot(bool errorsOnly = false)
    {
        lock (_gate)
        {
            return errorsOnly
                ? _entries.Where(e => e.Kind == TrafficLogKind.Error).ToList()
                : [.. _entries];
        }
    }

    /// <summary>모든 엔트리를 지운다.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _errorCount = 0;
        }
    }

    /// <summary>엔트리를 텍스트로 기록한다 (.txt 저장).</summary>
    /// <param name="writer">출력 대상.</param>
    /// <param name="errorsOnly">true면 오류 엔트리만.</param>
    public void WriteTo(TextWriter writer, bool errorsOnly = false)
    {
        foreach (var entry in Snapshot(errorsOnly))
        {
            writer.WriteLine(entry.Line);
        }
    }
}
