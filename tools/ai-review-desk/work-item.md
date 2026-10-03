# Work Item: AI Review Desk v0.1 Implementation

## Objective

Implement the accepted v0.1 design in core/Git/Copilot integration slices, then the small WPF product. Preserve the verified reviewer boundary and fail closed on configuration, capability, transport, or repository-integrity failures.

## Current state

- Phase 0 (design) is complete.
- Phase 1 authenticated proof is complete for Copilot CLI 1.0.91. Its effective tool manifest was restricted to `view`, `grep`, and `glob`; adversarial fixture fingerprints remained unchanged. See the report for probe-by-probe limits.
- Phase 2 independent verification is complete: **VERIFIED WITH REQUIRED DESIGN CHANGES**. The model tool restriction held, but profile hooks and MCP startup can execute outside that manifest and Phase 1's launch allowed sibling temp reads. See the [Phase 2 report](docs/phase2-copilot-cli-verification.md).
- The first executable WPF slice was committed as `3cf03e8`. The next slice is implemented in the working tree only and **blocked, incomplete, uncommitted and unpushed**: shell refinements, project Overview/Reviews navigation, plural profile defaults, full shared-policy/profile inspection, production prompt preview, local Git scopes/fingerprints, review/history/handoff scaffolding and fail-closed Copilot transport/configuration code. See [verification and blocker](docs/copilot-integration-verification.md).
- A fresh synthetic CLI 1.0.91 profile with a versioned inline hook in `config.json` executed a harmless startup command under constrained flags. The primary independently reproduced the verifier's result. The existing authenticated dedicated profile contains `config.json`, which can also hold authentication state; its contents were never read. Production launch rejects it. An explicit unverified-contract block also prevents an empty profile from bypassing the incomplete integration.
- CLI 1.0.91 provides official `login --web-flow` but no supported noninteractive account-status or sign-out command. Identity/authentication remain unknown; sign-out and switching are disabled. Full authenticated review acceptance is not established.

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

The continuation established installed Windows keyring support/plaintext fallback possibilities, prompt-mode controls, a GitHub CLI fallback opt-out and machine-policy presence guards. Actual browser OAuth storage and secret-free clean-home identity selection remain unresolved. `config.json` is rejected without opening it, `ReviewContractVerified` remains false, and Start review is disabled. No real review was forced through the boundary; no commit/push was made. See the integration report for evidence limits and 97-test/build results.

Resolve the mixed authentication/executable-configuration contract without reading, copying, logging or persisting credential values. Establish safe account inspection/management behavior. Then verify the complete startup surface (including remote managed settings) and actual completed JSONL before enabling `CopilotContract.ReviewContractVerified`. Preserve `--disallow-temp-dir`, `--no-auto-login`, fixed prompt-mode environment, app-owned profile/run cwd, machine-policy checks, exact tool restrictions, completed-run validation, process-tree cancellation and fingerprints. Product release remains blocked until real authenticated/manual review acceptance passes. The experimental Windows sandbox is not an assumed prerequisite.

The committed baseline passed 18 tests and manual project-management checks. The current working-tree evidence and exact final test/build counts are recorded in the [integration report](docs/copilot-integration-verification.md). Partial manual shell/profile/preview and blocked/stale-result checks do not constitute completion of the requested vertical slice.

## Artifact and repository state

- Artifact policy: local-tracked documentation in this repository, consistent with its source-controlled context documents.
- This work item is the single current-state snapshot for this objective; supporting design documents are linked above.
- Task-scoped commit, merge, and push authority for AI Review Desk work is defined in [`AGENTS.md`](AGENTS.md).
- No global Codex configuration change is part of this work item.

## Current evidence

- [Phase 1 proof harness](phase1-harness/README.md)
- [Phase 1 contract proof and recommendation](docs/phase1-copilot-cli-contract-proof.md)
- [Phase 2 independent verification and revised contract](docs/phase2-copilot-cli-verification.md)
