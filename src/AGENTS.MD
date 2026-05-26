# AGENTS.MD — src/

## Purpose of this folder

Contains all C# source code for the `Community.Microsoft.Extensions.AI.CoreML` library.

## Projects

| Project | Description |
|---|---|
| `Community.Microsoft.Extensions.AI.CoreML/` | The main library — implements `IChatClient` |

## Guidelines for agents working here

- All public API must target `net8.0` or higher.
- The library is **macOS arm64 only** at runtime. The build must succeed on all platforms (for CI), but runtime execution is macOS arm64 only.
- Use `[LibraryImport]` (source-generated P/Invoke) rather than `[DllImport]` for new interop declarations.
- Use `nullable` enabled — all reference types must be correctly annotated.
- Do not reference `Microsoft.Maui.*` packages.
- See [../docs/architecture.md](../docs/architecture.md) for the full technical design.
