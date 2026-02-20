using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Microsoft.Extensions.Logging;

namespace WinAppMCP.Services;

/// <summary>
/// Singleton service managing FlaUI's UIA3Automation instance and a cache of attached windows.
/// </summary>
public sealed class FlaUIService : IDisposable
{
    private readonly UIA3Automation _automation;
    private readonly ILogger<FlaUIService> _logger;
    private readonly ConcurrentDictionary<string, WindowEntry> _windowCache = new();

    public FlaUIService(UIA3Automation automation, ILogger<FlaUIService> logger)
    {
        _automation = automation;
        _logger = logger;
    }

    /// <summary>
    /// Get all top-level windows visible on the desktop.
    /// </summary>
    public IReadOnlyList<WindowInfo> GetAllTopLevelWindows(string? processNameFilter = null)
    {
        var desktop = _automation.GetDesktop();
        var windows = desktop.FindAllChildren(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Window));

        var results = new List<WindowInfo>();
        foreach (var win in windows)
        {
            try
            {
                var processId = win.Properties.ProcessId.ValueOrDefault;
                var process = Process.GetProcessById(processId);
                var processName = process.ProcessName;

                if (processNameFilter != null &&
                    !processName.Contains(processNameFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                results.Add(new WindowInfo
                {
                    Handle = win.Properties.NativeWindowHandle.ValueOrDefault.ToString("X"),
                    Title = win.Name ?? string.Empty,
                    ProcessName = processName,
                    ProcessId = processId
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get info for a window element");
            }
        }

        return results;
    }

    /// <summary>
    /// Attach to a running application by process name and cache the main window.
    /// </summary>
    public WindowInfo AttachToProcess(string processName)
    {
        _logger.LogInformation("Attaching to process: {ProcessName}", processName);

        var app = FlaUI.Core.Application.Attach(processName);
        var window = app.GetMainWindow(_automation, TimeSpan.FromSeconds(10));

        if (window == null)
            throw new InvalidOperationException($"Could not find main window for process '{processName}'.");

        return CacheWindow(app, window);
    }

    /// <summary>
    /// Attach to a running application by process ID and cache the main window.
    /// </summary>
    public WindowInfo AttachToProcess(int processId)
    {
        _logger.LogInformation("Attaching to process ID: {ProcessId}", processId);

        var app = FlaUI.Core.Application.Attach(processId);
        var window = app.GetMainWindow(_automation, TimeSpan.FromSeconds(10));

        if (window == null)
            throw new InvalidOperationException($"Could not find main window for process ID {processId}.");

        return CacheWindow(app, window);
    }

    /// <summary>
    /// Retrieve a cached window by its handle. Validates it is still available.
    /// </summary>
    public Window GetCachedWindow(string windowHandle)
    {
        if (!_windowCache.TryGetValue(windowHandle, out var entry))
            throw new InvalidOperationException(
                $"Window handle '{windowHandle}' not found in cache. Call 'attach_application' first to attach to a running application.");

        // Validate the window is still alive
        try
        {
            if (!entry.Window.IsAvailable)
            {
                _windowCache.TryRemove(windowHandle, out _);
                throw new InvalidOperationException(
                    $"Window '{windowHandle}' is no longer available. The application may have closed. Call 'attach_application' again.");
            }
        }
        catch (Exception) when (entry.Window == null)
        {
            _windowCache.TryRemove(windowHandle, out _);
            throw;
        }

        return entry.Window;
    }

    /// <summary>
    /// List all currently cached (attached) windows.
    /// </summary>
    public IReadOnlyList<WindowInfo> GetAttachedWindows()
    {
        var results = new List<WindowInfo>();
        var staleKeys = new List<string>();

        foreach (var (handle, entry) in _windowCache)
        {
            try
            {
                if (entry.Window.IsAvailable)
                {
                    results.Add(new WindowInfo
                    {
                        Handle = handle,
                        Title = entry.Window.Name ?? string.Empty,
                        ProcessName = entry.App.Name ?? string.Empty,
                        ProcessId = entry.App.ProcessId
                    });
                }
                else
                {
                    staleKeys.Add(handle);
                }
            }
            catch
            {
                staleKeys.Add(handle);
            }
        }

        // Clean up stale entries
        foreach (var key in staleKeys)
            _windowCache.TryRemove(key, out _);

        return results;
    }

    /// <summary>
    /// Serialize the UI tree of an element to a compact text format.
    /// </summary>
    public string SerializeTree(AutomationElement root, int maxDepth = 3, int currentDepth = 0)
    {
        if (currentDepth > maxDepth)
            return string.Empty;

        var sb = new StringBuilder();
        var indent = new string(' ', currentDepth * 2);

        var controlType = root.ControlType.ToString();
        var name = root.Name ?? string.Empty;
        var automationId = root.AutomationId ?? string.Empty;
        var className = root.ClassName ?? string.Empty;
        var isEnabled = root.IsEnabled;

        // Build compact line
        var parts = new List<string> { $"{indent}[{controlType}]" };

        if (!string.IsNullOrWhiteSpace(name))
            parts.Add($"\"{name}\"");

        if (!string.IsNullOrWhiteSpace(automationId))
            parts.Add($"id=\"{automationId}\"");

        if (!string.IsNullOrWhiteSpace(className))
            parts.Add($"class=\"{className}\"");

        if (!isEnabled)
            parts.Add("(disabled)");

        sb.AppendLine(string.Join(" ", parts));

        // Recurse into children
        try
        {
            var children = root.FindAllChildren();
            foreach (var child in children)
            {
                sb.Append(SerializeTree(child, maxDepth, currentDepth + 1));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to enumerate children of element");
        }

        return sb.ToString();
    }

    public void Dispose()
    {
        _windowCache.Clear();
        _automation.Dispose();
    }

    private WindowInfo CacheWindow(FlaUI.Core.Application app, Window window)
    {
        var handle = window.Properties.NativeWindowHandle.ValueOrDefault.ToString("X");

        var info = new WindowInfo
        {
            Handle = handle,
            Title = window.Name ?? string.Empty,
            ProcessName = app.Name ?? string.Empty,
            ProcessId = app.ProcessId
        };

        _windowCache[handle] = new WindowEntry(app, window);

        _logger.LogInformation("Cached window: {Handle} - {Title}", handle, info.Title);
        return info;
    }

    private sealed record WindowEntry(FlaUI.Core.Application App, Window Window);
}

/// <summary>
/// Compact window information DTO.
/// </summary>
public sealed class WindowInfo
{
    public string Handle { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public int ProcessId { get; init; }
}
