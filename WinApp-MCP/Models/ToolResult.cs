using System.Runtime.InteropServices;
using System.Text;

namespace WinAppMCP.Models;

/// <summary>
/// Structured result from an MCP tool operation. Provides consistent
/// error categorization and diagnostic detail for agents.
/// </summary>
public sealed class ToolResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public ErrorCategory? Error { get; init; }
    public string? Details { get; init; }

    public static ToolResult Ok(string message) =>
        new() { Success = true, Message = message };

    public static ToolResult Fail(ErrorCategory category, string message, string? details = null) =>
        new() { Success = false, Message = message, Error = category, Details = details };

    /// <summary>
    /// Classify an exception into an <see cref="ErrorCategory"/>.
    /// </summary>
    public static ErrorCategory Classify(Exception ex) => ex switch
    {
        COMException com when com.HResult == unchecked((int)0x8000FFFF) => ErrorCategory.TransientProviderFailure,
        COMException com when com.HResult == unchecked((int)0x80040201) => ErrorCategory.PropertyNotSupported,
        COMException => ErrorCategory.TransientProviderFailure,
        FlaUI.Core.Exceptions.PropertyNotSupportedException => ErrorCategory.PropertyNotSupported,
        InvalidOperationException => ErrorCategory.OperationNotSupported,
        _ => ErrorCategory.Unknown
    };

    public string ToMcpString()
    {
        if (Success)
            return Message;

        var sb = new StringBuilder();
        sb.Append("Error");
        if (Error.HasValue)
            sb.Append($" [{Error.Value}]");
        sb.Append($": {Message}");
        if (!string.IsNullOrEmpty(Details))
            sb.Append($"\nDetails: {Details}");
        return sb.ToString();
    }
}
