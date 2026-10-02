# Work Item: AI Review Desk v0.1 Design

## Objective

Define a small, reviewable v0.1 product design and delivery plan for AI Review Desk, an independent Copilot CLI review desktop application. Preserve this repository's existing work-item convention and keep WPF product implementation gated on the CLI contract proof and independent security verification.

## Current state

- Phase 0 (design) is complete.
- Phase 1 has a small executable harness and a written evidence record. Copilot CLI 1.0.91 did not resolve the manually provisioned OAuth credential with a fresh isolated `COPILOT_HOME`, so authenticated model/tool probes could not run.
- Phase 1 recommendation: **NO-GO because a core reviewer boundary cannot be established.** The auth/configuration incompatibility blocks proof of the read-only boundary; no bypass was observed because model/tool execution was not reached.
- No WPF product implementation or global Codex configuration change has started.

## Accepted direction

- AI Review Desk is a Windows-only .NET 10 WPF desktop product that coordinates constrained, independent Copilot reviews; it is not a general coding agent.
- Codex remains the primary engineering and implementation agent. Carlo retains final decision authority.
- AI Review Desk owns repository inspection and supplies deterministic review context. The intended read/search-only surface remains unproven; see the Phase 1 evidence report before implementation.
- Configuration and history belong under `%LOCALAPPDATA%`; customer repositories need no AI Review Desk artifacts.
- Reviews are evidence for later ChatGPT/Codex assessment, never automatic implementation tasks.
- Existing repository governance, work-item practice, and global routing policy remain authoritative.

## Authoritative design material

- [`README.md`](README.md) — product, UX, data, architecture, and v0.1 boundaries.
- [`docs/copilot-cli-contract-proof.md`](docs/copilot-cli-contract-proof.md) — unresolved CLI contract and Phase 1 proof criteria.
- [`docs/phase1-copilot-cli-contract-proof.md`](docs/phase1-copilot-cli-contract-proof.md) — Phase 1 evidence, unresolved probes, and recommendation.
- [`docs/review-profiles.md`](docs/review-profiles.md) — shared policy and built-in profile focus.
- [`docs/implementation-plan.md`](docs/implementation-plan.md) — phased investigation, implementation, and verification plan.

## Open gate

Do not begin WPF product implementation or Phase 2. First resolve the supported Windows OAuth credential lookup while retaining the isolated CLI configuration; no PAT or copied token should be introduced as a shortcut. Then complete the blocked authenticated Phase 1 model/tool/security probes and obtain independent Phase 2 verification. Unknown Copilot CLI behavior remains unknown until experimentally established. The experimental Windows sandbox is not an assumed part of the v0.1 contract.

## Artifact and repository state

- Artifact policy: local-tracked documentation in this repository, consistent with its source-controlled context documents.
- This work item is the single current-state snapshot for this objective; supporting design documents are linked above.
- Task-scoped commit, merge, and push authority for AI Review Desk work is defined in [`AGENTS.md`](AGENTS.md).
- No global Codex configuration change is part of this work item.

## Current evidence

- [Phase 1 proof harness](phase1-harness/README.md)
- [Phase 1 contract proof and recommendation](docs/phase1-copilot-cli-contract-proof.md)
