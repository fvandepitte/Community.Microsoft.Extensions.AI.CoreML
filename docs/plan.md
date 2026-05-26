# Implementation Plan

> **Project:** Community.Microsoft.Extensions.AI.CoreML  
> **Goal:** Expose Apple Intelligence on-device models via `IChatClient` using P/Invoke — no MAUI, no HTTP server.  
> **License:** MIT

---

## Status Legend

| Symbol | Meaning |
|---|---|
| ✅ | Done |
| 🔄 | In progress |
| ⬜ | Not started |
| ❓ | Blocked / needs investigation |

---

## Phase 0 — Repository Scaffolding ✅

- [x] Create GitHub repository (`fvandepitte/Community.Microsoft.Extensions.AI.CoreML`)
- [x] Initial `.csproj` targeting `net10.0` with `Microsoft.Extensions.AI.Abstractions 10.6.0`
- [x] MIT `LICENSE`
- [x] `README.md`
- [x] `AGENTS.MD` files at all levels
- [x] `docs/plan.md` (this file)
- [x] `docs/architecture.md`

---

## Phase 1 — Native Bridge (Swift / C) ⬜

The Foundation Models framework is Swift-only. To call it from C# we need a thin C-callable wrapper.

### 1.1 Create the Swift bridge project

- [ ] Add `native/` folder to the repository
- [ ] Create a Swift Package (`native/Package.swift`) that produces a dynamic library target (`AppleIntelligenceBridge`)
- [ ] Set minimum deployment target: macOS 26

### 1.2 Implement `@_cdecl` exported functions

Implement the following C-callable functions in Swift:

```swift
// Session lifecycle
@_cdecl("aib_session_create")
public func aib_session_create() -> UnsafeRawPointer

@_cdecl("aib_session_destroy")
public func aib_session_destroy(_ session: UnsafeRawPointer)

// Synchronous completion
@_cdecl("aib_complete")
public func aib_complete(
    _ session: UnsafeRawPointer,
    _ prompt: UnsafePointer<CChar>,
    _ resultCallback: @convention(c) (UnsafePointer<CChar>?) -> Void
)

// Streaming completion
@_cdecl("aib_complete_streaming")
public func aib_complete_streaming(
    _ session: UnsafeRawPointer,
    _ prompt: UnsafePointer<CChar>,
    _ chunkCallback: @convention(c) (UnsafePointer<CChar>?, Bool) -> Void
)

// Model availability check
@_cdecl("aib_is_available")
public func aib_is_available() -> Bool
```

### 1.3 Build & package the dylib

- [ ] Add a `Makefile` / shell script to build `libAppleIntelligenceBridge.dylib` for `arm64`
- [ ] Copy the output dylib to `src/Community.Microsoft.Extensions.AI.CoreML/runtimes/osx-arm64/native/`
- [ ] Update `.csproj` to embed the dylib as a native runtime asset

### 1.4 Investigate availability guard

- [ ] Determine the exact `LanguageModelSession.isAvailable` API shape in macOS 26 beta
- [ ] Confirm whether a Hardened Runtime entitlement is required for third-party callers

---

## Phase 2 — C# Interop Layer ⬜

### 2.1 P/Invoke declarations (`src/.../Interop/`)

- [ ] `NativeMethods.cs` — static `[LibraryImport]` / `[DllImport]` declarations matching the Swift C-bridge functions
- [ ] `SafeSessionHandle.cs` — a `SafeHandle` wrapping the opaque session pointer, calling `aib_session_destroy` on dispose

### 2.2 Platform guard ✅

- [x] `IPlatformValidator.cs` — internal interface providing a test seam
- [x] `PlatformGuard.cs` — real implementation; throws `PlatformNotSupportedException` unless on macOS arm64
- [x] `InternalsVisibleTo` wired to the test project

---

## Phase 3 — `IChatClient` Implementation 🔄

### 3.1 `AppleIntelligenceChatClient` ✅ (stubbed)

- [x] Implement `IChatClient`
- [x] `GetResponseAsync` — platform-guarded; throws `NotImplementedException` until bridge is ready
- [x] `GetStreamingResponseAsync` — platform-guarded; throws `NotImplementedException` until bridge is ready
- [x] `GetService(Type, object?)` — returns `this` if assignable to the requested type
- [x] `Dispose` / `ObjectDisposedException` guard
- [ ] Wire up real `GetResponseAsync` to `aib_complete` once native bridge exists
- [ ] Wire up real `GetStreamingResponseAsync` to `aib_complete_streaming` once native bridge exists

### 3.2 Message formatting

- [ ] Map `IEnumerable<ChatMessage>` to a prompt string the Foundation Models session expects
- [ ] Handle multi-turn conversations (system / user / assistant roles)

### 3.3 Metadata

- [ ] Populate `ChatResponse.ModelId` with `"apple-intelligence"` (or the actual model identifier if the API exposes one)
- [ ] Populate `Usage` if the framework provides token counts

---

## Phase 4 — DI Integration ⬜

- [ ] `ServiceCollectionExtensions.cs` — `AddAppleIntelligence()` extension that registers `IChatClient` as a singleton
- [ ] Respect the `IChatClientBuilder` pipeline (middleware support)

---

## Phase 5 — Tests 🔄

### 5.1 Unit tests (run on any platform) ✅ (partial)

- [x] `IPlatformValidator` interface seam — no mocking framework required
- [x] Platform guard: constructor throws on unsupported platform
- [x] Platform guard: constructor succeeds on supported platform
- [x] `GetService` returns self / null correctly
- [x] `ObjectDisposedException` after `Dispose()`
- [x] `NotImplementedException` before native bridge is wired up
- [ ] Test message formatting / prompt construction
- [ ] Test streaming chunk assembly

### 5.2 Integration tests (macOS arm64 only)

- [ ] CI job that runs on a macOS 26 GitHub-hosted runner (when available)
- [ ] `aib_is_available()` availability check test
- [ ] Simple round-trip completion test
- [ ] Streaming test

---

## Phase 6 — NuGet Packaging ⬜

- [ ] Add `<PackageId>`, `<Authors>`, `<Description>`, `<PackageTags>` to `.csproj`
- [ ] Add `<PackageLicenseExpression>MIT</PackageLicenseExpression>`
- [ ] Add `<RepositoryUrl>` and `<PackageReadmeFile>`
- [ ] Ensure the `runtimes/osx-arm64/native/` dylib is included in the NuGet package
- [ ] Publish to NuGet.org (manual initially, automate via GitHub Actions later)

---

## Phase 7 — CI/CD ⬜

- [ ] GitHub Actions workflow to build `.csproj` on `ubuntu-latest` (compilation check)
- [ ] GitHub Actions workflow to build native bridge on `macos-latest-xlarge` (Apple Silicon runner)
- [ ] Automated NuGet publish on tag push

---

## Open Questions

| # | Question | Status |
|---|---|---|
| 1 | Does `LanguageModelSession` require a running NSApplication/main run loop, or can it be called from a background thread? | ❓ |
| 2 | Is there a rate-limit or sandbox restriction on calling Foundation Models from a non-GUI process (similar to the restrictions apfel works around)? | ❓ |
| 3 | What entitlements (if any) are needed to access Foundation Models from a .NET process? | ❓ |
| 4 | Does Foundation Models expose a token-count API we can surface in `UsageDetails`? | ❓ |
| 5 | Should the dylib be code-signed before distribution? What impact does this have on NuGet packaging? | ❓ |

---

## References

- [Apple FoundationModels framework — developer docs](https://developer.apple.com/documentation/foundationmodels)
- [apfel — Swift CLI/server for Apple Intelligence](https://github.com/apfel-ai/apfel)
- [apple-on-device-openai — SwiftUI OpenAI server](https://github.com/gety-ai/apple-on-device-openai)
- [Microsoft.Extensions.AI — IChatClient interface](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.ichatclient)
- [Swift `@_cdecl` for C interop](https://forums.swift.org/t/using-cdecl/2893)
- [.NET LibraryImport source generation](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation)
