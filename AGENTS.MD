# AGENTS.MD — Community.Microsoft.Extensions.AI.CoreML (Root)

## Project Purpose

This repository implements an `IChatClient` for the `Microsoft.Extensions.AI` abstraction that calls Apple's on-device Foundation Models (Apple Intelligence) **directly from .NET via P/Invoke**, without requiring MAUI or any cloud dependency.

## Repository Layout

```
/
├── AGENTS.MD                        ← you are here (root context)
├── README.md                        ← user-facing documentation
├── LICENSE                          ← MIT
├── docs/
│   ├── AGENTS.MD                    ← context for agents working in docs/
│   ├── plan.md                      ← implementation roadmap
│   └── architecture.md              ← technical architecture detail
└── src/
    ├── AGENTS.MD                    ← context for agents working in src/
    └── Community.Microsoft.Extensions.AI.CoreML/
        ├── AGENTS.MD                ← context for agents working on the library
        └── Community.Microsoft.Extensions.AI.CoreML.csproj
```

## Technology Stack

- **Language:** C# (.NET 8+), Swift (native bridge only)
- **Target platform:** macOS 26+ on Apple Silicon (arm64)
- **Key dependency:** `Microsoft.Extensions.AI` — the `IChatClient` / `IStreamingChatClient` interfaces
- **Native interop:** P/Invoke into a thin Swift C-bridge dylib (`libAppleIntelligenceBridge.dylib`)
- **Apple framework used:** `FoundationModels` (Apple Intelligence, `LanguageModelSession`)

## Core Design Decisions

1. **No MAUI** — the project must not take a dependency on `Microsoft.Maui.*`. All Apple APIs are accessed via P/Invoke.
2. **No HTTP server** — communication is direct native calls, not HTTP to a local server.
3. **Thin C bridge** — Swift APIs with no stable C ABI are wrapped in `@_cdecl` Swift functions that are safely callable from C#.
4. **macOS-only guard** — the library must throw `PlatformNotSupportedException` on non-macOS platforms and non-Apple-Silicon hardware at runtime.
5. **Streaming first** — `CompleteStreamingAsync` is the primary implementation; `CompleteAsync` calls it internally.

## Naming Conventions

- Public API entry point: `AppleIntelligenceChatClient` (implements `IChatClient`)
- Native interop code lives in a `Interop/` subfolder
- DI extension methods live in `Extensions/ServiceCollectionExtensions.cs`

## What NOT to Do

- Do not add MAUI NuGet references
- Do not make HTTP calls to a local OpenAI server; keep it in-process via P/Invoke
- Do not target Windows or Linux; the project is macOS arm64 only

## Related Resources

- [Apple FoundationModels framework docs](https://developer.apple.com/documentation/foundationmodels)
- [apfel — Apple Intelligence CLI/server (Swift)](https://github.com/apfel-ai/apfel)
- [Microsoft.Extensions.AI source](https://github.com/dotnet/extensions)
- [docs/plan.md](docs/plan.md) — current roadmap
- [docs/architecture.md](docs/architecture.md) — technical deep dive
