# WinApp-MCP

[![Version](https://img.shields.io/badge/version-0.3.0-blue)](#changelog)

An MCP (Model Context Protocol) server that gives AI coding agents "eyes" and "hands" on Windows desktop applications using [FlaUI](https://github.com/FlaUI/FlaUI) and Windows UI Automation (UIA3).

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
| `get_window_tree` | Get the UI element tree in compact text format. Supports `maxDepth`, `rootAutomationId`, and `propertyProfile` (`minimal`, `standard`, `diagnostic`) |
| `find_elements` | Find elements by `automationId`, `name`, `controlType`, or `xpath`. Supports `visibleOnly`, `enabledOnly` filters and `rootAutomationId` for subtree-scoped search |
| `get_element_properties` | Get detailed properties and supported UIA patterns for an element |
| `get_selectable_items` | Enumerate items in a ComboBox/ListBox with visible-text summaries, CLR-type-name detection, raw provider text, and optional row-level action discovery (`includeActions`) |
| `get_child_controls` | Discover child controls within a composite parent, classified by interaction type (`edit`, `button`, `toggle`, `picker`, `expander`, `label`, `other`) |

### Text

| Tool | Description |
|---|---|
| `get_visible_text` | Read all visible text from a window or subtree in reading order |
| `get_element_text` | Read text/value from a specific element using a multi-strategy fallback chain |
| `set_text` | Set text in a text-capable control via ValuePattern (keyboard fallback opt-in) |

### Interaction

| Tool | Description |
|---|---|
| `click_element` | Click an element (left/right/double) |
| `invoke_element` | Invoke via UIA InvokePattern (no mouse, preferred for buttons) |
| `type_text` | Type text into an input field (with optional clear-first) |
| `toggle_element` | Toggle a CheckBox/ToggleButton |
| `select_item` | Select item in ComboBox/ListBox by value or index. Uses multi-strategy fallback for providers with incomplete metadata |
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

8. get_visible_text(windowHandle: "1A2B3C")
   → Found 3 text element(s):
       [Name] Welcome back, admin
       [Name] Dashboard
       [ValuePattern] Last login: 2026-03-08

9. screenshot(windowHandle: "1A2B3C")
   → [Screenshot captured: 1280x720px]
```

## Element Identification

Elements can be found using (in priority order):

1. **`automationId`** — Most reliable. Set by developers, unique within a window.
2. **`xpath`** — Structural path like `//Button[@Name='OK']`. Good when AutomationId is missing.
3. **`name` + `controlType`** — Display text + type combo. Useful for labeled controls.

## Provider Tolerance

WinApp-MCP is designed to work with real-world UI Automation providers that may expose incomplete or nonstandard properties:

- **Unsupported properties** are caught per-element and reported as `<NotSupported>` instead of failing the operation.
- **Tree inspection** continues past nodes that fail, emitting `[<Error>]` placeholders for uninspectable children.
- **Item selection** uses a multi-strategy fallback chain (Name → ValuePattern → LegacyIAccessible → text descendants) when provider metadata is incomplete.
- **Templated list rows** that return CLR type names (e.g. `Namespace.Model.ClassName`) instead of user-visible text are automatically resolved to visible descendant summaries.
- **Composite controls** with weak parent metadata can be explored via `get_child_controls`, which classifies children by interaction type.
- **Transient COM failures** (e.g. `E_UNEXPECTED`) are retried with element re-resolution.
- **Error categorization** distinguishes `ElementNotFound`, `PropertyNotSupported`, `TransientProviderFailure`, and `OperationNotSupported` so agents can make informed decisions.

## Troubleshooting

| Issue | Solution |
|---|---|
| **No windows found** | Ensure the target app is running. Some apps need to be started with admin rights. |
| **Empty UI tree** | The app may use custom-drawn controls that don't expose UIA. Try `screenshot` instead. |
| **Element not found** | Use `get_window_tree` with higher `maxDepth` to explore. Check AutomationId in the tree output. |
| **Click doesn't work** | Try `invoke_element` instead (uses UIA pattern, no mouse). Some apps block programmatic mouse input. |
| **Properties not supported** | Use `get_window_tree` with `propertyProfile: "minimal"` for maximum reliability, or `"diagnostic"` for full detail. |
| **ComboBox selection fails** | Use `get_selectable_items` to diagnose which items are visible to UIA and what text sources are available. |
| **List shows CLR type names** | Templated rows are auto-resolved to visible text. Check `rawProviderText` and `summaryParts` in `get_selectable_items` output. |
| **Composite control is hard to interact with** | Use `get_child_controls` to discover actionable children classified by interaction type. |
| **Can't read validation text** | Use `get_visible_text` to read all visible text from the window or a subtree. |
| **Screenshot fails** | Ensure the window is not minimized. The server needs a desktop session (not headless). |
| **DPI/coordinate issues** | Ensure the process is DPI-aware. BoundingRectangle values are in physical pixels. |
| **Logging to stdout breaks MCP** | All logging routes to stderr by default. Don't add `Console.WriteLine` calls. |

## Architecture

```
WinApp-MCP/
├── Program.cs                  # Host builder, DI, MCP server setup
├── Services/
│   ├── FlaUIService.cs         # Singleton: UIA3Automation, window cache, tree serialization
│   ├── ElementResolver.cs     # Element lookup with retry and transient failure recovery
│   └── SafeUIA.cs             # Safe UIA property access and multi-strategy text extraction
├── Models/
│   ├── ElementInfo.cs          # Compact DTO for element properties
│   ├── ErrorCategory.cs       # Error classification enum
│   ├── RowAction.cs           # Row-level action metadata for list items
│   └── ToolResult.cs          # Structured tool result with error categorization
└── Tools/
    ├── WindowTools.cs          # list_windows, attach_application, list_attached
    ├── InspectionTools.cs      # get_window_tree, find_elements, get_element_properties, get_selectable_items, get_child_controls
    ├── InteractionTools.cs     # click, invoke, type_text, toggle, select, send_keys, focus
    ├── TextTools.cs            # get_visible_text, get_element_text, set_text
    ├── CaptureTools.cs         # screenshot
    └── WaitTools.cs            # wait_for_element
```

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for release history.

## License

MIT
