using System.ComponentModel;
using FlaUI.Core.Tools;
using ModelContextProtocol.Server;
using WinAppMCP.Models;
using WinAppMCP.Services;

namespace WinAppMCP.Tools;

/// <summary>
/// MCP tools for waiting on UI elements to appear or become available.
/// </summary>
[McpServerToolType]
public static class WaitTools
{
    [McpServerTool(Name = "wait_for_element"), Description(
        "Wait for a UI element to appear in the window. " +
        "Useful after navigating or triggering operations that load new UI. " +
        "Returns element info on success, or a timeout error.")]
    public static string WaitForElement(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("AutomationId of the element to wait for")] string? automationId = null,
        [Description("Name/text of the element to wait for")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("XPath expression")] string? xpath = null,
        [Description("Maximum time to wait in milliseconds (default 5000, max 30000)")] int timeoutMs = 5000)
    {
        return Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(automationId) &&
                    string.IsNullOrWhiteSpace(name) &&
                    string.IsNullOrWhiteSpace(xpath))
                {
                    return "Error: Provide at least one search criterion (automationId, name, or xpath).";
                }

                var window = flaUI.GetCachedWindow(windowHandle);
                timeoutMs = Math.Clamp(timeoutMs, 500, 30000);

                var search = ElementResolver.DescribeSearch(automationId, name, controlType, xpath);

                FlaUI.Core.AutomationElements.AutomationElement? found = null;

                var result = Retry.WhileNull(
                    () =>
                    {
                        found = resolver.FindElement(window, automationId, name, controlType, xpath);
                        return found;
                    },
                    timeout: TimeSpan.FromMilliseconds(timeoutMs),
                    interval: TimeSpan.FromMilliseconds(250));

                if (result.Result != null)
                {
                    var info = ElementInfo.FromElement(result.Result);
                    return $"Element found after waiting: {info.ToCompactString()}";
                }

                return $"Timeout ({timeoutMs}ms): Element not found matching: {search}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }).Result;
    }
}
