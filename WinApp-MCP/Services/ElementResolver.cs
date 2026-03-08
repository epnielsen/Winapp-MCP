using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using Microsoft.Extensions.Logging;
using WinAppMCP.Models;

namespace WinAppMCP.Services;

/// <summary>
/// Resolves UI elements within a window using a priority cascade:
/// AutomationId > XPath > Name+ControlType.
/// Wraps lookups in a short retry for dynamically loaded UI.
/// Absorbs transient COM/provider failures with re-resolution.
/// </summary>
public sealed class ElementResolver
{
    private const int MaxTransientRetries = 3;
    private static readonly TimeSpan TransientRetryDelay = TimeSpan.FromMilliseconds(300);

    private readonly ILogger<ElementResolver> _logger;

    public ElementResolver(ILogger<ElementResolver> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Find a single element using the priority cascade.
    /// Retries on transient COM failures with element re-resolution.
    /// </summary>
    public AutomationElement? FindElement(
        AutomationElement root,
        string? automationId = null,
        string? name = null,
        string? controlType = null,
        string? xpath = null)
    {
        // Priority 1: AutomationId (most reliable)
        if (!string.IsNullOrWhiteSpace(automationId))
        {
            _logger.LogDebug("Searching by AutomationId: {Id}", automationId);
            var result = RetryFind(root, el => el.FindFirstDescendant(cf => cf.ByAutomationId(automationId)));
            if (result != null) return result;
            _logger.LogWarning("Element not found by AutomationId: {Id}", automationId);
        }

        // Priority 2: XPath
        if (!string.IsNullOrWhiteSpace(xpath))
        {
            _logger.LogDebug("Searching by XPath: {XPath}", xpath);
            var result = RetryFind(root, el => el.FindFirstByXPath(xpath));
            if (result != null) return result;
            _logger.LogWarning("Element not found by XPath: {XPath}", xpath);
        }

        // Priority 3: Name + ControlType combination
        if (!string.IsNullOrWhiteSpace(name))
        {
            _logger.LogDebug("Searching by Name: {Name}, ControlType: {ControlType}", name, controlType);

            var result = RetryFind(root, el =>
            {
                if (!string.IsNullOrWhiteSpace(controlType) && TryParseControlType(controlType, out var ct))
                {
                    return el.FindFirstDescendant(cf =>
                        cf.ByName(name).And(cf.ByControlType(ct)));
                }
                return el.FindFirstDescendant(cf => cf.ByName(name));
            });

            if (result != null) return result;
            _logger.LogWarning("Element not found by Name: {Name}", name);
        }

        return null;
    }

    /// <summary>
    /// Find all matching elements (max 50).
    /// Supports search by controlType alone. Filters by visible/enabled if requested.
    /// </summary>
    public IReadOnlyList<AutomationElement> FindElements(
        AutomationElement root,
        string? automationId = null,
        string? name = null,
        string? controlType = null,
        string? xpath = null,
        int maxResults = 50,
        bool visibleOnly = false,
        bool enabledOnly = false)
    {
        AutomationElement[] results = [];

        if (!string.IsNullOrWhiteSpace(automationId))
        {
            results = root.FindAllDescendants(cf => cf.ByAutomationId(automationId));
        }
        else if (!string.IsNullOrWhiteSpace(xpath))
        {
            var single = root.FindFirstByXPath(xpath);
            results = single != null ? [single] : [];
        }
        else if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(controlType))
        {
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(controlType) && TryParseControlType(controlType, out var ct))
            {
                results = root.FindAllDescendants(cf => cf.ByName(name).And(cf.ByControlType(ct)));
            }
            else if (!string.IsNullOrWhiteSpace(controlType) && TryParseControlType(controlType, out var ct2))
            {
                results = root.FindAllDescendants(cf => cf.ByControlType(ct2));
            }
            else if (!string.IsNullOrWhiteSpace(name))
            {
                results = root.FindAllDescendants(cf => cf.ByName(name));
            }
        }

        IEnumerable<AutomationElement> filtered = results;

        if (visibleOnly)
            filtered = filtered.Where(e => !SafeUIA.SafeGetIsOffscreen(e, defaultValue: true));

        if (enabledOnly)
            filtered = filtered.Where(e => SafeUIA.SafeGetIsEnabled(e, defaultValue: false));

        return filtered.Take(maxResults).ToArray();
    }

    /// <summary>
    /// Build a human-readable description of what was searched for (for error messages).
    /// </summary>
    public static string DescribeSearch(
        string? automationId, string? name, string? controlType, string? xpath)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(automationId)) parts.Add($"AutomationId='{automationId}'");
        if (!string.IsNullOrWhiteSpace(xpath)) parts.Add($"XPath='{xpath}'");
        if (!string.IsNullOrWhiteSpace(name)) parts.Add($"Name='{name}'");
        if (!string.IsNullOrWhiteSpace(controlType)) parts.Add($"ControlType='{controlType}'");
        return parts.Count > 0 ? string.Join(", ", parts) : "(no search criteria)";
    }

    /// <summary>
    /// Retry element lookup with transient COM failure recovery.
    /// On COMException (e.g. E_UNEXPECTED), sleeps and retries from the root.
    /// </summary>
    private AutomationElement? RetryFind(
        AutomationElement root,
        Func<AutomationElement, AutomationElement?> finder)
    {
        for (int attempt = 0; attempt <= MaxTransientRetries; attempt++)
        {
            try
            {
                var result = Retry.WhileNull(
                    () => finder(root),
                    timeout: TimeSpan.FromSeconds(2),
                    interval: TimeSpan.FromMilliseconds(250));

                return result.Result;
            }
            catch (COMException ex) when (attempt < MaxTransientRetries)
            {
                _logger.LogDebug(ex,
                    "Transient COM failure during element search (attempt {Attempt}/{Max}), retrying...",
                    attempt + 1, MaxTransientRetries);
                Thread.Sleep(TransientRetryDelay);
            }
        }

        return null;
    }

    private static bool TryParseControlType(string value, out ControlType controlType)
    {
        return Enum.TryParse(value, ignoreCase: true, out controlType);
    }
}
