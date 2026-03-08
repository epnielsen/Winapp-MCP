# WinApp-MCP — Copilot Instructions

## Mandatory: Versioning and Documentation

When making functional changes to this project (new features, bug fixes, behavioral changes), you **must** also update the following before considering the work complete:

1. **Version bump** — Update the version in both:
   - `WinApp-MCP/WinApp-MCP.csproj` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`)
   - `WinApp-MCP/Program.cs` (the `Version` property in `ServerInfo`)

   Follow [Semantic Versioning](https://semver.org/):
   - **MAJOR** — breaking changes to existing MCP tool contracts (renamed tools, removed parameters, changed return formats)
   - **MINOR** — new tools, new parameters on existing tools, new capabilities
   - **PATCH** — bug fixes, internal hardening, documentation-only changes

2. **CHANGELOG.md** — Add an entry under the new version at the top of the file. Use [Keep a Changelog](https://keepachangelog.com/) format with sections: `Added`, `Changed`, `Fixed`, `Removed` as applicable.

3. **README.md** — Update if the change affects:
   - The Tools Reference tables
   - The Architecture tree
   - The Example Agent Workflow
   - The Troubleshooting table
   - The version badge

Skip these updates only for non-functional changes like whitespace, comments, or internal refactoring with no user-visible effect.
