# AGENTS.MD — native/

## Purpose

This folder contains the Swift Package that builds `libAppleIntelligenceBridge.dylib` —
the thin C bridge between .NET P/Invoke and Apple's Foundation Models framework.

## Requirements

- macOS 26 beta 2 or later  
- Apple Silicon (arm64)  
- Xcode 26 beta 2 or later (provides `FoundationModels.framework`)  
- Apple Intelligence enabled in System Settings

## Build

From the **repository root**:

```sh
make bridge
```

This compiles the dylib in release mode and copies it to:

```
src/Community.Microsoft.Extensions.AI.CoreML/runtimes/osx-arm64/native/libAppleIntelligenceBridge.dylib
```

You must run `make bridge` **before** running `dotnet pack`.

## Exported C functions

| Symbol | Signature | Purpose |
|---|---|---|
| `aib_is_available` | `() -> Bool` | Returns true if Apple Intelligence models are available |
| `aib_complete` | `(messagesJson, callback)` | Blocking completion — calls callback once with result or error |
| `aib_complete_streaming` | `(messagesJson, callback)` | Streaming — calls callback per delta chunk; final call has `isDone=true` |

## Message JSON format

Both `aib_complete` and `aib_complete_streaming` accept a UTF-8 JSON string:

```json
[
  { "role": "system",    "content": "You are a helpful assistant." },
  { "role": "user",      "content": "Hello" },
  { "role": "assistant", "content": "Hi! How can I help?" },
  { "role": "user",      "content": "What is stoicism?" }
]
```

The **last entry must be role `"user"`** — this becomes the prompt sent to `respond(to:)`.
Prior entries are baked into the `Transcript` as conversation history (no re-inference cost).

## Guidelines for agents working here

- Do NOT change the exported symbol names — they are hard-coded in `NativeMethods.cs` on the C# side.
- The bridge is **intentionally thin** — no AI logic here; all formatting and mapping lives in C#.
- Do not add Swift package dependencies; keep the bridge dependency-free.
- Keep the async→sync bridging via `DispatchSemaphore` — do not block the main thread from C#.
