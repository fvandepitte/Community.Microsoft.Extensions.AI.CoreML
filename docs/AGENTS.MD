# AGENTS.MD — docs/

## Purpose of this folder

The `docs/` folder contains human- and agent-readable documentation for the `Community.Microsoft.Extensions.AI.CoreML` project.

## Files

| File | Description |
|---|---|
| `plan.md` | Full implementation roadmap — phases, tasks, open questions |
| `architecture.md` | Technical architecture: interop layer, P/Invoke signatures, data-flow |

## Guidelines for agents working here

- **Do not** put generated code in this folder.
- Keep `plan.md` up to date when implementation milestones are reached.
- `architecture.md` should reflect the *actual* code, not aspirational design — update it when the native bridge API surface changes.
- All diagrams use **Mermaid JS** (fenced ` ```mermaid ` blocks). No external image dependencies.
