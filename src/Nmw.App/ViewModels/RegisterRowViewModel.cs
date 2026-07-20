using CommunityToolkit.Mvvm.ComponentModel;

namespace Nmw.App.ViewModels;

/// <summary>폴 그리드의 한 행. 스냅샷마다 재생성하지 않고 값 문자열만 갱신한다.</summary>
public sealed partial class RegisterRowViewModel : ObservableObject
{
    /// <summary>이 행의 프로토콜 주소(0-base).</summary>
    public ushort ProtocolAddress { get; init; }

    /// <summary>표시 주소 문자열 (base 토글에 따라 갱신).</summary>
    [ObservableProperty]
    private string address = "";

    /// <summary>별칭 (사용자 편집 가능).</summary>
    [ObservableProperty]
    private string alias = "";

    /// <summary>표시 값 문자열.</summary>
    [ObservableProperty]
    private string value = "";

    /// <summary>이 행의 마지막 raw 레지스터 값 (쓰기 프리필용, 레지스터 폴에서만).</summary>
    public ushort RawRegister { get; set; }

    /// <summary>이 행의 마지막 raw 비트 값 (코일/접점 폴에서만).</summary>
    public bool RawBit { get; set; }
}
