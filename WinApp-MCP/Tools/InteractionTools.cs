using System.ComponentModel;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
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
        "Use clickType to specify left, right, or double click. " +
        "Use rootAutomationId to scope the search to a subtree.")]
    public static string ClickElement(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type: Button, TextBox, CheckBox, etc.")] string? controlType = null,
        [Description("XPath expression")] string? xpath = null,
        [Description("Click type: left (default), right, or double")] string clickType = "left",
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

                var resolved = resolver.ResolveElement(searchRoot, automationId, name, controlType, xpath);

                if (resolved.Element == null)
                    return $"Error: Element not found. {ElementResolver.DescribeSearch(automationId, name, controlType, xpath)}";

                var element = resolved.Element;

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
                var result = $"Clicked ({clickType}) on: {info.ToCompactString()}";
                if (resolved.IsAmbiguous)
                    result += resolved.FormatAmbiguityWarning();
                return result;
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
        "Falls back to Click() if InvokePattern is not supported. " +
        "Use rootAutomationId to scope the search to a subtree.")]
    public static string InvokeElement(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type: Button, MenuItem, etc.")] string? controlType = null,
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

                var resolved = resolver.ResolveElement(searchRoot, automationId, name, controlType, xpath);

                if (resolved.Element == null)
                    return $"Error: Element not found. {ElementResolver.DescribeSearch(automationId, name, controlType, xpath)}";

                var element = resolved.Element;
                var ambiguityWarning = resolved.IsAmbiguous ? resolved.FormatAmbiguityWarning() : "";

                // Try InvokePattern first
                if (element.Patterns.Invoke.IsSupported)
                {
                    element.Patterns.Invoke.Pattern.Invoke();
                    Wait.UntilInputIsProcessed();
                    return $"Invoked: {ElementInfo.FromElement(element).ToCompactString()}{ambiguityWarning}";
                }

                // Fallback to click
                window.SetForeground();
                element.Click();
                Wait.UntilInputIsProcessed();
                return $"Clicked (InvokePattern not supported, used Click fallback): {ElementInfo.FromElement(element).ToCompactString()}{ambiguityWarning}";
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
        "Uses ValuePattern.SetValue() if available, otherwise falls back to keyboard simulation. " +
        "Use rootAutomationId to scope the search to a subtree.")]
    public static string TypeText(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("The text to type")] string text,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("XPath expression")] string? xpath = null,
        [Description("Clear existing text before typing (default true)")] bool clearFirst = true,
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
        "Toggle a CheckBox or ToggleButton. Returns the new checked state. " +
        "Use rootAutomationId to scope the search to a subtree.")]
    public static string ToggleElement(
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
        "Select an item in a ComboBox or ListBox by text value or index. " +
        "Uses a multi-strategy fallback chain for value matching when provider does not expose Name. " +
        "Use rootAutomationId to scope the search to a subtree.")]
    public static string SelectItem(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("AutomationId of the ComboBox/ListBox")] string? automationId = null,
        [Description("Name of the ComboBox/ListBox")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("XPath expression")] string? xpath = null,
        [Description("Text value of the item to select")] string? value = null,
        [Description("Zero-based index of the item to select")] int? index = null,
        [Description("Optional AutomationId of subtree root to scope the search")] string? rootAutomationId = null)
    {
        return Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(value) && index == null)
                    return "Error: Provide either 'value' or 'index' to select an item.";

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

                // Expand ComboBox if collapsed
                bool wasExpanded = false;
                if (element.Patterns.ExpandCollapse.IsSupported)
                {
                    var state = element.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.ValueOrDefault;
                    if (state == ExpandCollapseState.Collapsed)
                    {
                        element.Patterns.ExpandCollapse.Pattern.Expand();
                        Wait.UntilInputIsProcessed();
                        Thread.Sleep(100); // allow items to load
                        wasExpanded = true;
                    }
                }

                try
                {
                    // Get items from the container
                    AutomationElement[] items;
                    var comboBox = element.AsComboBox();
                    var listBox = element.AsListBox();

                    if (comboBox != null)
                        items = comboBox.Items.Select(i => (AutomationElement)i).ToArray();
                    else if (listBox != null)
                        items = listBox.Items.Select(i => (AutomationElement)i).ToArray();
                    else
                        return $"Error: Element is not a ComboBox or ListBox. {ElementInfo.FromElement(element).ToCompactString()}";

                    if (index.HasValue)
                    {
                        return SelectByIndex(items, index.Value, element);
                    }
                    else
                    {
                        return SelectByValue(items, value!, element);
                    }
                }
                finally
                {
                    // Collapse if we expanded it
                    if (wasExpanded)
                    {
                        try
                        {
                            element.Patterns.ExpandCollapse.Pattern.Collapse();
                            Wait.UntilInputIsProcessed();
                        }
                        catch { /* best effort */ }
                    }
                }
            }
            catch (Exception ex)
            {
                var category = ToolResult.Classify(ex);
                return $"Error [{category}]: {ex.Message}";
            }
        }).Result;
    }

    private static string SelectByIndex(AutomationElement[] items, int index, AutomationElement container)
    {
        if (index < 0 || index >= items.Length)
            return $"Error: Index {index} out of range. Item count: {items.Length}.";

        var item = items[index];

        // Scroll into view if virtualized
        try
        {
            if (item.Patterns.ScrollItem.IsSupported)
                item.Patterns.ScrollItem.Pattern.ScrollIntoView();
        }
        catch { /* best effort */ }

        // Select via SelectionItemPattern (does not require reading Name)
        try
        {
            if (item.Patterns.SelectionItem.IsSupported)
            {
                item.Patterns.SelectionItem.Pattern.Select();
                Wait.UntilInputIsProcessed();

                var (text, source) = SafeUIA.ExtractText(item);
                return $"Selected index {index} (text='{text}', source={source}) in: {ElementInfo.FromElement(container).ToCompactString()}";
            }
        }
        catch { /* fall through to click */ }

        // Fallback: click the item
        item.Click();
        Wait.UntilInputIsProcessed();

        var (fallbackText, fallbackSource) = SafeUIA.ExtractText(item);
        return $"Selected index {index} via click (text='{fallbackText}', source={fallbackSource}) in: {ElementInfo.FromElement(container).ToCompactString()}";
    }

    private static string SelectByValue(AutomationElement[] items, string value, AutomationElement container)
    {
        int unsupportedCount = 0;
        int clrTypeNameCount = 0;
        int visibleDescendantCount = 0;
        string matchMethod = "None";

        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];
            var (text, source) = SafeUIA.ExtractText(item);

            if (source == "None" || (text == SafeUIA.NotSupported))
            {
                unsupportedCount++;
                continue;
            }

            // Track CLR type names and visible descendant fallbacks
            if (SafeUIA.IsLikelyCLRTypeName(text))
                clrTypeNameCount++;
            if (source == "TextDescendants")
                visibleDescendantCount++;

            // Try matching against primary text first
            bool matched = string.Equals(text, value, StringComparison.OrdinalIgnoreCase);

            // If primary text is a CLR type name, also try matching against visible descendants
            if (!matched && SafeUIA.IsLikelyCLRTypeName(text))
            {
                var (summaryText, _, _, summarySource) = SafeUIA.ExtractVisibleSummary(item);
                if (summarySource == "VisibleDescendants" && string.Equals(summaryText, value, StringComparison.OrdinalIgnoreCase))
                {
                    text = summaryText;
                    source = summarySource;
                    matched = true;
                }
            }

            if (matched)
            {
                matchMethod = source;

                // Scroll into view if virtualized
                try
                {
                    if (item.Patterns.ScrollItem.IsSupported)
                        item.Patterns.ScrollItem.Pattern.ScrollIntoView();
                }
                catch { /* best effort */ }

                var diagnosticSuffix = BuildDiagnosticSuffix(unsupportedCount, clrTypeNameCount, visibleDescendantCount);

                // Select via SelectionItemPattern
                try
                {
                    if (item.Patterns.SelectionItem.IsSupported)
                    {
                        item.Patterns.SelectionItem.Pattern.Select();
                        Wait.UntilInputIsProcessed();
                        return $"Selected '{value}' at index {i} (matchedVia={matchMethod}) in: {ElementInfo.FromElement(container).ToCompactString()}{diagnosticSuffix}";
                    }
                }
                catch { /* fall through to click */ }

                // Fallback: click
                item.Click();
                Wait.UntilInputIsProcessed();
                return $"Selected '{value}' at index {i} via click (matchedVia={matchMethod}) in: {ElementInfo.FromElement(container).ToCompactString()}{diagnosticSuffix}";
            }
        }

        var finalDiag = BuildDiagnosticSuffix(unsupportedCount, clrTypeNameCount, visibleDescendantCount);
        return $"Error: No item matching '{value}' found. Searched {items.Length} items.{finalDiag}";
    }

    private static string BuildDiagnosticSuffix(int unsupported, int clrTypeNames, int visibleDescendants)
    {
        var parts = new List<string>();
        if (unsupported > 0) parts.Add($"{unsupported} items had unsupported properties");
        if (clrTypeNames > 0) parts.Add($"{clrTypeNames} items had CLR type name text");
        if (visibleDescendants > 0) parts.Add($"{visibleDescendants} items used visible descendant fallback");
        return parts.Count > 0 ? $" ({string.Join(", ", parts)})" : "";
    }

    [McpServerTool(Name = "file_dialog_select"), Description(
        "Complete a standard Windows Open/Save file dialog by entering a file path and clicking the accept button. " +
        "Works with standard Windows file dialogs that have a 'File name:' ComboBox and an accept button (Open, Save, etc.). " +
        "Handles the common ambiguity where multiple buttons share the name 'Open' by targeting the dialog action button.")]
    public static string FileDialogSelect(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle of the file dialog from attach_application")] string windowHandle,
        [Description("Full file path to enter in the File name field")] string filePath,
        [Description("Name of the accept button (default 'Open'). Use 'Save' for Save dialogs.")] string acceptButtonName = "Open")
    {
        return Task.Run(() =>
        {
            try
            {
                var window = flaUI.GetCachedWindow(windowHandle);
                window.SetForeground();
                Wait.UntilInputIsProcessed();

                // Step 1: Find the File name edit field
                // Standard file dialog: ComboBox "File name:" with automationId "1148", containing an Edit child
                AutomationElement? fileNameEdit = null;
                string editMethod = "";

                // Strategy 1: Find Edit child inside the "File name:" ComboBox by automationId
                var fileNameCombo = resolver.FindElement(window, automationId: "1148");
                if (fileNameCombo != null)
                {
                    try
                    {
                        var edits = fileNameCombo.FindAllChildren(cf => cf.ByControlType(ControlType.Edit));
                        if (edits.Length > 0)
                        {
                            fileNameEdit = edits[0];
                            editMethod = "ComboBox(id=1148)/Edit";
                        }
                    }
                    catch { /* fall through */ }
                }

                // Strategy 2: Fallback — find the ComboBox by name "File name:"
                if (fileNameEdit == null)
                {
                    fileNameCombo = resolver.FindElement(window, name: "File name:", controlType: "ComboBox");
                    if (fileNameCombo != null)
                    {
                        try
                        {
                            var edits = fileNameCombo.FindAllChildren(cf => cf.ByControlType(ControlType.Edit));
                            if (edits.Length > 0)
                            {
                                fileNameEdit = edits[0];
                                editMethod = "ComboBox('File name:')/Edit";
                            }
                        }
                        catch { /* fall through */ }
                    }
                }

                // Strategy 3: Find any Edit with automationId "1148" directly
                if (fileNameEdit == null)
                {
                    fileNameEdit = resolver.FindElement(window, automationId: "1148", controlType: "Edit");
                    if (fileNameEdit != null)
                        editMethod = "Edit(id=1148)";
                }

                if (fileNameEdit == null)
                    return "Error: Could not find the File name edit field. This may not be a standard Windows file dialog.";

                // Step 2: Set the file path
                if (fileNameEdit.Patterns.Value.IsSupported)
                {
                    fileNameEdit.Patterns.Value.Pattern.SetValue(filePath);
                }
                else
                {
                    // Fallback: focus and type
                    fileNameEdit.Focus();
                    Wait.UntilInputIsProcessed();
                    Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
                    Keyboard.Type(VirtualKeyShort.DELETE);
                    Wait.UntilInputIsProcessed();
                    Keyboard.Type(filePath);
                }
                Wait.UntilInputIsProcessed();

                // Step 3: Find and click the accept button
                // Standard dialog accept button has automationId "1"
                AutomationElement? acceptButton = null;
                string buttonMethod = "";

                // Strategy 1: By automationId "1" (most reliable for standard dialogs)
                var candidate = resolver.FindElement(window, automationId: "1");
                if (candidate != null && SafeUIA.SafeGetControlType(candidate) == "Button")
                {
                    acceptButton = candidate;
                    buttonMethod = "Button(id=1)";
                }

                // Strategy 2: Use ranked resolution by name (handles ambiguity)
                if (acceptButton == null)
                {
                    var resolved = resolver.ResolveElement(window, name: acceptButtonName, controlType: "Button");
                    if (resolved.Element != null)
                    {
                        acceptButton = resolved.Element;
                        buttonMethod = resolved.IsAmbiguous ? $"ranked({acceptButtonName})" : $"Button('{acceptButtonName}')";
                    }
                }

                if (acceptButton == null)
                    return $"Error: Could not find the accept button ('{acceptButtonName}'). File path was entered via {editMethod}.";

                // Invoke the accept button
                if (acceptButton.Patterns.Invoke.IsSupported)
                {
                    acceptButton.Patterns.Invoke.Pattern.Invoke();
                }
                else
                {
                    acceptButton.Click();
                }
                Wait.UntilInputIsProcessed();

                return $"File dialog completed. Path='{filePath}', editMethod={editMethod}, buttonMethod={buttonMethod}.";
            }
            catch (Exception ex)
            {
                var category = ToolResult.Classify(ex);
                return $"Error [{category}]: {ex.Message}";
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
        "Set focus to a specific element and bring the window to the foreground. " +
        "Use rootAutomationId to scope the search to a subtree.")]
    public static string FocusElement(
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
                window.SetForeground();

                if (automationId == null && name == null && controlType == null && xpath == null && rootAutomationId == null)
                {
                    Wait.UntilInputIsProcessed();
                    return $"Brought window to foreground: {window.Name}";
                }

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
