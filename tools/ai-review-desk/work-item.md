# Work Item: AI Review Desk v0.1 Implementation

## Objective

Implement the accepted v0.1 design in core/Git/Copilot integration slices, then the small WPF product. Preserve the verified reviewer boundary and fail closed on configuration, capability, transport, or repository-integrity failures.

## Current state

- Phase 0 (design) is complete.
- Phase 1 authenticated proof is complete for Copilot CLI 1.0.91. Its effective tool manifest was restricted to `view`, `grep`, and `glob`; adversarial fixture fingerprints remained unchanged. See the report for probe-by-probe limits.
- Phase 2 independent verification is complete: **VERIFIED WITH REQUIRED DESIGN CHANGES**. The model tool restriction held, but profile hooks and MCP startup can execute outside that manifest and Phase 1's launch allowed sibling temp reads. See the [Phase 2 report](docs/phase2-copilot-cli-verification.md).
- No WPF product implementation or global Codex configuration change has started. The next active work is implementation, beginning with core/Git/Copilot integration.

## Accepted direction

- AI Review Desk is a Windows-only .NET 10 WPF desktop product that coordinates constrained, independent Copilot reviews; it is not a general coding agent.
- Codex remains the primary engineering and implementation agent. Carlo retains final decision authority.
- AI Review Desk owns repository inspection and supplies deterministic review context. Each review uses an ephemeral run directory; the repository is supplied separately through `--add-dir`. `--disallow-temp-dir` is required, and read reach is a local-desktop trust assumption rather than an OS sandbox guarantee.
- Use a dedicated, persistent Copilot CLI profile at `%LOCALAPPDATA%\AIReviewDesk\Copilot`, authenticated once through the normal `copilot login --web-flow` OAuth flow. AI Review Desk does not read, copy, export, or persist OAuth credential values. A fresh empty Copilot home does not inherit the user's normal CLI authentication.
- Configuration, the dedicated Copilot profile, and history belong under `%LOCALAPPDATA%`; customer repositories need no AI Review Desk artifacts.
- Reviews are evidence for later ChatGPT/Codex assessment, never automatic implementation tasks.
- The app must preflight the app-owned profile and run directory for executable hook/MCP/plugin/extension configuration and reject unexpected sources before launching Copilot. `disableAllHooks=true` is defense in depth; it did not suppress a synthetic profile hook on CLI 1.0.91. The app must validate completed JSONL and repository integrity before presenting findings.
- Existing repository governance, work-item practice, and global routing policy remain authoritative.

## Authoritative design material

- [`README.md`](README.md) — product, UX, data, architecture, and v0.1 boundaries.
- [`docs/copilot-cli-contract-proof.md`](docs/copilot-cli-contract-proof.md) — unresolved CLI contract and Phase 1 proof criteria.
- [`docs/phase1-copilot-cli-contract-proof.md`](docs/phase1-copilot-cli-contract-proof.md) — Phase 1 evidence, unresolved probes, and recommendation.
- [`docs/phase2-copilot-cli-verification.md`](docs/phase2-copilot-cli-verification.md) — independent challenge, verdict, and required safeguards.
- [`docs/review-profiles.md`](docs/review-profiles.md) — shared policy and built-in profile focus.
- [`docs/implementation-plan.md`](docs/implementation-plan.md) — phased investigation, implementation, and verification plan.

## Next active work

Begin the implementation plan's Phase 3 core/Git inspection and Phase 4 Copilot integration slices. Incorporate the Phase 2 required design changes in the fixed launch, profile preflight, JSONL validation, cancellation, and stale-result handling. Product release remains gated on end-to-end verification against the real launch path. This Phase 2 task itself did not start application implementation. The experimental Windows sandbox is not an assumed prerequisite.

## Artifact and repository state

- Artifact policy: local-tracked documentation in this repository, consistent with its source-controlled context documents.
- This work item is the single current-state snapshot for this objective; supporting design documents are linked above.
- Task-scoped commit, merge, and push authority for AI Review Desk work is defined in [`AGENTS.md`](AGENTS.md).
- No global Codex configuration change is part of this work item.

## Current evidence

- [Phase 1 proof harness](phase1-harness/README.md)
- [Phase 1 contract proof and recommendation](docs/phase1-copilot-cli-contract-proof.md)
- [Phase 2 independent verification and revised contract](docs/phase2-copilot-cli-verification.md)
