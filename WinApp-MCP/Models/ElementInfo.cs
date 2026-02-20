using System.Drawing;
using FlaUI.Core.AutomationElements;

namespace WinAppMCP.Models;

/// <summary>
/// Compact DTO representing a UI element's key properties.
/// Designed for token-efficient serialization in MCP responses.
/// </summary>
public sealed class ElementInfo
{
    public string AutomationId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ControlType { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public bool IsOffscreen { get; init; }
    public BoundsInfo? Bounds { get; init; }
    public string[] SupportedPatterns { get; init; } = [];

    public static ElementInfo FromElement(AutomationElement element)
    {
        BoundsInfo? bounds = null;
        try
        {
            var rect = element.BoundingRectangle;
            bounds = new BoundsInfo(
                (int)rect.X,
                (int)rect.Y,
                (int)rect.Width,
                (int)rect.Height);
        }
        catch
        {
            // BoundingRectangle can throw for off-screen or collapsed elements
        }

        string[] patterns = [];
        try
        {
            var supported = element.GetSupportedPatterns();
            patterns = supported.Select(p => p.Name).ToArray();
        }
        catch
        {
            // Pattern enumeration can fail for some elements
        }

        return new ElementInfo
        {
            AutomationId = element.AutomationId ?? string.Empty,
            Name = element.Name ?? string.Empty,
            ControlType = element.ControlType.ToString(),
            ClassName = element.ClassName ?? string.Empty,
            IsEnabled = element.IsEnabled,
            IsOffscreen = element.IsOffscreen,
            Bounds = bounds,
            SupportedPatterns = patterns
        };
    }

    /// <summary>
    /// Single-line compact representation for tree output.
    /// Format: [ControlType] "Name" id="AutomationId" class="ClassName"
    /// </summary>
    public string ToCompactString()
    {
        var parts = new List<string> { $"[{ControlType}]" };

        if (!string.IsNullOrEmpty(Name))
            parts.Add($"\"{Name}\"");

        if (!string.IsNullOrEmpty(AutomationId))
            parts.Add($"id=\"{AutomationId}\"");

        if (!string.IsNullOrEmpty(ClassName))
            parts.Add($"class=\"{ClassName}\"");

        if (!IsEnabled)
            parts.Add("(disabled)");

        return string.Join(" ", parts);
    }
}

public sealed record BoundsInfo(int X, int Y, int Width, int Height);
