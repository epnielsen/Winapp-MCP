# WinApp-MCP

An MCP (Model Context Protocol) server that gives AI coding agents "eyes" on Windows desktop applications using [FlaUI](https://github.com/FlaUI/FlaUI) and Windows UI Automation (UIA3).

Works with any Windows application that exposes a standard UI Automation tree — including **WPF**, **WinForms**, **WinUI 3**, and **.NET MAUI** apps.

## Prerequisites

- **Windows 10/11**
- **.NET 10 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet/10.0)
- An MCP-capable AI client (Claude Desktop, VS Code Copilot, Cursor, etc.)

## Build & Run

```bash
cd WinApp-MCP
dotnet build
dotnet run
```

The server communicates over **stdio** (stdin/stdout) using the MCP protocol.

## Client Configuration

### Claude Desktop / VS Code Copilot / Cursor

Add to your MCP configuration:

```json
{
  "mcpServers": {
    "winapp-mcp": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\path\\to\\WinApp-MCP\\WinApp-MCP.csproj"]
    }
  }
}
```

Or run the compiled binary directly:

```json
{
  "mcpServers": {
    "winapp-mcp": {
      "command": "C:\\path\\to\\WinApp-MCP\\bin\\Debug\\net10.0-windows\\win-x64\\WinApp-MCP.exe"
    }
  }
}
```

## Tools Reference

### Discovery

| Tool | Description |
|---|---|
| `list_windows` | List all top-level windows (optional `processName` filter) |
| `attach_application` | Attach to a running app by `processName` or `processId` |
| `list_attached` | Show currently attached windows |

### Inspection

| Tool | Description |
|---|---|
| `get_window_tree` | Get the UI element tree in compact text format (configurable `maxDepth`) |
| `find_elements` | Find elements by `automationId`, `name`, `controlType`, or `xpath` |
| `get_element_properties` | Get detailed properties and supported UIA patterns for an element |

### Interaction

| Tool | Description |
|---|---|
| `click_element` | Click an element (left/right/double) |
| `invoke_element` | Invoke via UIA InvokePattern (no mouse, preferred for buttons) |
| `type_text` | Type text into an input field (with optional clear-first) |
| `toggle_element` | Toggle a CheckBox/ToggleButton |
| `select_item` | Select item in ComboBox/ListBox by value or index |
| `send_keys` | Send keyboard shortcuts (e.g., `Ctrl+A`, `Enter`, `Alt+F4`) |
| `focus_element` | Focus an element / bring window to foreground |

### Capture & Wait

| Tool | Description |
|---|---|
| `screenshot` | Capture a window or element as base64 PNG (max 1280px wide) |
| `wait_for_element` | Wait for an element to appear (configurable timeout) |

## Example Agent Workflow

```
1. list_windows(processName: "MyMauiApp")
   → PID: 12345, Handle: "1A2B3C", Title: "My App"

2. attach_application(processName: "MyMauiApp")
   → Handle: "1A2B3C"

3. get_window_tree(windowHandle: "1A2B3C", maxDepth: 3)
   → [Window] "My App" id="" class="WinUIDesktopWin32WindowClass"
       [Text] "Welcome" id="lblWelcome"
       [Edit] "" id="txtUsername" class="TextBox"
       [Edit] "" id="txtPassword" class="PasswordBox"
       [Button] "Sign In" id="btnSignIn"

4. type_text(windowHandle: "1A2B3C", automationId: "txtUsername", text: "admin")
   → Typed text via ValuePattern

5. type_text(windowHandle: "1A2B3C", automationId: "txtPassword", text: "pass123")
   → Typed text via ValuePattern

6. invoke_element(windowHandle: "1A2B3C", automationId: "btnSignIn")
   → Invoked: [Button] "Sign In" id="btnSignIn"

7. wait_for_element(windowHandle: "1A2B3C", name: "Dashboard", timeoutMs: 5000)
   → Element found

8. screenshot(windowHandle: "1A2B3C")
   → [Screenshot captured: 1280x720px]
```

## Element Identification

Elements can be found using (in priority order):

1. **`automationId`** — Most reliable. Set by developers, unique within a window.
2. **`xpath`** — Structural path like `//Button[@Name='OK']`. Good when AutomationId is missing.
3. **`name` + `controlType`** — Display text + type combo. Useful for labeled controls.

## Troubleshooting

| Issue | Solution |
|---|---|
| **No windows found** | Ensure the target app is running. Some apps need to be started with admin rights. |
| **Empty UI tree** | The app may use custom-drawn controls that don't expose UIA. Try `screenshot` instead. |
| **Element not found** | Use `get_window_tree` with higher `maxDepth` to explore. Check AutomationId in the tree output. |
| **Click doesn't work** | Try `invoke_element` instead (uses UIA pattern, no mouse). Some apps block programmatic mouse input. |
| **Screenshot fails** | Ensure the window is not minimized. The server needs a desktop session (not headless). |
| **DPI/coordinate issues** | Ensure the process is DPI-aware. BoundingRectangle values are in physical pixels. |
| **Logging to stdout breaks MCP** | All logging routes to stderr by default. Don't add `Console.WriteLine` calls. |

## Architecture

```
WinApp-MCP/
├── Program.cs                  # Host builder, DI, MCP server setup
├── Services/
│   ├── FlaUIService.cs         # Singleton: UIA3Automation, window cache, tree serialization
│   └── ElementResolver.cs     # Element lookup by AutomationId/XPath/Name+ControlType
├── Models/
│   └── ElementInfo.cs          # Compact DTO for element properties
└── Tools/
    ├── WindowTools.cs          # list_windows, attach_application, list_attached
    ├── InspectionTools.cs      # get_window_tree, find_elements, get_element_properties
    ├── InteractionTools.cs     # click, invoke, type_text, toggle, select, send_keys, focus
    ├── CaptureTools.cs         # screenshot
    └── WaitTools.cs            # wait_for_element
```

## License

MIT
