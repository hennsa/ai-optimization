# Work Item: AI Review Desk v0.1 Implementation

## Objective

Implement the accepted v0.1 design in core/Git/Copilot integration slices, then the small WPF product. Preserve the verified reviewer boundary and fail closed on configuration, capability, transport, or repository-integrity failures.

## Current state

- Phase 0 (design) is complete.
- Phase 1 authenticated proof is complete for Copilot CLI 1.0.91. Its effective tool manifest was restricted to `view`, `grep`, and `glob`; adversarial fixture fingerprints remained unchanged. See the report for probe-by-probe limits.
- Phase 2 independent verification is complete: **VERIFIED WITH REQUIRED DESIGN CHANGES**. The model tool restriction held, but profile hooks and MCP startup can execute outside that manifest and Phase 1's launch allowed sibling temp reads. See the [Phase 2 report](docs/phase2-copilot-cli-verification.md).
- The first executable WPF slice was committed as `3cf03e8`. The accumulated implementation was checkpointed as `93ad239` on `codex/ai-review-desk-secure-review`: shell refinements, project Overview/Reviews navigation, plural profile defaults, shared-policy/profile inspection, production prompt preview, local Git scopes/fingerprints, review/history/handoffs and fail-closed Copilot transport/configuration. Secure review acceptance remains incomplete; main has not received this feature. See [verification and blocker](docs/copilot-integration-verification.md).
- The reproduced inline-hook startup risk remains relevant. Under the refined credential rule, the scanner now examines approved JSON structure while skipping credential values without decoding/copying/returning them. The real dedicated profile passes preflight. The first production review exited unauthenticated, with no review events/result and an unchanged repository fingerprint. The explicit execution gate was restored with that precise reason; structural preflight alone cannot enable reviews.
- CLI 1.0.91 provides official `login --web-flow` but no supported noninteractive account-status or sign-out command. Identity/authentication remain unknown; sign-out and switching are disabled. Full authenticated review acceptance is not established.

## Accepted direction

- AI Review Desk is a Windows-only .NET 10 WPF desktop product that coordinates constrained, independent Copilot reviews; it is not a general coding agent.
- Codex remains the primary engineering and implementation agent. Carlo retains final decision authority.
- AI Review Desk owns repository inspection and supplies deterministic review context. Each review uses an ephemeral run directory; the repository is supplied separately through `--add-dir`. `--disallow-temp-dir` is required, and read reach is a local-desktop trust assumption rather than an OS sandbox guarantee.
- Use a dedicated, persistent Copilot CLI profile at `%LOCALAPPDATA%\AIReviewDesk\Copilot`, authenticated through the normal `copilot login --web-flow` OAuth flow. The official CLI alone consumes credentials. AI Review Desk may structurally inspect input in-place but must never materialize, retain, copy, log, persist, display, return, compare, transform, transmit or expose OAuth values. A fresh empty Copilot home does not inherit the user's normal CLI authentication.
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

Windows keyring support/plaintext fallback possibilities, prompt-mode controls, a tested GitHub CLI fallback opt-out and machine-policy presence guards remain unchanged. Actual OAuth storage is not inferred from account metadata. The structural scanner accepts only understood credential/account/state shapes, rejects unknown/executable fields and emits fixed categories/booleans. All 132 tests pass; Release build passes with the existing NU1900 vulnerability-feed warning. The latest report distinguishes this progress from historical uncommitted passes.

Resolve why the dedicated profile cannot authenticate with the exact required launch/environment contract, without dropping `--no-auto-login`, accepting unrelated fallback credentials or consuming secrets. The bounded synthetic auth-selection probe was inconclusive; do not claim this flag disables all saved-profile authentication. Then verify completed real JSONL and remaining startup/managed-setting assumptions before enabling `CopilotContract.ReviewContractVerified`. Preserve temp restrictions, fixed prompt switches, app-owned profile/run cwd, policy checks, exact tools, completed-run validation, cancellation and fingerprints. Product release remains blocked until real authenticated/manual acceptance passes.

The original baseline passed 18 tests and manual project-management checks. This pass manually verified Overview/Reviews navigation, scroll reset, truthful account/config state, the disabled review gate and Security + Database / EF prompt preview. Initial launch-helper failures recovered when the delayed window appeared. No completed result-flow claim is made. Exact evidence and delivery status are in the [integration report](docs/copilot-integration-verification.md).

## Artifact and repository state

- Artifact policy: local-tracked documentation in this repository, consistent with its source-controlled context documents.
- This work item is the single current-state snapshot for this objective; supporting design documents are linked above.
- Task-scoped commit, merge, and push authority for AI Review Desk work is defined in [`AGENTS.md`](AGENTS.md).
- No global Codex configuration change is part of this work item.

## Current evidence

- [Phase 1 proof harness](phase1-harness/README.md)
- [Phase 1 contract proof and recommendation](docs/phase1-copilot-cli-contract-proof.md)
- [Phase 2 independent verification and revised contract](docs/phase2-copilot-cli-verification.md)
