using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using WinAppMCP.Models;

namespace WinAppMCP.Services;

/// <summary>
/// Safe wrappers for UIA property access. Many providers return NotSupported,
/// throw COMException, or raise ElementNotAvailableException for properties
/// that are technically optional. These helpers absorb those failures and
/// return a defined sentinel or default value.
/// </summary>
public static class SafeUIA
{
    public const string NotSupported = "<NotSupported>";

    public static string SafeGetName(AutomationElement element)
    {
        try
        {
            return element.Name ?? string.Empty;
        }
        catch (COMException) { return NotSupported; }
        catch (FlaUI.Core.Exceptions.PropertyNotSupportedException) { return NotSupported; }
        catch (Exception) { return NotSupported; }
    }

    public static string SafeGetAutomationId(AutomationElement element)
    {
        try
        {
            return element.AutomationId ?? string.Empty;
        }
        catch (COMException) { return NotSupported; }
        catch (FlaUI.Core.Exceptions.PropertyNotSupportedException) { return NotSupported; }
        catch (Exception) { return NotSupported; }
    }

    public static string SafeGetClassName(AutomationElement element)
    {
        try
        {
            return element.ClassName ?? string.Empty;
        }
        catch (COMException) { return NotSupported; }
        catch (FlaUI.Core.Exceptions.PropertyNotSupportedException) { return NotSupported; }
        catch (Exception) { return NotSupported; }
    }

    public static bool SafeGetIsEnabled(AutomationElement element, bool defaultValue = true)
    {
        try
        {
            return element.IsEnabled;
        }
        catch { return defaultValue; }
    }

    public static bool SafeGetIsOffscreen(AutomationElement element, bool defaultValue = false)
    {
        try
        {
            return element.IsOffscreen;
        }
        catch { return defaultValue; }
    }

    public static string SafeGetControlType(AutomationElement element)
    {
        try
        {
            return element.ControlType.ToString();
        }
        catch { return "Unknown"; }
    }

    /// <summary>
    /// Try to extract text from an element using a multi-strategy fallback chain.
    /// Returns the text and which source provided it.
    /// </summary>
    public static (string Text, string Source) ExtractText(AutomationElement element)
    {
        // Strategy 1: ValuePattern (most common for editable controls)
        try
        {
            if (element.Patterns.Value.IsSupported)
            {
                var val = element.Patterns.Value.Pattern.Value.ValueOrDefault;
                if (!string.IsNullOrEmpty(val))
                    return (val, "ValuePattern");
            }
        }
        catch { /* fall through */ }

        // Strategy 2: TextPattern
        try
        {
            if (element.Patterns.Text.IsSupported)
            {
                var text = element.Patterns.Text.Pattern.DocumentRange.GetText(-1);
                if (!string.IsNullOrEmpty(text))
                    return (text, "TextPattern");
            }
        }
        catch { /* fall through */ }

        // Strategy 3: Name property
        try
        {
            var name = element.Name;
            if (!string.IsNullOrEmpty(name))
                return (name, "Name");
        }
        catch { /* fall through */ }

        // Strategy 4: LegacyIAccessible
        try
        {
            if (element.Patterns.LegacyIAccessible.IsSupported)
            {
                var legacyName = element.Patterns.LegacyIAccessible.Pattern.Name.ValueOrDefault;
                if (!string.IsNullOrEmpty(legacyName))
                    return (legacyName, "LegacyIAccessible.Name");

                var legacyValue = element.Patterns.LegacyIAccessible.Pattern.Value.ValueOrDefault;
                if (!string.IsNullOrEmpty(legacyValue))
                    return (legacyValue, "LegacyIAccessible.Value");
            }
        }
        catch { /* fall through */ }

        // Strategy 5: Visible text descendants
        try
        {
            var textChildren = element.FindAllDescendants(cf =>
                cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text));
            var texts = new List<string>();
            foreach (var child in textChildren)
            {
                var childName = SafeGetName(child);
                if (!string.IsNullOrEmpty(childName) && childName != NotSupported)
                    texts.Add(childName);
            }
            if (texts.Count > 0)
                return (string.Join(" ", texts), "TextDescendants");
        }
        catch { /* fall through */ }

        return (string.Empty, "None");
    }

    /// <summary>
    /// Heuristic: returns true when text looks like a fully-qualified CLR type name
    /// (e.g. "Namespace.SubNs.ClassName") rather than user-visible content.
    /// </summary>
    public static bool IsLikelyCLRTypeName(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text == NotSupported)
            return false;

        // Must contain at least one dot, no spaces, and each segment starts with uppercase
        if (text.Contains(' ') || !text.Contains('.'))
            return false;

        return Regex.IsMatch(text, @"^[A-Z][A-Za-z0-9]*(\.[A-Z][A-Za-z0-9]*)+$");
    }

    /// <summary>
    /// Extract user-visible text from an element, falling back to visible descendants
    /// when the primary text looks like a CLR type name (templated list row).
    /// Returns the best text, individual parts, raw provider text, and the source strategy.
    /// </summary>
    public static (string Text, string[] Parts, string RawProviderText, string Source) ExtractVisibleSummary(AutomationElement element)
    {
        var rawName = SafeGetName(element);
        var (primaryText, primarySource) = ExtractText(element);

        // If primary text is good and not a CLR type name, return it directly
        if (!string.IsNullOrEmpty(primaryText) && primarySource != "None" && !IsLikelyCLRTypeName(primaryText))
            return (primaryText, [], rawName, primarySource);

        // Primary text is missing or looks like a CLR type name — try visible descendants
        try
        {
            var textChildren = element.FindAllDescendants(cf =>
                cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text));
            var parts = new List<string>();
            foreach (var child in textChildren)
            {
                var childName = SafeGetName(child);
                if (!string.IsNullOrEmpty(childName) && childName != NotSupported)
                    parts.Add(childName);
            }
            if (parts.Count > 0)
                return (string.Join(" ", parts), parts.ToArray(), rawName, "VisibleDescendants");
        }
        catch { /* fall through */ }

        // Return whatever primary text we had (even if CLR-looking), since nothing better was found
        return (
            string.IsNullOrEmpty(primaryText) ? string.Empty : primaryText,
            [],
            rawName,
            string.IsNullOrEmpty(primaryText) ? "None" : primarySource
        );
    }

    /// <summary>
    /// Discover actionable child controls within an element (buttons, toggles, expanders).
    /// Returns up to 10 row-level actions for use in list item enumeration.
    /// </summary>
    public static List<RowAction> DiscoverRowActions(AutomationElement element)
    {
        var actions = new List<RowAction>();
        try
        {
            var children = element.FindAllDescendants();
            foreach (var child in children)
            {
                if (actions.Count >= 10) break;

                try
                {
                    bool isActionable = false;
                    try { isActionable = child.Patterns.Invoke.IsSupported; } catch { }
                    if (!isActionable) try { isActionable = child.Patterns.Toggle.IsSupported; } catch { }
                    if (!isActionable) try { isActionable = child.Patterns.ExpandCollapse.IsSupported; } catch { }

                    if (isActionable)
                    {
                        actions.Add(new RowAction(
                            SafeGetName(child),
                            SafeGetAutomationId(child),
                            SafeGetControlType(child)
                        ));
                    }
                }
                catch { /* skip child */ }
            }
        }
        catch { /* element may not support child enumeration */ }

        return actions;
    }

    /// <summary>
    /// Classify a child control's primary interaction type based on its supported patterns.
    /// </summary>
    public static string ClassifyInteractionType(AutomationElement element)
    {
        try { if (element.Patterns.Value.IsSupported) return "edit"; } catch { }
        try { if (element.Patterns.Toggle.IsSupported) return "toggle"; } catch { }
        try { if (element.Patterns.Invoke.IsSupported) return "button"; } catch { }
        try { if (element.Patterns.Selection.IsSupported) return "picker"; } catch { }
        try { if (element.Patterns.ExpandCollapse.IsSupported) return "expander"; } catch { }

        var ct = SafeGetControlType(element);
        if (ct is "Text" or "Image") return "label";

        return "other";
    }
}
