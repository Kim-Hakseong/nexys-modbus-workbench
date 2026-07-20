namespace Nmw.Core.Tests;

/// <summary>테스트용 hex 문자열 헬퍼.</summary>
internal static class TestHex
{
    /// <summary>"01 03 00 0A" 형식의 hex 문자열을 바이트 배열로 변환한다.</summary>
    public static byte[] Bytes(string spacedHex) =>
        Convert.FromHexString(spacedHex.Replace(" ", "", StringComparison.Ordinal));
}
