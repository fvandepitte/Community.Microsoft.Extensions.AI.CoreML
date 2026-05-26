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

## Phase 1 — Native Bridge (Swift / C) ✅

The Foundation Models framework is Swift-only. To call it from C# we need a thin C-callable wrapper.

### 1.1 Create the Swift bridge project ✅

- [x] Add `native/` folder to the repository
- [x] Create a Swift Package (`native/Package.swift`) that produces a dynamic library target (`AppleIntelligenceBridge`)
- [x] Set minimum deployment target: macOS 15 (runtime guard requires macOS 26+)

### 1.2 Implement `@_cdecl` exported functions ✅

See `native/Sources/AppleIntelligenceBridge/Bridge.swift`.

Both completion functions accept a **JSON string** of `{"role","content"}` objects (full
conversation history). A fresh `LanguageModelSession` is built per-call with the
Transcript pre-loaded — no re-inference on history, matches `IChatClient` contract.

| Symbol | Description |
|---|---|
| `aib_is_available` | `SystemLanguageModel.isAvailable` |
| `aib_complete` | Blocking; bridges async Swift → sync via `DispatchSemaphore` |
| `aib_complete_streaming` | Streams cumulative snapshots; computes deltas by tracking previous content length |

### 1.3 Build & package the dylib ✅

- [x] `Makefile` at repo root — run `make bridge` to compile and copy the dylib
- [x] Output path: `src/Community.Microsoft.Extensions.AI.CoreML/runtimes/osx-arm64/native/`
- [x] `.csproj` updated to embed the dylib as a NuGet native runtime asset (conditional on file existing)

### 1.4 Investigate availability guard 🔄

#### What is confirmed (no hardware needed)

At **WWDC 2025**, Apple opened the Foundation Models framework to all third-party developers:

| Question | Answer |
|---|---|
| `SystemLanguageModel.isAvailable` API shape | ✅ Static `Bool` property — confirmed unchanged |
| Restricted to Apple apps only? | ✅ **No** — opened to all third-party devs at WWDC 2025 |
| Special entitlement approval required? | ✅ **No** — standard Apple Developer Program membership is enough |
| Minimum OS | macOS 26 ("Tahoe") on Apple Silicon |
| Minimum Xcode | Xcode 26 |
| Public beta | July 2025 |
| Full release | September 2025 (with macOS 26) |

#### What still needs testing on macOS 26 hardware

The one open question is whether a **.NET host process** embedding the dylib must itself be **code-signed with a FoundationModels entitlement**. Normal native apps bundle the entitlement in their `.entitlements` file; a .NET process is a generic runtime, not a signed app bundle.

**Steps to verify:**

```sh
# 1. Build the bridge
make bridge

# 2. Run the test .NET app on macOS 26 — check what aib_is_available() returns.
#    If it crashes or returns false, inspect the system log:
log show --predicate 'subsystem == "com.apple.FoundationModels"' --last 5m
```

**If the entitlement IS required on the host process**, sign the dylib with:

```sh
codesign --entitlements entitlements.plist -s "Apple Developer" \
  src/Community.Microsoft.Extensions.AI.CoreML/runtimes/osx-arm64/native/libAppleIntelligenceBridge.dylib
```

Where `entitlements.plist` contains:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>com.apple.developer.FoundationModels</key>
    <true/>
</dict>
</plist>
```

- [x] Confirm `SystemLanguageModel.isAvailable` is a static Bool — ✅ confirmed
- [x] Confirm framework is open to third-party developers — ✅ confirmed (WWDC 2025)
- [ ] Test `aib_is_available()` from a .NET process on macOS 26 hardware
- [ ] Confirm whether dylib / host process needs code-signing with the FoundationModels entitlement

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
