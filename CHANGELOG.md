# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.0] - 2026-03-09

### Added

- **`get_child_controls`** tool — discover child controls within a composite parent element, classified by interaction type (`edit`, `button`, `toggle`, `picker`, `expander`, `label`, `other`). Enables composite-control aware discovery without full tree dumps.
- **`rootAutomationId`** parameter on `find_elements` — scope element search to a subtree, matching existing `get_window_tree` behavior.
- **`includeActions`** parameter on `get_selectable_items` — opt-in discovery of row-level action controls (buttons, toggles) within each list item.
- **`rawProviderText`** and **`summaryParts`** fields in `get_selectable_items` response — always preserves raw provider text for diagnostics; provides individual visible text parts when descendant fallback was used.
- **`RowAction`** model — metadata for actionable child controls within list rows.
- **`SafeUIA.IsLikelyCLRTypeName()`** — heuristic detection of fully-qualified .NET type names (e.g. `Namespace.Model.ClassName`) in item text, enabling automatic visible-descendant fallback.
- **`SafeUIA.ExtractVisibleSummary()`** — enhanced text extraction that falls back to visible descendant text when primary text is a CLR type name.
- **`SafeUIA.DiscoverRowActions()`** — discovers actionable child controls (Invoke, Toggle, ExpandCollapse patterns) within a list row element.
- **`SafeUIA.ClassifyInteractionType()`** — classifies elements by their primary interaction pattern.

### Changed

- **`get_selectable_items`** now uses `ExtractVisibleSummary` instead of `ExtractText` — templated list rows that previously surfaced only CLR type names now return user-visible text summaries assembled from visible descendants.
- **`select_item` value matching** now also searches visible descendant text when item text is a CLR type name, improving match rates for templated lists.
- **`select_item` diagnostics** now report counts of CLR-type-name items and visible-descendant fallbacks alongside unsupported property counts.

## [0.2.0] - 2026-03-08

### Added

- **`get_visible_text`** tool — read all visible text from a window or subtree in visual reading order, with multi-strategy fallback (Name, ValuePattern, TextPattern, LegacyIAccessible, text descendants).
- **`get_element_text`** tool — read text/value from a specific element with source reporting.
- **`set_text`** tool — set text in text-capable controls via ValuePattern with opt-in keyboard fallback.
- **`get_selectable_items`** tool — diagnostic enumeration of ComboBox/ListBox items with provider-tolerant metadata (index, text, text source, selection state, unsupported properties).
- **`propertyProfile`** parameter on `get_window_tree` — choose `minimal` (ControlType + Name only), `standard` (default), or `diagnostic` (includes SupportedPatterns and BoundingRectangle).
- **`visibleOnly`** and **`enabledOnly`** filter parameters on `find_elements`.
- **`SafeUIA`** helper — safe wrappers for all UIA property access. `<NotSupported>` sentinels replace crashes when providers don't expose properties.
- **`ErrorCategory`** enum — structured error classification: `ElementNotFound`, `PropertyNotSupported`, `TransientProviderFailure`, `OperationNotSupported`, `Unknown`.
- **`ToolResult`** record — consistent error categorization and diagnostics in tool responses.
- **Provider Tolerance** section in README documenting resilience behavior.

### Changed

- **`select_item`** rewritten with provider-tolerant selection:
  - Index-based selection uses `SelectionItemPattern` directly without reading every item's `Name`.
  - Value-based selection uses a 5-strategy fallback chain (Name → ValuePattern → LegacyIAccessible → text descendants).
  - Auto-expands collapsed ComboBoxes before selection.
  - Reports matching method used, selected index, and count of items with unsupported properties.
- **`get_window_tree`** now returns partial trees when some nodes fail instead of aborting. Failed children appear as `[<Error>]` placeholders.
- **`find_elements`** now supports searching by `controlType` alone (no name/id required). Per-element fault tolerance: elements that fail to serialize are skipped instead of failing the whole call.
- **`ElementInfo.FromElement()`** uses safe property access — no longer crashes when `Name`, `AutomationId`, `ClassName`, `IsEnabled`, or `IsOffscreen` throw `NotSupported`.
- **`ElementResolver.RetryFind()`** now catches and retries transient `COMException` (e.g. `E_UNEXPECTED`) up to 3 times with element re-resolution.
- Error messages throughout now include `[ErrorCategory]` classification instead of raw exception text.

### Fixed

- `get_window_tree` no longer fails when elements in the tree lack `AutomationId` or `Name` properties.
- `select_item` no longer fails on ComboBoxes where items return `NotSupported` for `NameProperty`.
- Per-child try/catch in tree serialization prevents one uninspectable node from aborting sibling enumeration.
- Transient COM failures (`E_UNEXPECTED`, `0x8000FFFF`) during element search are now retried instead of surfaced as raw errors.

## [0.1.0] - 2026-02-20

### Added

- Initial release.
- MCP server with stdio transport.
- **Discovery tools:** `list_windows`, `attach_application`, `list_attached`.
- **Inspection tools:** `get_window_tree`, `find_elements`, `get_element_properties`.
- **Interaction tools:** `click_element`, `invoke_element`, `type_text`, `toggle_element`, `select_item`, `send_keys`, `focus_element`.
- **Capture tools:** `screenshot` (base64 PNG, max 1280px width).
- **Wait tools:** `wait_for_element` (configurable timeout).
- Element resolution priority cascade: AutomationId → XPath → Name + ControlType.
- Window caching with automatic stale-entry cleanup.
- FlaUI UIA3 automation backend.
