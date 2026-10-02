# AI Review Desk Implementation Plan

This plan separates investigation, product implementation, and verification. Phases 0–2 are complete. Phase 2 verified the model tool boundary with required launch and configuration changes; the [current work item](../work-item.md) and [Phase 2 report](phase2-copilot-cli-verification.md) control the next implementation step.

## Phase 0 — Design

**Status: complete.** Define the v0.1 product boundary, UX, result and history concepts, architecture direction, profiles, and staged plan. Record assumptions instead of converting unknown CLI behavior into guarantees.

## Phase 1 — Copilot CLI contract proof

**Status: complete.** Authenticated Phase 1 probes established the external read/search path, an effective `view`/`grep`/`glob` tool allowlist, fixture invariance under adversarial prompts, structured streaming, stdin transport, active cancellation, and concurrent-change detection. See the [Phase 1 evidence report](phase1-copilot-cli-contract-proof.md) and [contract proof plan](copilot-cli-contract-proof.md).

## Phase 2 — Independent contract/security verification

**Status: complete — VERIFIED WITH REQUIRED DESIGN CHANGES.** Independent synthetic probes reproduced the three-tool model boundary and found out-of-band profile hook/MCP startup plus automatic temp-directory reads. The [Phase 2 report](phase2-copilot-cli-verification.md) defines required pre-launch profile checks, `--disallow-temp-dir`, and completed-run validation. These are product acceptance criteria, not optional hardening. The experimental Windows sandbox is not assumed by v0.1.

## Phase 3 — Core and Git inspection

Implement the small UI-independent domain/application model, project registry contract, deterministic repository snapshots, review scopes, base/merge-base resolution, changed paths, diff construction/statistics, and fingerprints using installed `git.exe` unless evidence justifies a Git library. Define behavior for detached HEAD, missing base, shallow/missing objects, untracked files, submodules, and unavailable Git. Do not fetch automatically.

## Phase 4 — Copilot integration

Implement version/capability detection from proven CLI behavior, safe process argument construction, redirected stdin/stdout/stderr, streaming JSONL handling, cancellation and process-tree termination, structured result parsing, and before/after integrity checks. Keep runs in empty owned directories, add only the reviewed repository, use `--disallow-temp-dir`, and enforce the proof-approved model tools. Before launch, fail closed on unapproved hook/MCP/plugin/extension configuration in the app-owned profile or run directory. After exit, reject absent/extra tools, unexpected MCP status, malformed or partial JSONL, nonzero exit, cancellation, or changed repository state before showing trusted findings. The tool manifest is not an early startup gate. Report unsupported CLI contract conditions clearly.

## Phase 5 — Persistence and handoff

Persist project settings and run metadata under `%LOCALAPPDATA%` without storing full diffs or raw JSONL by default. Select file-based persistence unless a documented query, concurrency, migration, or integrity need justifies SQLite. Implement the three copy formats and history retention/deletion controls; verify that source excerpts and credentials do not leak into durable records.

## Phase 6 — WPF MVP

Build a Windows-only .NET 10 WPF app with MVVM using `CommunityToolkit.Mvvm` and, if confirmed, `Wpf.Ui`. Deliver accessible project management, state summary, scope/profile selection, friendly progress, structured findings, history, settings, and copy handoffs. Keep Git details and diagnostics progressively disclosed. Normal operation must not require config editing or terminal interaction.

Suggested project split, subject to repository/build convention review:

- `AIReviewDesk.App`: WPF views, navigation, view models, user interaction.
- `AIReviewDesk.Core`: domain/application concepts and orchestration contracts.
- `AIReviewDesk.Infrastructure`: Git, process/Copilot integration, persistence, clipboard formatting.

Keep solution/project count small. Confirm package support, target framework availability, signing/distribution needs, and Windows deployment assumptions during this phase.

## Phase 7 — End-to-end verification

Verify project add/switch/defaults; all scopes and path narrowing; no-fetch base resolution; valid empty and populated findings; malformed/partial JSONL; CLI absent/unsupported/auth failures; cancellation; stale-state detection; persistence and retention; copy handoffs; and UI progress/error states. Repeat the adversarial read-only and repository invariance scenarios against the product launch path. Any capability or integrity regression blocks release.

## Sequence gates and outputs

1. Phase 0 provides this design set.
2. Phase 1 produced a reproducible authenticated CLI proof and handed it to independent verification.
3. Phase 2 independently challenged the proof and accepted a revised contract with required design changes.
4. Phases 3–6 produce the MVP in vertical slices under new implementation work items.
5. Phase 7 provides end-to-end and security acceptance evidence before release.

Cross-repository execution, PR management, custom profiles, web research, and other listed non-goals require a separate future scope decision; they are not implied by completion of the MVP.
