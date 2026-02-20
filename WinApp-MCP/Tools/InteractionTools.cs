using System.ComponentModel;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using ModelContextProtocol.Server;
using WinAppMCP.Models;
using WinAppMCP.Services;

namespace WinAppMCP.Tools;

/// <summary>
/// MCP tools for interacting with UI elements — clicking, typing, toggling, selecting, etc.
/// </summary>
[McpServerToolType]
public static class InteractionTools
{
    [McpServerTool(Name = "click_element"), Description(
        "Click on a UI element. Brings the window to the foreground first. " +
        "Use clickType to specify left, right, or double click.")]
    public static string ClickElement(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type: Button, TextBox, CheckBox, etc.")] string? controlType = null,
        [Description("XPath expression")] string? xpath = null,
        [Description("Click type: left (default), right, or double")] string clickType = "left")
    {
        return Task.Run(() =>
        {
            try
            {
                var window = flaUI.GetCachedWindow(windowHandle);
                var element = resolver.FindElement(window, automationId, name, controlType, xpath);

                if (element == null)
                    return $"Error: Element not found. {ElementResolver.DescribeSearch(automationId, name, controlType, xpath)}";

                // Bring window to foreground
                window.SetForeground();
                Wait.UntilInputIsProcessed();

                switch (clickType.ToLowerInvariant())
                {
                    case "right":
                        element.RightClick();
                        break;
                    case "double":
                        element.DoubleClick();
                        break;
                    default:
                        element.Click();
                        break;
                }

                Wait.UntilInputIsProcessed();

                var info = ElementInfo.FromElement(element);
                return $"Clicked ({clickType}) on: {info.ToCompactString()}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }).Result;
    }

    [McpServerTool(Name = "invoke_element"), Description(
        "Invoke a button or clickable element using the UIA Invoke pattern (no mouse movement). " +
        "Preferred over click_element for buttons as it is more reliable. " +
        "Falls back to Click() if InvokePattern is not supported.")]
    public static string InvokeElement(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type: Button, MenuItem, etc.")] string? controlType = null,
        [Description("XPath expression")] string? xpath = null)
    {
        return Task.Run(() =>
        {
            try
            {
                var window = flaUI.GetCachedWindow(windowHandle);
                var element = resolver.FindElement(window, automationId, name, controlType, xpath);

                if (element == null)
                    return $"Error: Element not found. {ElementResolver.DescribeSearch(automationId, name, controlType, xpath)}";

                // Try InvokePattern first
                if (element.Patterns.Invoke.IsSupported)
                {
                    element.Patterns.Invoke.Pattern.Invoke();
                    Wait.UntilInputIsProcessed();
                    return $"Invoked: {ElementInfo.FromElement(element).ToCompactString()}";
                }

                // Fallback to click
                window.SetForeground();
                element.Click();
                Wait.UntilInputIsProcessed();
                return $"Clicked (InvokePattern not supported, used Click fallback): {ElementInfo.FromElement(element).ToCompactString()}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }).Result;
    }

    [McpServerTool(Name = "type_text"), Description(
        "Type text into an input element (TextBox, Edit, etc.). " +
        "By default clears existing text first. " +
        "Uses ValuePattern.SetValue() if available, otherwise falls back to keyboard simulation.")]
    public static string TypeText(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("The text to type")] string text,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("XPath expression")] string? xpath = null,
        [Description("Clear existing text before typing (default true)")] bool clearFirst = true)
    {
        return Task.Run(() =>
        {
            try
            {
                var window = flaUI.GetCachedWindow(windowHandle);
                var element = resolver.FindElement(window, automationId, name, controlType, xpath);

                if (element == null)
                    return $"Error: Element not found. {ElementResolver.DescribeSearch(automationId, name, controlType, xpath)}";

                window.SetForeground();

                // Try ValuePattern (most reliable)
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
                    return $"Typed text via ValuePattern into: {ElementInfo.FromElement(element).ToCompactString()}";
                }

                // Fallback to keyboard simulation
                element.Focus();
                Wait.UntilInputIsProcessed();

                if (clearFirst)
                {
                    // Select all and delete
                    Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
                    Keyboard.Type(VirtualKeyShort.DELETE);
                    Wait.UntilInputIsProcessed();
                }

                Keyboard.Type(text);
                Wait.UntilInputIsProcessed();

                return $"Typed text via keyboard simulation into: {ElementInfo.FromElement(element).ToCompactString()}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }).Result;
    }

    [McpServerTool(Name = "toggle_element"), Description(
        "Toggle a CheckBox or ToggleButton. Returns the new checked state.")]
    public static string ToggleElement(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("XPath expression")] string? xpath = null)
    {
        return Task.Run(() =>
        {
            try
            {
                var window = flaUI.GetCachedWindow(windowHandle);
                var element = resolver.FindElement(window, automationId, name, controlType, xpath);

                if (element == null)
                    return $"Error: Element not found. {ElementResolver.DescribeSearch(automationId, name, controlType, xpath)}";

                if (!element.Patterns.Toggle.IsSupported)
                    return $"Error: Element does not support Toggle pattern. {ElementInfo.FromElement(element).ToCompactString()}";

                element.Patterns.Toggle.Pattern.Toggle();
                Wait.UntilInputIsProcessed();

                var newState = element.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault;
                return $"Toggled: {ElementInfo.FromElement(element).ToCompactString()} → State: {newState}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }).Result;
    }

    [McpServerTool(Name = "select_item"), Description(
        "Select an item in a ComboBox or ListBox by text value or index.")]
    public static string SelectItem(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("AutomationId of the ComboBox/ListBox")] string? automationId = null,
        [Description("Name of the ComboBox/ListBox")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("XPath expression")] string? xpath = null,
        [Description("Text value of the item to select")] string? value = null,
        [Description("Zero-based index of the item to select")] int? index = null)
    {
        return Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(value) && index == null)
                    return "Error: Provide either 'value' or 'index' to select an item.";

                var window = flaUI.GetCachedWindow(windowHandle);
                var element = resolver.FindElement(window, automationId, name, controlType, xpath);

                if (element == null)
                    return $"Error: Element not found. {ElementResolver.DescribeSearch(automationId, name, controlType, xpath)}";

                window.SetForeground();

                var comboBox = element.AsComboBox();
                if (comboBox != null)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                        comboBox.Select(value);
                    else if (index.HasValue)
                        comboBox.Select(index.Value);

                    Wait.UntilInputIsProcessed();
                    var selected = comboBox.SelectedItem?.Text ?? "(none)";
                    return $"Selected '{selected}' in: {ElementInfo.FromElement(element).ToCompactString()}";
                }

                var listBox = element.AsListBox();
                if (listBox != null)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                        listBox.Select(value);
                    else if (index.HasValue)
                        listBox.Select(index.Value);

                    Wait.UntilInputIsProcessed();
                    var selected = listBox.SelectedItem?.Text ?? "(none)";
                    return $"Selected '{selected}' in: {ElementInfo.FromElement(element).ToCompactString()}";
                }

                return $"Error: Element is not a ComboBox or ListBox. {ElementInfo.FromElement(element).ToCompactString()}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }).Result;
    }

    [McpServerTool(Name = "send_keys"), Description(
        "Send keyboard shortcuts or key presses to the focused window. " +
        "Supports modifiers: Ctrl+A, Alt+F4, Shift+Tab, Ctrl+Shift+S, etc. " +
        "Single keys: Enter, Tab, Escape, F1-F12, Delete, Backspace, Up, Down, Left, Right.")]
    public static string SendKeys(
        FlaUIService flaUI,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("Key combination to send, e.g. 'Ctrl+A', 'Enter', 'Alt+F4', 'Ctrl+Shift+S'")] string keys)
    {
        return Task.Run(() =>
        {
            try
            {
                var window = flaUI.GetCachedWindow(windowHandle);
                window.SetForeground();
                Wait.UntilInputIsProcessed();

                var keyParts = keys.Split('+').Select(k => k.Trim()).ToArray();
                var modifiers = new List<VirtualKeyShort>();
                VirtualKeyShort? mainKey = null;

                foreach (var part in keyParts)
                {
                    var key = ParseKey(part);
                    if (key == null)
                        return $"Error: Unknown key '{part}'. Supported: Ctrl, Alt, Shift, Enter, Tab, Escape, F1-F12, Delete, Backspace, Up, Down, Left, Right, A-Z, 0-9.";

                    if (IsModifier(part))
                        modifiers.Add(key.Value);
                    else
                        mainKey = key.Value;
                }

                if (mainKey == null && modifiers.Count > 0)
                {
                    // Just modifiers — press and release
                    foreach (var mod in modifiers) Keyboard.Press(mod);
                    foreach (var mod in modifiers) Keyboard.Release(mod);
                }
                else if (mainKey != null)
                {
                    foreach (var mod in modifiers) Keyboard.Press(mod);
                    Keyboard.Type(mainKey.Value);
                    foreach (var mod in modifiers.AsEnumerable().Reverse()) Keyboard.Release(mod);
                }

                Wait.UntilInputIsProcessed();
                return $"Sent keys: {keys}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }).Result;
    }

    [McpServerTool(Name = "focus_element"), Description(
        "Set focus to a specific element and bring the window to the foreground.")]
    public static string FocusElement(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("XPath expression")] string? xpath = null)
    {
        return Task.Run(() =>
        {
            try
            {
                var window = flaUI.GetCachedWindow(windowHandle);
                window.SetForeground();

                if (automationId == null && name == null && controlType == null && xpath == null)
                {
                    Wait.UntilInputIsProcessed();
                    return $"Brought window to foreground: {window.Name}";
                }

                var element = resolver.FindElement(window, automationId, name, controlType, xpath);
                if (element == null)
                    return $"Error: Element not found. {ElementResolver.DescribeSearch(automationId, name, controlType, xpath)}";

                element.Focus();
                Wait.UntilInputIsProcessed();
                return $"Focused: {ElementInfo.FromElement(element).ToCompactString()}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }).Result;
    }

    #region Key Parsing Helpers

    private static bool IsModifier(string key) =>
        key.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Control", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Alt", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Shift", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Win", StringComparison.OrdinalIgnoreCase);

    private static VirtualKeyShort? ParseKey(string key) => key.ToUpperInvariant() switch
    {
        "CTRL" or "CONTROL" => VirtualKeyShort.CONTROL,
        "ALT" => VirtualKeyShort.ALT,
        "SHIFT" => VirtualKeyShort.SHIFT,
        "WIN" => VirtualKeyShort.LWIN,
        "ENTER" or "RETURN" => VirtualKeyShort.RETURN,
        "TAB" => VirtualKeyShort.TAB,
        "ESCAPE" or "ESC" => VirtualKeyShort.ESCAPE,
        "DELETE" or "DEL" => VirtualKeyShort.DELETE,
        "BACKSPACE" or "BACK" => VirtualKeyShort.BACK,
        "SPACE" => VirtualKeyShort.SPACE,
        "UP" => VirtualKeyShort.UP,
        "DOWN" => VirtualKeyShort.DOWN,
        "LEFT" => VirtualKeyShort.LEFT,
        "RIGHT" => VirtualKeyShort.RIGHT,
        "HOME" => VirtualKeyShort.HOME,
        "END" => VirtualKeyShort.END,
        "PAGEUP" or "PGUP" => VirtualKeyShort.PRIOR,
        "PAGEDOWN" or "PGDN" => VirtualKeyShort.NEXT,
        "INSERT" or "INS" => VirtualKeyShort.INSERT,
        "F1" => VirtualKeyShort.F1,
        "F2" => VirtualKeyShort.F2,
        "F3" => VirtualKeyShort.F3,
        "F4" => VirtualKeyShort.F4,
        "F5" => VirtualKeyShort.F5,
        "F6" => VirtualKeyShort.F6,
        "F7" => VirtualKeyShort.F7,
        "F8" => VirtualKeyShort.F8,
        "F9" => VirtualKeyShort.F9,
        "F10" => VirtualKeyShort.F10,
        "F11" => VirtualKeyShort.F11,
        "F12" => VirtualKeyShort.F12,
        // Letters A-Z
        var k when k.Length == 1 && k[0] >= 'A' && k[0] <= 'Z' =>
            (VirtualKeyShort)k[0],
        // Digits 0-9
        var k when k.Length == 1 && k[0] >= '0' && k[0] <= '9' =>
            (VirtualKeyShort)k[0],
        _ => null,
    };

    #endregion
}
