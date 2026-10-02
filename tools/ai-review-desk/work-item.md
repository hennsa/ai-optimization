# Work Item: AI Review Desk v0.1 Design

## Objective

Define a small, reviewable v0.1 product design and delivery plan for AI Review Desk, an independent Copilot CLI review desktop application. Preserve this repository's existing work-item convention and keep the work documentation-only until a later phase explicitly authorizes implementation.

## Current state

- Phase 0 (design) is complete in this documentation set.
- No application, harness, package, or global Codex configuration has been created or changed.
- The next active work is Phase 1: a bounded Copilot CLI contract proof, followed by independent adversarial verification.

## Accepted direction

- AI Review Desk is a Windows-only .NET 10 WPF desktop product that coordinates constrained, independent Copilot reviews; it is not a general coding agent.
- Codex remains the primary engineering and implementation agent. Carlo retains final decision authority.
- AI Review Desk owns repository inspection and supplies deterministic review context. Copilot receives only the narrow read/search surface that Phase 1 can prove.
- Configuration and history belong under `%LOCALAPPDATA%`; customer repositories need no AI Review Desk artifacts.
- Reviews are evidence for later ChatGPT/Codex assessment, never automatic implementation tasks.
- Existing repository governance, work-item practice, and global routing policy remain authoritative.

## Authoritative design material

- [`README.md`](README.md) — product, UX, data, architecture, and v0.1 boundaries.
- [`docs/copilot-cli-contract-proof.md`](docs/copilot-cli-contract-proof.md) — unresolved CLI contract and Phase 1 proof criteria.
- [`docs/review-profiles.md`](docs/review-profiles.md) — shared policy and built-in profile focus.
- [`docs/implementation-plan.md`](docs/implementation-plan.md) — phased investigation, implementation, and verification plan.

## Open gate

Do not begin WPF product implementation until the CLI contract proof and independent security verification have produced an explicit go/no-go decision. Unknown Copilot CLI behavior remains unknown until experimentally established; see the proof plan.

## Artifact and repository state

- Artifact policy: local-tracked documentation in this repository, consistent with its source-controlled context documents.
- This work item is the single current-state snapshot for this objective; supporting design documents are linked above.
- Task-scoped commit, merge, and push authority for AI Review Desk work is defined in [`AGENTS.md`](AGENTS.md); this Phase 0 task does not begin Phase 1 or authorize unrelated work.
- No global Codex configuration change is part of this work item.
