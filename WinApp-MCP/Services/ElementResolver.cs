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
/// For Name+ControlType matches, ranks multiple candidates using structural
/// context (depth, dialog role, parent type) to prefer dialog action buttons
/// over identically-named child controls.
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
    /// For Name+ControlType searches with multiple matches, uses ranked resolution
    /// to prefer dialog action buttons over identically-named child controls.
    /// Retries on transient COM failures with element re-resolution.
    /// </summary>
    public AutomationElement? FindElement(
        AutomationElement root,
        string? automationId = null,
        string? name = null,
        string? controlType = null,
        string? xpath = null)
    {
        return ResolveElement(root, automationId, name, controlType, xpath).Element;
    }

    /// <summary>
    /// Resolve an element with ranking and ambiguity diagnostics.
    /// Returns the best match plus information about all candidates when ambiguous.
    /// </summary>
    public ResolveResult ResolveElement(
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
            if (result != null) return ResolveResult.Single(result);
            _logger.LogWarning("Element not found by AutomationId: {Id}", automationId);
        }

        // Priority 2: XPath
        if (!string.IsNullOrWhiteSpace(xpath))
        {
            _logger.LogDebug("Searching by XPath: {XPath}", xpath);
            var result = RetryFind(root, el => el.FindFirstByXPath(xpath));
            if (result != null) return ResolveResult.Single(result);
            _logger.LogWarning("Element not found by XPath: {XPath}", xpath);
        }

        // Priority 3: Name + ControlType — ranked resolution
        if (!string.IsNullOrWhiteSpace(name))
        {
            _logger.LogDebug("Searching by Name: {Name}, ControlType: {ControlType}", name, controlType);

            var allMatches = RetryFindAll(root, el =>
            {
                if (!string.IsNullOrWhiteSpace(controlType) && TryParseControlType(controlType, out var ct))
                {
                    return el.FindAllDescendants(cf =>
                        cf.ByName(name).And(cf.ByControlType(ct)));
                }
                return el.FindAllDescendants(cf => cf.ByName(name));
            });

            if (allMatches != null && allMatches.Length > 0)
            {
                if (allMatches.Length == 1)
                    return ResolveResult.Single(allMatches[0]);

                // Multiple matches — rank them
                return RankMatches(allMatches, root);
            }

            _logger.LogWarning("Element not found by Name: {Name}", name);
        }

        return ResolveResult.Single(null);
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

    /// <summary>
    /// Retry multi-element lookup with transient COM failure recovery.
    /// Returns all matching elements, or null on failure.
    /// </summary>
    private AutomationElement[]? RetryFindAll(
        AutomationElement root,
        Func<AutomationElement, AutomationElement[]> finder)
    {
        for (int attempt = 0; attempt <= MaxTransientRetries; attempt++)
        {
            try
            {
                var result = Retry.WhileNull(
                    () =>
                    {
                        var matches = finder(root);
                        return matches.Length > 0 ? matches : null;
                    },
                    timeout: TimeSpan.FromSeconds(2),
                    interval: TimeSpan.FromMilliseconds(250));

                return result.Result;
            }
            catch (COMException ex) when (attempt < MaxTransientRetries)
            {
                _logger.LogDebug(ex,
                    "Transient COM failure during multi-element search (attempt {Attempt}/{Max}), retrying...",
                    attempt + 1, MaxTransientRetries);
                Thread.Sleep(TransientRetryDelay);
            }
        }

        return null;
    }

    /// <summary>
    /// Rank multiple matching elements and return a <see cref="ResolveResult"/> with the best match
    /// and all candidates for ambiguity diagnostics.
    /// </summary>
    private ResolveResult RankMatches(AutomationElement[] matches, AutomationElement root)
    {
        var scored = new List<ScoredMatch>(matches.Length);

        foreach (var element in matches)
        {
            try
            {
                var depth = GetDepth(element, root);
                var score = ScoreElement(element, depth);
                var info = ElementInfo.FromElement(element);

                // Get parent description for diagnostic context
                var parentDesc = string.Empty;
                var parent = SafeUIA.SafeGetParent(element);
                if (parent != null)
                {
                    var parentType = SafeUIA.SafeGetControlType(parent);
                    var parentName = SafeUIA.SafeGetName(parent);
                    parentDesc = string.IsNullOrEmpty(parentName)
                        ? parentType
                        : $"{parentType} \"{parentName}\"";
                }

                scored.Add(new ScoredMatch
                {
                    Element = element,
                    Info = info,
                    Score = score,
                    Depth = depth,
                    ParentDescription = parentDesc
                });
            }
            catch
            {
                // Skip elements that fail to score
            }
        }

        if (scored.Count == 0)
            return ResolveResult.Single(null);

        // Sort by score descending
        scored.Sort((a, b) => b.Score.CompareTo(a.Score));

        _logger.LogDebug("Ranked {Count} candidate elements. Best: score={Score}, depth={Depth}, id={Id}",
            scored.Count, scored[0].Score, scored[0].Depth, scored[0].Info.AutomationId);

        return ResolveResult.Ranked(scored[0].Element, scored);
    }

    /// <summary>
    /// Compute an additive score for an element based on structural context.
    /// Higher scores are preferred. Dialog action buttons score higher than
    /// identically-named child dropdown buttons.
    /// </summary>
    private static int ScoreElement(AutomationElement element, int depth)
    {
        int score = 0;

        // Shallower elements are preferred (dialog action buttons are near the top)
        score -= depth;

        var automationId = SafeUIA.SafeGetAutomationId(element);
        var controlType = SafeUIA.SafeGetControlType(element);

        // Standard dialog accept button (id="1") and cancel button (id="2")
        if (controlType == "Button" && (automationId == "1" || automationId == "2"))
            score += 100;

        // Dropdown buttons are usually embedded in combo boxes — penalize
        if (string.Equals(automationId, "DropDown", StringComparison.OrdinalIgnoreCase))
            score -= 50;

        // Elements nested directly inside a ComboBox are less likely to be the intended target
        var parent = SafeUIA.SafeGetParent(element);
        if (parent != null)
        {
            var parentType = SafeUIA.SafeGetControlType(parent);
            if (parentType == "ComboBox")
                score -= 50;
        }

        // Prefer enabled, visible elements
        if (SafeUIA.SafeGetIsEnabled(element))
            score += 10;

        if (!SafeUIA.SafeGetIsOffscreen(element))
            score += 10;

        return score;
    }

    /// <summary>
    /// Walk up the tree from element to root to determine nesting depth.
    /// Capped at 20 to avoid runaway traversal.
    /// </summary>
    private static int GetDepth(AutomationElement element, AutomationElement root)
    {
        const int maxDepth = 20;
        int depth = 0;

        try
        {
            var rootHandle = root.Properties.NativeWindowHandle.ValueOrDefault;
            var current = element;

            while (depth < maxDepth)
            {
                var parent = SafeUIA.SafeGetParent(current);
                if (parent == null) break;

                // Check if we've reached the root by comparing native window handles
                try
                {
                    var parentHandle = parent.Properties.NativeWindowHandle.ValueOrDefault;
                    if (parentHandle == rootHandle && rootHandle != IntPtr.Zero)
                        break;
                }
                catch { /* handle comparison failed — continue walking */ }

                depth++;
                current = parent;
            }
        }
        catch
        {
            // If tree walking fails, return a neutral depth
        }

        return depth;
    }

    private static bool TryParseControlType(string value, out ControlType controlType)
    {
        return Enum.TryParse(value, ignoreCase: true, out controlType);
    }
}
