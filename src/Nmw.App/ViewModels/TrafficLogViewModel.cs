using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nmw.Core.Client;

namespace Nmw.App.ViewModels;

/// <summary>트래픽 로그 뷰모델: 링버퍼 미러 + 에러만 필터 + 클리어 + 저장.</summary>
public sealed partial class TrafficLogViewModel : ObservableObject
{
    private readonly TrafficLogBuffer _buffer;

    [ObservableProperty]
    private bool errorsOnly;

    [ObservableProperty]
    private string summaryText = "0 라인 | Err 0";

    /// <summary>기본 용량(5000라인)의 로그 뷰모델을 만든다.</summary>
    public TrafficLogViewModel()
        : this(TrafficLogBuffer.DefaultCapacity)
    {
    }

    /// <summary>지정 용량의 로그 뷰모델을 만든다 (테스트용).</summary>
    /// <param name="capacity">최대 라인 수.</param>
    public TrafficLogViewModel(int capacity)
    {
        _buffer = new TrafficLogBuffer(capacity);
    }

    /// <summary>현재 필터 기준으로 보이는 로그 라인들 (UI 스레드에서만 갱신).</summary>
    public ObservableCollection<string> VisibleLines { get; } = [];

    /// <summary>TX/RX 트래픽 이벤트를 추가한다 (UI 스레드에서 호출).</summary>
    /// <param name="trafficEvent">마스터가 발행한 트래픽 이벤트.</param>
    public void AddTraffic(TrafficEvent trafficEvent) =>
        Append(new TrafficLogEntry(
            trafficEvent.Timestamp,
            trafficEvent.Direction == TrafficDirection.Tx ? TrafficLogKind.Tx : TrafficLogKind.Rx,
            TrafficLogBuffer.ToHexString(trafficEvent.Data)));

    /// <summary>통신 오류 라인을 추가한다 (UI 스레드에서 호출).</summary>
    /// <param name="text">오류 명칭.</param>
    /// <param name="at">발생 시각.</param>
    public void AddError(string text, DateTimeOffset at) =>
        Append(new TrafficLogEntry(at, TrafficLogKind.Error, text));

    /// <summary>로그를 모두 지운다.</summary>
    [RelayCommand]
    public void Clear()
    {
        _buffer.Clear();
        VisibleLines.Clear();
        UpdateSummary();
    }

    /// <summary>현재 필터 기준의 로그를 스트림에 저장한다 (.txt).</summary>
    /// <param name="stream">출력 스트림.</param>
    public async Task SaveAsync(Stream stream)
    {
        await using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        _buffer.WriteTo(writer, ErrorsOnly);
        await writer.FlushAsync();
    }

    partial void OnErrorsOnlyChanged(bool value) => RebuildVisible();

    private void Append(TrafficLogEntry entry)
    {
        var dropped = _buffer.Add(entry);
        var matchesFilter = !ErrorsOnly || entry.Kind == TrafficLogKind.Error;
        if (matchesFilter)
        {
            VisibleLines.Add(entry.Line);
        }

        // 버퍼에서 밀려난 라인이 화면에도 있으면 제거 (맨 앞)
        if (dropped is not null && VisibleLines.Count > 0 &&
            (!ErrorsOnly || dropped.Kind == TrafficLogKind.Error) &&
            VisibleLines[0] == dropped.Line)
        {
            VisibleLines.RemoveAt(0);
        }

        UpdateSummary();
    }

    private void RebuildVisible()
    {
        VisibleLines.Clear();
        foreach (var entry in _buffer.Snapshot(ErrorsOnly))
        {
            VisibleLines.Add(entry.Line);
        }

        UpdateSummary();
    }

    private void UpdateSummary() =>
        SummaryText = $"{_buffer.Count} 라인 | Err {_buffer.ErrorCount}";
}
