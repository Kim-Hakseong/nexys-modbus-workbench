using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nmw.Core.Workspace;

/// <summary>워크스페이스 파일이 손상되었거나 지원하지 않는 형식일 때 발생.</summary>
public sealed class WorkspaceFormatException : Exception
{
    /// <summary>메시지로 예외를 만든다.</summary>
    /// <param name="message">사용자에게 표시할 메시지.</param>
    public WorkspaceFormatException(string message)
        : base(message)
    {
    }

    /// <summary>메시지와 내부 예외로 예외를 만든다.</summary>
    /// <param name="message">사용자에게 표시할 메시지.</param>
    /// <param name="innerException">원인 예외.</param>
    public WorkspaceFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// .nmw 워크스페이스 JSON 직렬화. 부분 손상 시 통째로 실패한다 (부분 로드 금지).
/// enum은 문자열("Float32", "CDAB", "OneBase"), function code는 숫자로 저장한다.
/// </summary>
public static class WorkspaceSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        // 한국어 별칭/이름을 \uXXXX 이스케이프 없이 실제 한글로 저장한다.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>워크스페이스를 JSON 문자열로 직렬화한다.</summary>
    /// <param name="document">워크스페이스 문서.</param>
    public static string Serialize(WorkspaceDocument document) =>
        JsonSerializer.Serialize(document, Options);

    /// <summary>JSON 문자열에서 워크스페이스를 역직렬화한다.</summary>
    /// <param name="json">.nmw 파일 내용.</param>
    /// <returns>워크스페이스 문서.</returns>
    /// <exception cref="WorkspaceFormatException">손상되었거나 지원하지 않는 형식.</exception>
    public static WorkspaceDocument Deserialize(string json)
    {
        WorkspaceDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<WorkspaceDocument>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new WorkspaceFormatException($"워크스페이스 파일이 손상되었습니다: {ex.Message}", ex);
        }

        if (document is null)
        {
            throw new WorkspaceFormatException("워크스페이스 파일이 비어 있습니다.");
        }

        if (document.SchemaVersion != WorkspaceDocument.CurrentSchemaVersion)
        {
            throw new WorkspaceFormatException(
                $"지원하지 않는 워크스페이스 스키마 버전입니다: {document.SchemaVersion} " +
                $"(지원: {WorkspaceDocument.CurrentSchemaVersion})");
        }

        return document;
    }
}
