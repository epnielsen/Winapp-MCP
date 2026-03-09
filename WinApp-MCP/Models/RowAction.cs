namespace WinAppMCP.Models;

/// <summary>
/// Metadata for an actionable child control within a list row.
/// </summary>
public sealed record RowAction(string Name, string AutomationId, string ControlType);
