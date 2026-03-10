using System.ComponentModel;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using ModelContextProtocol.Server;
using WinAppMCP.Models;
using WinAppMCP.Services;

namespace WinAppMCP.Tools;

/// <summary>
/// MCP tools for reading and setting text in UI elements.
/// </summary>
[McpServerToolType]
public static class TextTools
{
    [McpServerTool(Name = "get_visible_text"), Description(
        "Read all visible text from a window or subtree. Returns text elements in visual reading order. " +
        "Useful for reading validation messages, error banners, status labels, and other displayed text. " +
        "Uses a fallback chain (Name, ValuePattern, TextPattern) to extract text from each element.")]
    public static string GetVisibleText(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("Optional AutomationId of subtree root to scope the search")] string? rootAutomationId = null,
        [Description("Optional control type filter, e.g. 'Text' to only read text blocks")] string? controlTypeFilter = null,
        [Description("Maximum number of text entries to return (default 100)")] int maxResults = 100,
        [Description("Include offscreen/hidden elements (default false)")] bool includeOffscreen = false)
    {
        return Task.Run(() =>
        {
            try
            {
                var window = flaUI.GetCachedWindow(windowHandle);
                AutomationElement root = window;

                if (!string.IsNullOrWhiteSpace(rootAutomationId))
                {
                    var subtreeRoot = resolver.FindElement(window, automationId: rootAutomationId);
                    if (subtreeRoot == null)
                        return $"Error: Could not find subtree root with AutomationId='{rootAutomationId}'.";
                    root = subtreeRoot;
                }

                // Determine which control types to search
                AutomationElement[] elements;
                if (!string.IsNullOrWhiteSpace(controlTypeFilter) &&
                    Enum.TryParse<ControlType>(controlTypeFilter, ignoreCase: true, out var ct))
                {
                    elements = root.FindAllDescendants(cf => cf.ByControlType(ct));
                }
                else
                {
                    // Default: search text-bearing control types
                    var textTypes = new[] { ControlType.Text, ControlType.Edit, ControlType.Document };
                    var all = new List<AutomationElement>();
                    foreach (var tt in textTypes)
                    {
                        try
                        {
                            all.AddRange(root.FindAllDescendants(cf => cf.ByControlType(tt)));
                        }
                        catch { /* skip types that fail */ }
                    }
                    elements = all.ToArray();
                }

                // Extract text from each element, with bounds for sorting
                var entries = new List<(string Text, string Source, int Top, int Left)>();

                foreach (var el in elements)
                {
                    try
                    {
                        if (!includeOffscreen && SafeUIA.SafeGetIsOffscreen(el, defaultValue: true))
                            continue;

                        var (text, source) = SafeUIA.ExtractText(el);
                        if (string.IsNullOrWhiteSpace(text) || text == SafeUIA.NotSupported)
                            continue;

                        int top = 0, left = 0;
                        try
                        {
                            var rect = el.BoundingRectangle;
                            top = (int)rect.Y;
                            left = (int)rect.X;
                        }
                        catch { /* position unknown, sort last */ }

                        entries.Add((text, source, top, left));
                    }
                    catch { /* skip element */ }

                    if (entries.Count >= maxResults)
                        break;
                }

                if (entries.Count == 0)
                    return "No visible text found in the specified scope.";

                // Sort by reading order (top to bottom, left to right)
                entries.Sort((a, b) =>
                {
                    var cmp = a.Top.CompareTo(b.Top);
                    return cmp != 0 ? cmp : a.Left.CompareTo(b.Left);
                });

                var sb = new StringBuilder();
                sb.AppendLine($"Found {entries.Count} text element(s):");
                foreach (var (text, source, _, _) in entries)
                {
                    sb.AppendLine($"  [{source}] {text}");
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                var category = ToolResult.Classify(ex);
                return $"Error [{category}]: {ex.Message}";
            }
        }).Result;
    }

    [McpServerTool(Name = "get_element_text"), Description(
        "Read the text or current value from a specific UI element. " +
        "Uses a multi-strategy fallback: ValuePattern, TextPattern, Name property, " +
        "LegacyIAccessible, and visible text descendants. Reports which source provided the text. " +
        "Use rootAutomationId to scope the search to a subtree.")]
    public static string GetElementText(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("XPath expression")] string? xpath = null,
        [Description("Optional AutomationId of subtree root to scope the search")] string? rootAutomationId = null)
    {
        return Task.Run(() =>
        {
            try
            {
                var window = flaUI.GetCachedWindow(windowHandle);
                AutomationElement searchRoot = window;
                if (!string.IsNullOrWhiteSpace(rootAutomationId))
                {
                    var root = resolver.FindElement(window, automationId: rootAutomationId);
                    if (root == null)
                        return $"Error: Could not find subtree root with AutomationId='{rootAutomationId}'.";
                    searchRoot = root;
                }

                var element = resolver.FindElement(searchRoot, automationId, name, controlType, xpath);

                if (element == null)
                    return $"Error: Element not found. {ElementResolver.DescribeSearch(automationId, name, controlType, xpath)}";

                var (text, source) = SafeUIA.ExtractText(element);

                if (string.IsNullOrEmpty(text) || source == "None")
                    return $"No text found on element: {ElementInfo.FromElement(element).ToCompactString()}";

                return $"Text (source={source}): {text}\nElement: {ElementInfo.FromElement(element).ToCompactString()}";
            }
            catch (Exception ex)
            {
                var category = ToolResult.Classify(ex);
                return $"Error [{category}]: {ex.Message}";
            }
        }).Result;
    }

    [McpServerTool(Name = "set_text"), Description(
        "Set text in a text-capable UI element. Uses ValuePattern.SetValue() by default (most reliable). " +
        "Set allowKeyboardFallback=true to enable keyboard simulation as a last resort. " +
        "Reports which method was used to set the text. " +
        "Use rootAutomationId to scope the search to a subtree.")]
    public static string SetText(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("The text to set")] string text,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("XPath expression")] string? xpath = null,
        [Description("Clear existing text before setting (default true)")] bool clearFirst = true,
        [Description("Allow keyboard simulation as a fallback if patterns unavailable (default false)")] bool allowKeyboardFallback = false,
        [Description("Optional AutomationId of subtree root to scope the search")] string? rootAutomationId = null)
    {
        return Task.Run(() =>
        {
            try
            {
                var window = flaUI.GetCachedWindow(windowHandle);
                AutomationElement searchRoot = window;
                if (!string.IsNullOrWhiteSpace(rootAutomationId))
                {
                    var root = resolver.FindElement(window, automationId: rootAutomationId);
                    if (root == null)
                        return $"Error: Could not find subtree root with AutomationId='{rootAutomationId}'.";
                    searchRoot = root;
                }

                var element = resolver.FindElement(searchRoot, automationId, name, controlType, xpath);

                if (element == null)
                    return $"Error: Element not found. {ElementResolver.DescribeSearch(automationId, name, controlType, xpath)}";

                window.SetForeground();
                var elementDesc = ElementInfo.FromElement(element).ToCompactString();

                // Strategy 1: ValuePattern (most reliable)
                try
                {
                    if (element.Patterns.Value.IsSupported)
                    {
                        if (clearFirst)
                        {
                            element.Patterns.Value.Pattern.SetValue(text);
                        }
                        else
                        {
                            var current = element.Patterns.Value.Pattern.Value.ValueOrDefault ?? "";
                            element.Patterns.Value.Pattern.SetValue(current + text);
                        }
                        Wait.UntilInputIsProcessed();
                        return $"Set text via ValuePattern on: {elementDesc}";
                    }
                }
                catch (Exception ex)
                {
                    // ValuePattern supported but SetValue failed — log and try next strategy
                    if (!allowKeyboardFallback)
                        return $"Error: ValuePattern.SetValue() failed: {ex.Message}. Set allowKeyboardFallback=true to try keyboard simulation. Element: {elementDesc}";
                }

                // Strategy 2: Keyboard simulation (opt-in only)
                if (allowKeyboardFallback)
                {
                    element.Focus();
                    Wait.UntilInputIsProcessed();

                    if (clearFirst)
                    {
                        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
                        Keyboard.Type(VirtualKeyShort.DELETE);
                        Wait.UntilInputIsProcessed();
                    }

                    Keyboard.Type(text);
                    Wait.UntilInputIsProcessed();
                    return $"Set text via keyboard simulation on: {elementDesc}";
                }

                return $"Error: Element does not support ValuePattern and keyboard fallback is disabled. Element: {elementDesc}";
            }
            catch (Exception ex)
            {
                var category = ToolResult.Classify(ex);
                return $"Error [{category}]: {ex.Message}";
            }
        }).Result;
    }
}
