using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;

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
}
