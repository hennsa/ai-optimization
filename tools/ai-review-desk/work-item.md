# Work Item: AI Review Desk v0.1 Design

## Objective

Define a small, reviewable v0.1 product design and delivery plan for AI Review Desk, an independent Copilot CLI review desktop application. Preserve this repository's existing work-item convention and keep WPF product implementation gated on the CLI contract proof and independent security verification.

## Current state

- Phase 0 (design) is complete.
- Phase 1 authenticated proof is complete for Copilot CLI 1.0.91. Its effective tool manifest was restricted to `view`, `grep`, and `glob`; adversarial fixture fingerprints remained unchanged. See the report for probe-by-probe limits.
- Phase 1 recommendation: **GO for Phase 2 independent verification.** This does not authorize WPF implementation.
- No WPF product implementation or global Codex configuration change has started.

## Accepted direction

- AI Review Desk is a Windows-only .NET 10 WPF desktop product that coordinates constrained, independent Copilot reviews; it is not a general coding agent.
- Codex remains the primary engineering and implementation agent. Carlo retains final decision authority.
- AI Review Desk owns repository inspection and supplies deterministic review context. Each review uses an ephemeral run directory; the repository is supplied separately as a read-only additional directory.
- Use a dedicated, persistent Copilot CLI profile at `%LOCALAPPDATA%\AIReviewDesk\Copilot`, authenticated once through the normal `copilot login --web-flow` OAuth flow. AI Review Desk does not read, copy, export, or persist OAuth credential values. A fresh empty Copilot home does not inherit the user's normal CLI authentication.
- Configuration, the dedicated Copilot profile, and history belong under `%LOCALAPPDATA%`; customer repositories need no AI Review Desk artifacts.
- Reviews are evidence for later ChatGPT/Codex assessment, never automatic implementation tasks.
- Existing repository governance, work-item practice, and global routing policy remain authoritative.

## Authoritative design material

- [`README.md`](README.md) — product, UX, data, architecture, and v0.1 boundaries.
- [`docs/copilot-cli-contract-proof.md`](docs/copilot-cli-contract-proof.md) — unresolved CLI contract and Phase 1 proof criteria.
- [`docs/phase1-copilot-cli-contract-proof.md`](docs/phase1-copilot-cli-contract-proof.md) — Phase 1 evidence, unresolved probes, and recommendation.
- [`docs/review-profiles.md`](docs/review-profiles.md) — shared policy and built-in profile focus.
- [`docs/implementation-plan.md`](docs/implementation-plan.md) — phased investigation, implementation, and verification plan.

## Open gate

Phase 1 recommends proceeding to independent Phase 2 verification. Do not begin WPF product implementation in this task. Phase 2 should challenge the exact persistent-profile launch contract, especially effective-tool fail-closed behavior, repository extension loading, hooks/MCP isolation, and stale-result handling. The experimental Windows sandbox is not an assumed part of the v0.1 contract.

## Artifact and repository state

- Artifact policy: local-tracked documentation in this repository, consistent with its source-controlled context documents.
- This work item is the single current-state snapshot for this objective; supporting design documents are linked above.
- Task-scoped commit, merge, and push authority for AI Review Desk work is defined in [`AGENTS.md`](AGENTS.md).
- No global Codex configuration change is part of this work item.

## Current evidence

- [Phase 1 proof harness](phase1-harness/README.md)
- [Phase 1 contract proof and recommendation](docs/phase1-copilot-cli-contract-proof.md)
