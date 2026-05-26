# AGENTS.MD — src/Community.Microsoft.Extensions.AI.CoreML/

## Purpose

This is the main C# library project. It implements `IChatClient` from `Microsoft.Extensions.AI` by P/Invoking a native Swift bridge that calls Apple's Foundation Models framework (Apple Intelligence).

## Planned Source Layout

```
Community.Microsoft.Extensions.AI.CoreML/
├── Community.Microsoft.Extensions.AI.CoreML.csproj
├── AppleIntelligenceChatClient.cs      ← IChatClient implementation (main entry point)
├── PlatformGuard.cs                    ← throws PlatformNotSupportedException on non-arm64-macOS
├── Interop/
│   ├── NativeMethods.cs                ← [LibraryImport] P/Invoke declarations
│   └── SafeSessionHandle.cs            ← SafeHandle wrapping the native session pointer
└── Extensions/
    └── ServiceCollectionExtensions.cs  ← AddAppleIntelligence() DI helper
```

## Key Types

### `AppleIntelligenceChatClient`
- Implements `IChatClient` and `IDisposable`
- Constructor calls `PlatformGuard.ThrowIfNotSupported()` and `NativeMethods.SessionCreate()`
- `GetResponseAsync(IEnumerable<ChatMessage>, ChatOptions?, CancellationToken)` — delegates to `aib_complete` via P/Invoke, returns `Task<ChatResponse>`
- `GetStreamingResponseAsync(IEnumerable<ChatMessage>, ChatOptions?, CancellationToken)` — delegates to `aib_complete_streaming` with a GC-pinned callback delegate, returns `IAsyncEnumerable<ChatResponseUpdate>`
- `GetService(Type, object?)` — returns `this` if assignable to the requested type, else `null`

### `NativeMethods`
- `internal static partial class` using `[LibraryImport("AppleIntelligenceBridge", ...)]`
- The dylib resolves from `runtimes/osx-arm64/native/libAppleIntelligenceBridge.dylib`

### `SafeSessionHandle`
- Inherits `SafeHandle`
- `ReleaseHandle()` calls `NativeMethods.SessionDestroy(handle)`

## Coding Rules

- Enable `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>`
- All public members must have XML doc comments
- Use `[LibraryImport]` not `[DllImport]` for new P/Invoke
- Do not `throw` from inside P/Invoke callbacks — catch and propagate via a shared error slot
- Streaming must yield chunks incrementally; do not buffer the full response before returning

## Testing

- Unit tests mock the `NativeMethods` layer via an internal interface seam (`INativeSession`)
- Integration tests (`*.Integration.Tests`) run only on macOS arm64 and are skipped elsewhere via `[SkipOnPlatform]`

## See Also

- [docs/plan.md](../../docs/plan.md) — implementation roadmap
- [docs/architecture.md](../../docs/architecture.md) — full design
