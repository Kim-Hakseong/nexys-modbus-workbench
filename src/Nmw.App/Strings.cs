namespace Nmw.App;

/// <summary>UI 한국어 문자열 (향후 영어화 대비 상수 클래스로 분리).</summary>
public static class Strings
{
    /// <summary>앱 타이틀.</summary>
    public const string AppTitle = "Modbus Workbench";

    /// <summary>연결되지 않음 상태.</summary>
    public const string NotConnected = "연결 안 됨";

    /// <summary>연결 실패 상태.</summary>
    public const string ConnectFailed = "연결 실패";

    /// <summary>폴링 중 아님 표시.</summary>
    public const string PollStopped = "정지됨";

    /// <summary>ON 값 표시.</summary>
    public const string On = "ON";

    /// <summary>OFF 값 표시.</summary>
    public const string Off = "OFF";

    /// <summary>입력 값 오류 프리픽스.</summary>
    public const string InvalidInput = "입력 값 오류";

    /// <summary>연결 전 폴 시작 시 오류.</summary>
    public const string NotConnectedError = "먼저 채널에 연결하세요";
}
