# Work Item: AI Review Desk v0.1 Implementation

## Objective

Implement the accepted v0.1 design in core/Git/Copilot integration slices, then the small WPF product. Preserve the verified reviewer boundary and fail closed on configuration, capability, transport, or repository-integrity failures.

## Current state

- Capability discovery and certification replaces the ad-hoc model table with live discovery, immutable bundled baseline evidence and atomic local certificates. Explicit user testing uses owned disposable fixtures and the unchanged production validator; runtime drift suspends use. The independently proved CLI 1.0.91 `rg` capability permits locally certified Luna / High with `view,rg,glob`, while Sonnet retains `view,grep,glob` and Auto remains a separate mode. Defaults remain Sonnet 5.5 / High. See [current certification evidence](docs/copilot-integration-verification.md#capability-discovery-and-certification--2026-10-04).
- The preceding model/reasoning and usage slice added project defaults, schema-4 requested/observed history, Review Again reuse, restricted account metadata and actual token/credit usage. Its earlier Luna tool mismatch correctly failed closed and motivated this certification slice; its strict output rejection evidence remains historical.

- The first dogfooding fix slice is delivered: compact scrolling project navigation with selected-project tabs, reachable default profiles, empty-scope preview without bogus Failed history, centralized absolute Git resolution, bounded preparation progress/diagnostics, faster deterministic fingerprinting, schema 3 tracked/untracked metadata and truthful saved-account/login feedback. Full tests: 207 passed, 0 failed, 0 skipped; Release build succeeded. Actual Release WPF and a real synthetic Copilot review passed. Client access remained read-only and the accepted security contract is preserved. See [dogfooding findings and verification](docs/dogfooding-fixes.md).
- The Review History and Results slice is delivered: each project's Reviews opens newest-first History, historical runs open focused read-only details, and New review remains one click away. Finding selection, full detail, severity/certainty/category filters and title/file search support accepted results. Zero findings and Failed/Cancelled/Stale/Unsupported states are explicit; only Completed results expose the shared independent handoffs.
- Review again reuses configuration only, clears unavailable profiles/paths with an explanation and prepares a fresh snapshot through the normal preview/run flow. Record schema 2 adds captured profile names; existing history defaults safely and is preserved. Project removal retains stored history without reassociating it with a newly registered project. The accepted secure runner is changed only to capture the additive record metadata.

- Phase 0 (design) is complete.
- Phase 1 authenticated proof is complete for Copilot CLI 1.0.91. Its effective tool manifest was restricted to `view`, `grep`, and `glob`; adversarial fixture fingerprints remained unchanged. See the report for probe-by-probe limits.
- Phase 2 independent verification is complete: **VERIFIED WITH REQUIRED DESIGN CHANGES**. The model tool restriction held, but profile hooks and MCP startup can execute outside that manifest and Phase 1's launch allowed sibling temp reads. See the [Phase 2 report](docs/phase2-copilot-cli-verification.md).
- The first executable WPF slice was committed as `3cf03e8`. The accumulated implementation was checkpointed as `93ad239` on `codex/ai-review-desk-secure-review`: shell refinements, project Overview/Reviews navigation, plural profile defaults, shared-policy/profile inspection, production prompt preview, local Git scopes/fingerprints, review/history/handoffs and fail-closed Copilot transport/configuration. The current secure-review vertical slice now has real production and WPF acceptance under the revised saved-account contract. See [verification evidence](docs/copilot-integration-verification.md).
- Scanner progress is committed as `54bc580`; authentication evidence as `c41dabb`. The corrected contract allows the saved dedicated profile and omits `--no-auto-login`, while preventing unintended token/provider/GitHub CLI/other-home fallback. Real-profile and empty-profile controls passed; the actual production runner completed zero-findings, known-finding and combined-profile reviews, live cancellation and during-run stale checks. The temporary execution gate is removed; per-run security validation remains mandatory.
- CLI 1.0.91 provides official `login --web-flow` but no supported noninteractive account-status or sign-out command. Settings distinguishes successful sign-in and structurally understood saved-account configuration while reporting unknown identity/live status truthfully; sign-out and switching remain disabled. Successful review authentication does not fabricate a persistent account-status API.

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

## Verified production contract

The revised production contract uses a System32-only child PATH and rejects GitHub CLI in Windows executable search directories before launch. It retains the accepted scanner, machine/repository checks, false hook/extension switches, exact tools and all other disabling flags. Windows keyring support/plaintext fallback possibilities remain documented; the exact credential-store target/backend for this account is unobserved. Profile-selected saved authentication is established behaviorally without credential access.

The first real tool-using run revealed that 1.0.91 completion events omit toolName. Validation now correlates toolCallId to an allowed start and rejects unmatched, conflicting, reused or unfinished calls. WPF acceptance also required explicit UTF-8 transport. Exact test/build, manual UI and source-control evidence is in the latest section of the [integration report](docs/copilot-integration-verification.md). Earlier blocked-pass sections remain historical evidence.

## Artifact and repository state

- Artifact policy: local-tracked documentation in this repository, consistent with its source-controlled context documents.
- This work item is the single current-state snapshot for this objective; supporting design documents are linked above.
- Task-scoped commit, merge, and push authority for AI Review Desk work is defined in [`AGENTS.md`](AGENTS.md).
- No global Codex configuration change is part of this work item.

## Current evidence

- [Phase 1 proof harness](phase1-harness/README.md)
- [Phase 1 contract proof and recommendation](docs/phase1-copilot-cli-contract-proof.md)
- [Phase 2 independent verification and revised contract](docs/phase2-copilot-cli-verification.md)
