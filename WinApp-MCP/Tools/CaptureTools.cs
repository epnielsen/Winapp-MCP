using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using FlaUI.Core.Capturing;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WinAppMCP.Models;
using WinAppMCP.Services;

namespace WinAppMCP.Tools;

/// <summary>
/// MCP tools for capturing screenshots of windows and elements.
/// </summary>
[McpServerToolType]
public static class CaptureTools
{
    private const int MaxWidth = 1280;

    [McpServerTool(Name = "screenshot"), Description(
        "Capture a screenshot of a window or a specific element within it. " +
        "Returns the image as base64 PNG. If no element identifiers are provided, " +
        "captures the entire window. Images are resized to max 1280px width.")]
    public static string Screenshot(
        FlaUIService flaUI,
        ElementResolver resolver,
        [Description("Window handle from attach_application")] string windowHandle,
        [Description("AutomationId of a specific element to capture")] string? automationId = null,
        [Description("Name of a specific element to capture")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("XPath expression for element to capture")] string? xpath = null)
    {
        return Task.Run(() =>
        {
            try
            {
                var window = flaUI.GetCachedWindow(windowHandle);
                window.SetForeground();
                FlaUI.Core.Input.Wait.UntilInputIsProcessed();

                // Small delay to ensure window is fully rendered after foreground
                Thread.Sleep(200);

                CaptureImage capture;
                string description;

                var hasElementSearch = !string.IsNullOrWhiteSpace(automationId) ||
                                       !string.IsNullOrWhiteSpace(name) ||
                                       !string.IsNullOrWhiteSpace(xpath);

                if (hasElementSearch)
                {
                    var element = resolver.FindElement(window, automationId, name, controlType, xpath);
                    if (element == null)
                        return $"Error: Element not found. {ElementResolver.DescribeSearch(automationId, name, controlType, xpath)}";

                    capture = Capture.Element(element);
                    description = $"Element: {ElementInfo.FromElement(element).ToCompactString()}";
                }
                else
                {
                    capture = Capture.Element(window);
                    description = $"Window: \"{window.Name}\"";
                }

                using var bitmap = capture.Bitmap;

                // Resize if wider than MaxWidth
                using var resized = ResizeIfNeeded(bitmap);
                using var ms = new MemoryStream();
                resized.Save(ms, ImageFormat.Png);

                var base64 = Convert.ToBase64String(ms.ToArray());
                var width = resized.Width;
                var height = resized.Height;

                return $"[Screenshot captured: {width}x{height}px]\n" +
                       $"Description: {description}\n" +
                       $"Base64 PNG ({base64.Length} chars):\ndata:image/png;base64,{base64}";
            }
            catch (Exception ex)
            {
                return $"Error capturing screenshot: {ex.Message}";
            }
        }).Result;
    }

    private static Bitmap ResizeIfNeeded(Bitmap original)
    {
        if (original.Width <= MaxWidth)
            return new Bitmap(original); // Return a copy since original will be disposed

        var ratio = (double)MaxWidth / original.Width;
        var newHeight = (int)(original.Height * ratio);
        var resized = new Bitmap(MaxWidth, newHeight);

        using var graphics = Graphics.FromImage(resized);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(original, 0, 0, MaxWidth, newHeight);

        return resized;
    }
}
