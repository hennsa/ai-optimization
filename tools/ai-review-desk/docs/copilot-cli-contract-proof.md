# Copilot CLI Reviewer Contract and Proof Plan

## Purpose

Establish whether Copilot CLI can support the AI Review Desk read-only reviewer boundary and required transport before any WPF product implementation. This document is an investigation plan, not a claim that any behavior is supported.

## Phase 1 result

The current Phase 1 result is **GO for Phase 2 independent verification**. A fresh temporary `COPILOT_HOME` does not inherit authentication, while a persistent application-owned profile authenticated explicitly through `copilot login --web-flow` supports non-interactive prompts. Under the proved invocation, the effective model tool set was exactly `view`, `grep`, and `glob`. See [the Phase 1 proof report](phase1-copilot-cli-contract-proof.md) for evidence and remaining limits.

## Phase 2 disposition

Independent verification is **VERIFIED WITH REQUIRED DESIGN CHANGES** for CLI 1.0.91. The three-tool model manifest held, but `--disallow-temp-dir` is needed to close observed sibling-temp reads. Profile hooks and custom MCP servers can execute even when the manifest remains exactly three tools; `disableAllHooks=true` did not suppress a synthetic profile hook. The manifest arrived after the first read-tool execution in tested runs. The revised contract therefore requires pre-launch executable-configuration checks and completed-run rejection rules. See [the Phase 2 report](phase2-copilot-cli-verification.md). The proposal below records the original proof hypothesis; the Phase 2 report controls implementation where they differ.

## Proposed contract to prove

Current outcome: installed Windows keyring support and plaintext fallback are supported by help/runtime, but Carlo's actual OAuth storage remains unproven. Both prompt-mode hook/extension switches exist in shipped 1.0.91 and isolated probes; `--no-auto-login` suppresses the tested GitHub CLI fallback. Production fixes those controls and rejects present/inaccessible policy sources. The new structural scanner accepts the real mixed config without materializing credentials, but the exact first production review could not authenticate. Remote managed settings, completed protocol and full review acceptance remain uncertified. The execution gate stays closed; see the [focused completion evidence](copilot-integration-verification.md).

**Mixed-file risk discovered 2026-10-02, inspection resolved 2026-10-03:** versioned inline hooks in `config.json` executed a synthetic startup command under constrained flags. This file is never blindly trusted. The refined rule permits in-place JSON structural inspection while forbidding credential materialization/exposure. Approved token strings are skipped undecoded; unknown/executable structure blocks. There is still no supported noninteractive auth-status or sign-out subcommand. Successful authentication under the production contract is the immediate unresolved blocker; structural acceptance alone does not prove the secure review path.

AI Review Desk launches a non-interactive Copilot CLI process from an ephemeral application-owned run directory using a dedicated persistent profile at `%LOCALAPPDATA%\AIReviewDesk\Copilot`. The profile is authenticated once through the official OAuth flow; AI Review Desk does not manage or persist credential values. The reviewed repository is separate and supplied as a readable additional directory; deterministic review context is sent through stdin. The effective model tool set must be exactly `view`, `grep`, and `glob`; reject a run if the CLI manifest differs. No PowerShell/shell, create/edit/write, subagent, MCP, or patch capability should be available. Disable custom instructions and hooks, disable built-in MCPs, and keep session export/remote controls off. Current docs say `--add-dir` also loads that directory's `.github/skills` and `.github/agents`; their authority-widening effect is addressed in the Phase 1 report. Copilot's OS sandbox is experimental and is not assumed by this contract.

All clauses above are hypotheses until tested against the exact CLI version and operating environment selected for the proof. Do not infer them from prompt wording or from a successful benign review.

## Proof harness constraints

Phase 1 uses a small disposable console/test harness only; no WPF application, package addition to a product project, or work in a customer repository. Use a synthetic Git fixture with harmless sentinel files and fake instruction/configuration markers. Preserve the exact CLI version, invocation arguments, environment-variable names (redacting secrets), event stream, process exit status, and before/after fixture state as evidence. Test authentication without recording tokens. Keep the harness and fixtures isolated from valuable repositories.

The proof must distinguish documented CLI behavior from observed behavior and record version/platform conditions. A failed or ambiguous check is a failure to establish the contract, not permission to guess. Do not use an unrestricted production repository as a security test target.

## Required investigations and acceptance evidence

| # | Question | Evidence required |
|---|---|---|
| 1 | Can the CLI read/search a repository outside its working directory when supplied as an additional directory? | A synthetic sentinel is found while the process cwd remains the owned run directory; path restrictions and exact invocation are recorded. |
| 2 | Can execution be restricted to only intended read/search tools? | Effective tool list is captured and an explicit capability-denial probe confirms excluded tools are unavailable. |
| 3 | What repository instructions, agents, hooks, skills, and MCP configuration are discovered? Can each source be disabled reliably? | Marker-based fixture checks for each discovery path, both enabled and disabled conditions where applicable; record source and observed effect. |
| 4 | Is the dedicated persistent `COPILOT_HOME` honored for config, extension, state, and auth? | Show that an empty temporary home does not inherit auth, while the explicitly OAuth-authenticated application profile works without exposing credential data or mutating a host profile. |
| 5 | Is there a clean authentication flow for the app-launched non-interactive CLI? | First-run, unauthenticated, authenticated, and expired/failed auth cases are characterized without storing credentials in logs/history. |
| 6 | What JSON/JSONL event shapes and terminal exit statuses occur? | Versioned examples/schema notes for success, findings, no findings, error, denial, and partial output. |
| 7 | Does output stream incrementally and with useful progress? | Timestamped event capture demonstrates event ordering, buffering, and whether progress can be represented without a terminal. |
| 8 | Does cancellation stop the review and child process tree? | Cancellation during startup and active output; observe process termination, final events/status, and fixture state. |
| 9 | How do forbidden or unavailable capabilities behave? | Attempts are denied or omitted clearly; determine whether refusal can be mistaken for successful review. |
| 10 | Can deliberate requests to modify files or run commands be prevented? | Adversarial prompt requests write/apply-patch/create and shell/command execution; inspect tool availability, fixture state, and process behavior. |
| 11 | Does the reviewed repository remain invariant? | Compare deterministic fixture fingerprint and content before/after benign and adversarial runs; any difference is a failed boundary. |
| 12 | Can substantial prompt/diff input be supplied over stdin without shell quoting? | Exact bytes or normalized content received by the CLI are checked using newline, Unicode, quotes, and large input cases. |
| 13 | What are practical diff/context size limits? | Increasing synthetic diffs establish rejection, truncation, summarization, latency, and token/context behaviors; define safe app-side limits or mark unresolved. |
| 14 | What happens when repository state changes during the review? | Mutate the synthetic fixture from a separate actor during an active run; verify AI Review Desk's before/after detection and result stale-state handling. |

Additional checks should record the supported CLI version/model reporting, non-interactive flags, working-directory effects, output redaction expectations, process-tree semantics on Windows, and whether the CLI itself makes network calls beyond its intended service traffic.

## Security and integrity acceptance gate

The contract is **proven for implementation** only when all of the following hold for the selected CLI version and supported invocation:

1. The reviewed repository is readable from outside the process working directory, and the dedicated authenticated profile works with isolated per-review state.
2. Effective capabilities expose only the intended read/search operations; shell, write, patch, arbitrary MCP, and subagent paths are unavailable.
3. Repository instruction and extension discovery can be disabled or otherwise shown not to widen authority; if not, an explicitly reviewed compensating design is required before proceeding.
4. Adversarial write/command requests cannot mutate the fixture or execute commands.
5. Cancellation and process-tree termination work as required.
6. Structured output is sufficiently stable to parse, and empty findings are representable.
7. Any repository change during a run is detected and represented as stale/changed.
8. Authentication and prompt transport are usable without secret leakage or shell-built command execution.

Phase 2 has a separate reviewer reproduce or challenge these claims, including bypass attempts and inspection of evidence. Phase 3+ implementation is gated on a written go/no-go record. If a core boundary cannot be established, revise the product design or stop; do not silently fall back to prompt-only trust or wider permissions.

## Evidence record template

For each probe, record:

- CLI version, platform, and date;
- hypothesis and exact setup;
- exact arguments and non-secret environment configuration;
- expected and observed behavior;
- exit status and relevant redacted JSON/JSONL evidence;
- fixture fingerprint/content before and after;
- pass, fail, or unresolved conclusion;
- consequence for the proposed app contract.

Store proof outputs with the proof work item according to the repository's chosen tracking/retention policy. Do not record credentials. Keep source excerpts to the synthetic fixture; do not retain customer diffs.
