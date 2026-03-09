using System.ComponentModel;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using ModelContextProtocol.Server;
using WinAppMCP.Models;
using WinAppMCP.Services;

namespace WinAppMCP.Tools;

/// <summary>
/// MCP tools for inspecting the UI element tree of Windows applications.
/// </summary>
[McpServerToolType]
public static class InspectionTools
{
    [McpServerTool(Name = "get_window_tree"), Description(
        "Get the UI element tree of a window in compact text format. " +
        "Each line shows [ControlType] \"Name\" id=\"AutomationId\" class=\"ClassName\". " +
        "Use maxDepth to control tree depth (default 3). " +
        "Use propertyProfile to control how many properties are read per node: " +
        "'minimal' (ControlType+Name only, fastest), 'standard' (default, +AutomationId+ClassName+enabled), " +
        "'diagnostic' (+SupportedPatterns+BoundingRectangle). " +
        "Optionally provide rootAutomationId to inspect a subtree instead of the full window.")]
    public static string GetWindowTree(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("Maximum tree depth to traverse (default 3, max 8)")] int maxDepth = 3,
        [Description("Optional AutomationId of subtree root element to inspect")] string? rootAutomationId = null,
        [Description("Property profile: 'minimal', 'standard' (default), or 'diagnostic'")] string propertyProfile = "standard")
    {
        return Task.Run(() =>
        {
            try
            {
                var window = flaUI.GetCachedWindow(windowHandle);
                maxDepth = Math.Clamp(maxDepth, 1, 8);

                // Normalize profile
                var profile = propertyProfile?.ToLowerInvariant() switch
                {
                    "minimal" => "minimal",
                    "diagnostic" => "diagnostic",
                    _ => "standard"
                };

                FlaUI.Core.AutomationElements.AutomationElement root = window;

                if (!string.IsNullOrWhiteSpace(rootAutomationId))
                {
                    var subtreeRoot = resolver.FindElement(window, automationId: rootAutomationId);
                    if (subtreeRoot == null)
                        return $"Error: Could not find subtree root element with AutomationId='{rootAutomationId}'.";
                    root = subtreeRoot;
                }

                var tree = flaUI.SerializeTree(root, maxDepth, profile: profile);

                if (string.IsNullOrWhiteSpace(tree))
                    return "The UI tree is empty. The window may not have finished loading.";

                return tree;
            }
            catch (Exception ex)
            {
                var category = ToolResult.Classify(ex);
                return $"Error [{category}]: {ex.Message}";
            }
        }).Result;
    }

    [McpServerTool(Name = "find_elements"), Description(
        "Find UI elements matching search criteria. Returns a JSON array of matching elements " +
        "with their AutomationId, Name, ControlType, ClassName, IsEnabled, and BoundingRectangle. " +
        "Search priority: automationId > xpath > name+controlType. " +
        "Can search by controlType alone to discover elements without knowing their name or id. " +
        "Optionally provide rootAutomationId to search within a subtree instead of the full window.")]
    public static string FindElements(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("AutomationId to search for (exact match, most reliable)")] string? automationId = null,
        [Description("Element name/text to search for (exact match)")] string? name = null,
        [Description("Control type filter: Button, TextBox, CheckBox, ComboBox, MenuItem, Text, etc.")] string? controlType = null,
        [Description("XPath expression like //Button[@Name='OK'] or /Menu/MenuItem[@Name='File']")] string? xpath = null,
        [Description("Only return elements that are visible on screen (default false)")] bool visibleOnly = false,
        [Description("Only return elements that are enabled (default false)")] bool enabledOnly = false,
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
                    var subtreeRoot = resolver.FindElement(window, automationId: rootAutomationId);
                    if (subtreeRoot == null)
                        return $"Error: Could not find subtree root element with AutomationId='{rootAutomationId}'.";
                    searchRoot = subtreeRoot;
                }

                var elements = resolver.FindElements(searchRoot, automationId, name, controlType, xpath,
                    visibleOnly: visibleOnly, enabledOnly: enabledOnly);

                if (elements.Count == 0)
                {
                    var search = ElementResolver.DescribeSearch(automationId, name, controlType, xpath);
                    return $"No elements found matching: {search}";
                }

                // Per-element fault tolerance: skip elements that fail to serialize
                var infos = new List<ElementInfo>();
                foreach (var el in elements)
                {
                    try
                    {
                        infos.Add(ElementInfo.FromElement(el));
                    }
                    catch
                    {
                        // Skip elements that can't be inspected
                    }
                }

                if (infos.Count == 0)
                    return $"Found {elements.Count} elements but all failed to serialize their properties.";

                return JsonSerializer.Serialize(infos, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
            catch (Exception ex)
            {
                var category = ToolResult.Classify(ex);
                return $"Error [{category}]: {ex.Message}";
            }
        }).Result;
    }

    [McpServerTool(Name = "get_element_properties"), Description(
        "Get detailed properties and supported UIA patterns for a specific element. " +
        "Returns all available properties including IsEnabled, BoundingRectangle, " +
        "and which interaction patterns are supported (Invoke, Value, Toggle, Selection, etc.).")]
    public static string GetElementProperties(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type: Button, TextBox, CheckBox, etc.")] string? controlType = null,
        [Description("XPath expression")] string? xpath = null)
    {
        return Task.Run(() =>
        {
            try
            {
                var window = flaUI.GetCachedWindow(windowHandle);
                var element = resolver.FindElement(window, automationId, name, controlType, xpath);

                if (element == null)
                {
                    var search = ElementResolver.DescribeSearch(automationId, name, controlType, xpath);
                    return $"Element not found matching: {search}";
                }

                var info = ElementInfo.FromElement(element);
                return JsonSerializer.Serialize(info, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }).Result;
    }

    [McpServerTool(Name = "get_selectable_items"), Description(
        "Enumerate items in a ComboBox or ListBox with safe, provider-tolerant metadata. " +
        "Returns a JSON array with each item's index, text (user-visible summary when available), " +
        "raw provider text, text source, selection state, summary parts, and unsupported properties. " +
        "When item text is a CLR type name (templated row), automatically extracts visible descendant text. " +
        "Set includeActions=true to discover row-level action controls (buttons, toggles) within each item. " +
        "Expands ComboBox if collapsed to force items to load, then collapses after.")]
    public static string GetSelectableItems(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("AutomationId of the ComboBox/ListBox")] string? automationId = null,
        [Description("Name of the ComboBox/ListBox")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("XPath expression")] string? xpath = null,
        [Description("Include row-level action controls (buttons, toggles) in each item (default false)")] bool includeActions = false)
    {
        return Task.Run(() =>
        {
            try
            {
                var window = flaUI.GetCachedWindow(windowHandle);
                var element = resolver.FindElement(window, automationId, name, controlType, xpath);

                if (element == null)
                    return $"Error: Element not found. {ElementResolver.DescribeSearch(automationId, name, controlType, xpath)}";

                // Expand if collapsed
                bool wasExpanded = false;
                try
                {
                    if (element.Patterns.ExpandCollapse.IsSupported)
                    {
                        var state = element.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.ValueOrDefault;
                        if (state == ExpandCollapseState.Collapsed)
                        {
                            element.Patterns.ExpandCollapse.Pattern.Expand();
                            FlaUI.Core.Input.Wait.UntilInputIsProcessed();
                            Thread.Sleep(100);
                            wasExpanded = true;
                        }
                    }
                }
                catch { /* best effort */ }

                try
                {
                    // Get items
                    AutomationElement[] items;
                    var comboBox = element.AsComboBox();
                    var listBox = element.AsListBox();

                    if (comboBox != null)
                        items = comboBox.Items.Select(i => (AutomationElement)i).ToArray();
                    else if (listBox != null)
                        items = listBox.Items.Select(i => (AutomationElement)i).ToArray();
                    else
                        return $"Error: Element is not a ComboBox or ListBox. {ElementInfo.FromElement(element).ToCompactString()}";

                    var itemInfos = new List<object>();
                    for (int i = 0; i < items.Length; i++)
                    {
                        try
                        {
                            var item = items[i];
                            var (text, parts, rawProvider, source) = SafeUIA.ExtractVisibleSummary(item);

                            bool isSelected = false;
                            try
                            {
                                if (item.Patterns.SelectionItem.IsSupported)
                                    isSelected = item.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault;
                            }
                            catch { /* unknown selection state */ }

                            var unsupported = new List<string>();
                            if (SafeUIA.SafeGetName(item) == SafeUIA.NotSupported)
                                unsupported.Add("Name");
                            if (SafeUIA.SafeGetAutomationId(item) == SafeUIA.NotSupported)
                                unsupported.Add("AutomationId");

                            object itemObj;
                            if (includeActions)
                            {
                                var actions = SafeUIA.DiscoverRowActions(item);
                                itemObj = new
                                {
                                    index = i,
                                    text = string.IsNullOrEmpty(text) ? "<Unknown>" : text,
                                    textSource = source,
                                    rawProviderText = rawProvider,
                                    summaryParts = parts,
                                    isSelected,
                                    unsupportedProperties = unsupported.ToArray(),
                                    primaryActions = actions.Select(a => new { name = a.Name, automationId = a.AutomationId, controlType = a.ControlType }).ToArray()
                                };
                            }
                            else
                            {
                                itemObj = new
                                {
                                    index = i,
                                    text = string.IsNullOrEmpty(text) ? "<Unknown>" : text,
                                    textSource = source,
                                    rawProviderText = rawProvider,
                                    summaryParts = parts,
                                    isSelected,
                                    unsupportedProperties = unsupported.ToArray()
                                };
                            }

                            itemInfos.Add(itemObj);
                        }
                        catch (Exception ex)
                        {
                            itemInfos.Add(new
                            {
                                index = i,
                                text = "<Error>",
                                textSource = "None",
                                rawProviderText = "",
                                summaryParts = Array.Empty<string>(),
                                isSelected = false,
                                unsupportedProperties = new[] { $"Error: {ex.Message}" }
                            });
                        }
                    }

                    return JsonSerializer.Serialize(itemInfos, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });
                }
                finally
                {
                    if (wasExpanded)
                    {
                        try
                        {
                            element.Patterns.ExpandCollapse.Pattern.Collapse();
                            FlaUI.Core.Input.Wait.UntilInputIsProcessed();
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

    [McpServerTool(Name = "get_child_controls"), Description(
        "Discover child controls within a composite parent element and classify them by interaction type. " +
        "Returns a JSON array of child elements with their properties and an interactionType field " +
        "(edit, button, toggle, picker, expander, label, other). " +
        "Useful for composite controls where the parent is weakly surfaced but children are actionable.")]
    public static string GetChildControls(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("AutomationId of the parent element")] string? automationId = null,
        [Description("Name of the parent element")] string? name = null,
        [Description("Control type of the parent")] string? controlType = null,
        [Description("XPath expression for the parent")] string? xpath = null,
        [Description("Maximum depth to search for child controls (default 2, max 4)")] int maxDepth = 2)
    {
        return Task.Run(() =>
        {
            try
            {
                var window = flaUI.GetCachedWindow(windowHandle);
                var element = resolver.FindElement(window, automationId, name, controlType, xpath);

                if (element == null)
                    return $"Error: Element not found. {ElementResolver.DescribeSearch(automationId, name, controlType, xpath)}";

                maxDepth = Math.Clamp(maxDepth, 1, 4);
                var parentInfo = ElementInfo.FromElement(element);

                var children = CollectChildren(element, maxDepth, currentDepth: 0);

                var result = new
                {
                    parent = new
                    {
                        automationId = parentInfo.AutomationId,
                        name = parentInfo.Name,
                        controlType = parentInfo.ControlType,
                        className = parentInfo.ClassName,
                        supportedPatterns = parentInfo.SupportedPatterns
                    },
                    childCount = children.Count,
                    children = children
                };

                return JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
            catch (Exception ex)
            {
                var category = ToolResult.Classify(ex);
                return $"Error [{category}]: {ex.Message}";
            }
        }).Result;
    }

    private static List<object> CollectChildren(AutomationElement parent, int maxDepth, int currentDepth)
    {
        var results = new List<object>();
        if (currentDepth >= maxDepth) return results;

        try
        {
            var children = parent.FindAllChildren();
            foreach (var child in children)
            {
                try
                {
                    var info = ElementInfo.FromElement(child);
                    var interactionType = SafeUIA.ClassifyInteractionType(child);

                    results.Add(new
                    {
                        automationId = info.AutomationId,
                        name = info.Name,
                        controlType = info.ControlType,
                        className = info.ClassName,
                        isEnabled = info.IsEnabled,
                        interactionType,
                        supportedPatterns = info.SupportedPatterns
                    });

                    // Recurse into children if not at max depth
                    if (currentDepth + 1 < maxDepth)
                    {
                        var grandchildren = CollectChildren(child, maxDepth, currentDepth + 1);
                        results.AddRange(grandchildren);
                    }
                }
                catch { /* skip uninspectable children */ }
            }
        }
        catch { /* parent may not support child enumeration */ }

        return results;
    }
}
