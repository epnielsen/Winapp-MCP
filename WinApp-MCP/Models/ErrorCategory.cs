namespace WinAppMCP.Models;

/// <summary>
/// Categorizes errors returned by MCP tools so agents can distinguish
/// between transient provider failures, missing elements, and unsupported operations.
/// </summary>
public enum ErrorCategory
{
    /// <summary>The target element was not found in the UI tree.</summary>
    ElementNotFound,

    /// <summary>A UIA property was not supported by the provider.</summary>
    PropertyNotSupported,

    /// <summary>A transient COM or provider failure (e.g. E_UNEXPECTED). May succeed on retry.</summary>
    TransientProviderFailure,

    /// <summary>The requested operation or pattern is not supported by the element.</summary>
    OperationNotSupported,

    /// <summary>An unclassified error.</summary>
    Unknown
}
