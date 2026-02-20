using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using WinAppMCP.Services;

namespace WinAppMCP.Tools;

/// <summary>
/// MCP tools for discovering and attaching to Windows applications.
/// </summary>
[McpServerToolType]
public static class WindowTools
{
    [McpServerTool(Name = "list_windows"), Description(
        "List all top-level windows visible on the desktop. " +
        "Optionally filter by process name. Returns a compact table of PID, handle, title, and process name.")]
    public static string ListWindows(
        FlaUIService flaUI,
        [Description("Optional process name filter (case-insensitive partial match). Example: 'notepad', 'chrome'")]
        string? processName = null)
    {
        return Task.Run(() =>
        {
            var windows = flaUI.GetAllTopLevelWindows(processName);

            if (windows.Count == 0)
            {
                return processName != null
                    ? $"No windows found matching process name '{processName}'."
                    : "No top-level windows found.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {windows.Count} window(s):");
            sb.AppendLine($"{"PID",-8} {"Handle",-12} {"Process",-25} Title");
            sb.AppendLine(new string('-', 80));

            foreach (var w in windows)
            {
                sb.AppendLine($"{w.ProcessId,-8} {w.Handle,-12} {w.ProcessName,-25} {w.Title}");
            }

            return sb.ToString();
        }).Result;
    }

    [McpServerTool(Name = "attach_application"), Description(
        "Attach to a running Windows application by process name or process ID. " +
        "Returns the window handle needed for all subsequent tools. " +
        "The application must already be running — this tool does not launch applications.")]
    public static string AttachApplication(
        FlaUIService flaUI,
        [Description("Process name to attach to (e.g., 'notepad', 'MyMauiApp'). Mutually exclusive with processId.")]
        string? processName = null,
        [Description("Process ID to attach to. Mutually exclusive with processName.")]
        int? processId = null)
    {
        if (string.IsNullOrWhiteSpace(processName) && processId == null)
            return "Error: You must provide either 'processName' or 'processId'.";

        if (!string.IsNullOrWhiteSpace(processName) && processId != null)
            return "Error: Provide either 'processName' or 'processId', not both.";

        return Task.Run(() =>
        {
            try
            {
                var info = processId.HasValue
                    ? flaUI.AttachToProcess(processId.Value)
                    : flaUI.AttachToProcess(processName!);

                return $"Successfully attached.\n" +
                       $"  Handle: {info.Handle}\n" +
                       $"  Title: {info.Title}\n" +
                       $"  Process: {info.ProcessName} (PID: {info.ProcessId})\n\n" +
                       $"Use windowHandle=\"{info.Handle}\" in subsequent tool calls.";
            }
            catch (Exception ex)
            {
                return $"Error attaching to application: {ex.Message}";
            }
        }).Result;
    }

    [McpServerTool(Name = "list_attached"), Description(
        "List all currently attached (cached) windows. " +
        "Useful to see which applications are already connected.")]
    public static string ListAttached(FlaUIService flaUI)
    {
        return Task.Run(() =>
        {
            var windows = flaUI.GetAttachedWindows();

            if (windows.Count == 0)
                return "No attached windows. Use 'attach_application' to connect to a running application.";

            var sb = new StringBuilder();
            sb.AppendLine($"{windows.Count} attached window(s):");
            foreach (var w in windows)
            {
                sb.AppendLine($"  Handle: {w.Handle} | {w.ProcessName} (PID: {w.ProcessId}) | \"{w.Title}\"");
            }
            return sb.ToString();
        }).Result;
    }
}
