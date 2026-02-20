using System.ComponentModel;
using System.Text.Json;
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
        "Optionally provide rootAutomationId to inspect a subtree instead of the full window.")]
    public static string GetWindowTree(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("Maximum tree depth to traverse (default 3, max 8)")] int maxDepth = 3,
        [Description("Optional AutomationId of subtree root element to inspect")] string? rootAutomationId = null)
    {
        return Task.Run(() =>
        {
            try
            {
                var window = flaUI.GetCachedWindow(windowHandle);
                maxDepth = Math.Clamp(maxDepth, 1, 8);

                FlaUI.Core.AutomationElements.AutomationElement root = window;

                if (!string.IsNullOrWhiteSpace(rootAutomationId))
                {
                    var subtreeRoot = resolver.FindElement(window, automationId: rootAutomationId);
                    if (subtreeRoot == null)
                        return $"Error: Could not find subtree root element with AutomationId='{rootAutomationId}'.";
                    root = subtreeRoot;
                }

                var tree = flaUI.SerializeTree(root, maxDepth);

                if (string.IsNullOrWhiteSpace(tree))
                    return "The UI tree is empty. The window may not have finished loading.";

                return tree;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }).Result;
    }

    [McpServerTool(Name = "find_elements"), Description(
        "Find UI elements matching search criteria. Returns a JSON array of matching elements " +
        "with their AutomationId, Name, ControlType, ClassName, IsEnabled, and BoundingRectangle. " +
        "Search priority: automationId > xpath > name+controlType.")]
    public static string FindElements(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("AutomationId to search for (exact match, most reliable)")] string? automationId = null,
        [Description("Element name/text to search for (exact match)")] string? name = null,
        [Description("Control type filter: Button, TextBox, CheckBox, ComboBox, MenuItem, etc.")] string? controlType = null,
        [Description("XPath expression like //Button[@Name='OK'] or /Menu/MenuItem[@Name='File']")] string? xpath = null)
    {
        return Task.Run(() =>
        {
            try
            {
                var window = flaUI.GetCachedWindow(windowHandle);
                var elements = resolver.FindElements(window, automationId, name, controlType, xpath);

                if (elements.Count == 0)
                {
                    var search = ElementResolver.DescribeSearch(automationId, name, controlType, xpath);
                    return $"No elements found matching: {search}";
                }

                var infos = elements.Select(ElementInfo.FromElement).ToArray();
                return JsonSerializer.Serialize(infos, new JsonSerializerOptions
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
}
