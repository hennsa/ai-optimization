# AI Review Desk Implementation Plan

This plan separates investigation, product implementation, and verification. Phase 0 is complete. Phase 1 remains NO-GO because Copilot CLI 1.0.91 did not resolve the manually provisioned Windows OAuth credential under a fresh isolated `COPILOT_HOME`; the current work item and linked evidence report control the next step.

## Phase 0 — Design

**Status: complete.** Define the v0.1 product boundary, UX, result and history concepts, architecture direction, profiles, and staged plan. Record assumptions instead of converting unknown CLI behavior into guarantees.

## Phase 1 — Copilot CLI contract proof

**Status: NO-GO because a core reviewer boundary cannot be established.** The harness and report exist, but model/tool security probes did not run because the isolated CLI could not resolve OAuth from Windows Credential Manager. Resolve authentication while retaining isolated configuration, complete the authenticated probes, and establish the requested controls before starting Phase 2. The experimental Windows sandbox is not assumed by the v0.1 contract. See the [Phase 1 evidence report](phase1-copilot-cli-contract-proof.md) and [contract proof plan](copilot-cli-contract-proof.md).

## Phase 2 — Independent contract/security verification

Have a separate verifier challenge Phase 1 evidence and attempt to break the read-only boundary with synthetic fixtures. Check instruction/config discovery, tool denial, modification and command attempts, cancellation, and mid-run state changes. Resolve discrepancies and record explicit residual risks. Do not advance to product implementation if the boundary depends only on prompt compliance or remains materially ambiguous.

## Phase 3 — Core and Git inspection

Implement the small UI-independent domain/application model, project registry contract, deterministic repository snapshots, review scopes, base/merge-base resolution, changed paths, diff construction/statistics, and fingerprints using installed `git.exe` unless evidence justifies a Git library. Define behavior for detached HEAD, missing base, shallow/missing objects, untracked files, submodules, and unavailable Git. Do not fetch automatically.

## Phase 4 — Copilot integration

Implement version/capability detection from proven CLI behavior, safe process argument construction, redirected stdin/stdout/stderr, streaming JSONL handling, cancellation and process-tree termination, structured result parsing, and before/after integrity checks. Keep runs in owned directories with isolated state and enforce only the proof-approved capabilities. Report unsupported CLI contract conditions clearly.

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
2. Phase 1 must produce reproducible CLI behavior evidence and a go/no-go recommendation; current evidence is insufficient to pass.
3. Phase 2 independently challenges a complete Phase 1 proof and approves or rejects the security contract.
4. Phases 3–6 produce the MVP in vertical slices under new implementation work items.
5. Phase 7 provides end-to-end and security acceptance evidence before release.

Cross-repository execution, PR management, custom profiles, web research, and other listed non-goals require a separate future scope decision; they are not implied by completion of the MVP.
