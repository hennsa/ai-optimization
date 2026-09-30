# Current Global Design

This records the global Codex routing policy and a configuration/runtime snapshot from 2026-09-24, with subsequent runtime and reporting evidence added below. The global optimization setup is reusable across repositories; project repositories remain model-routing agnostic.

## Configuration snapshot and primary selection

At the 2026-09-24 snapshot, the active Codex home was `C:\Users\henns\.codex` and `config.toml` persisted:

```toml
model = "gpt-6-luna"
model_reasoning_effort = "high"
```

Persisted `model` and `model_reasoning_effort` values are saved/default primary-selection state. The user or invocation can select the primary for a particular turn independently. The `[agents]` section enables agents and sets a concurrency limit; it does not define child model or effort defaults. Each child delegation must explicitly select both under the routing policy below.

At the forensic read on 2026-09-30 07:49 UTC, the persisted primary selection was `gpt-6-sol` / `high`. At a later documentation-check read on 2026-09-30 09:47 UTC, it was `gpt-6-luna` / `medium`, matching that turn's recorded settings. These dated observations show that the saved selection can change; neither value establishes the model/effort used for a different turn. Use per-turn runtime/session metadata when available.

Standalone Codex CLI and Codex Desktop can use different Codex runtimes. The investigated standalone CLI was `0.157.1`; the VIV Desktop session recorded `0.158.0-alpha.2.1`. Their configuration loading and invocation paths should not be assumed identical.

## Current logical routing roles

The global role matrix is maintained in `C:\Users\henns\.codex\MULTI_AGENT.md` and summarized in `AGENTS.md`.

| Role | Model | Effort | Intended work |
|---|---|---:|---|
| `mechanical_light` | GPT-6 Luna | Medium | Deterministic checks, known commands, formatting, result collection |
| `mechanical` | GPT-6 Luna | Medium | Mechanical work requiring limited judgment |
| `implementation_light` | GPT-6 Luna | Medium | Straightforward narrow implementation |
| `implementation_standard` | GPT-6 Luna | High | Normal bounded production implementation |
| `implementation_deep` | GPT-6 Luna | High | Difficult implementation with settled requirements |
| `reasoning_light` | GPT-6 Luna | Medium | Ordinary reasoning with clear acceptance criteria |
| `reasoning_standard` | GPT-6 Luna | High | Bounded investigation, design, or debugging |
| `reasoning_deep` | GPT-6 Sol | High | Difficult ambiguity, cross-system semantics, or unresolved architecture |
| `reasoning_frontier` | GPT-6 Astra | High | Exceptional frontier-level reasoning |
| `verifier_standard` | GPT-6 Luna | High | Substantive independent verification |
| `verifier_deep` | GPT-6 Sol | High | Difficult or high-risk independent verification |
| `verifier_frontier` | GPT-6 Astra | High | Exceptional, high-consequence verification |

Luna Medium is the default for low-risk, clearly bounded discovery, mechanical changes, routine documentation, straightforward implementation, simple tests, and bounded diagnostics. Luna High is the default for substantial but well-bounded implementation, reasoning, governance review, pre-commit inspection, final diff review, and milestone validation. Sol High is reserved for conflicting authority, subtle cross-system semantics, difficult root-cause analysis, unresolved architecture, or deep verification when Luna High is insufficient. Astra High is exceptional and is not the normal deep-reasoning route.

`mechanical_light` uses Luna Medium because Luna Low child execution has not yet been proven in this environment. Luna Low is an unverified optimization that may be revisited; it is not considered unsupported.

## GPT-6 smoke evidence (2026-09-24; standalone CLI 0.156.1)

The reported standalone CLI metadata is `0.156.1`. The previous GPT-6 HTTP 400 did not recur. Delegated GPT-6 Luna Medium, Luna High, and Sol High executions succeeded. Luna Low was not proven because the smoke runtime/tool was unavailable, not because the model was rejected. Primary GPT-6 execution completed successfully, although its exact model/effort identity was not exposed programmatically.

These smoke results resolve the previous old-alpha CLI compatibility blocker for the routes used by the current workflow. No broad compatibility investigation is needed unless new evidence appears.

## Subsequent VIV runtime and reporting evidence (2026-09-29)

Per-turn records for the VIV Desktop investigation and implementation show the primary as `gpt-6-sol` / `high`. Their completion prose incorrectly reported `gpt-6-luna` / `high`. A later, separate narrow frontend correction in the same Desktop thread was recorded as Luna High. Historical per-turn records are authoritative for those turns; do not apply the thread's later model setting retrospectively. The child spawn arguments and child per-turn records show the delegated frontend and ZAPP work used Luna High, matching the routed child reports.

Quota consumption is not model-identification evidence. Report a primary model and effort only when reliable runtime/session metadata for that turn is available to the agent; otherwise mark it unavailable/not authoritatively exposed or omit it. Do not infer it from saved defaults, routing policy, child routes, or generated completion text. See `experiments/primary-runtime-reporting.md` for session IDs and record locations.

## Superseded routing and historical evidence

Before this migration, the operational matrix used GPT-5.6 Luna Low/Medium, GPT-5.6 Terra Low/Medium/High, and GPT-5.6 Sol High routes; the two frontier roles already used GPT-6 Astra High. That matrix is superseded by the active GPT-6 matrix above. The earlier dated reconnaissance and primary-model experiments remain historical records in `04-terra-reconnaissance.md` and `experiments/primary-model-persistence.md`; they were not cosmetically rewritten.

The persisted primary selection is independent of child routing. The top-level `model` and `model_reasoning_effort` values can change when the user selects another primary model and do not act as child defaults.

## Routing application lesson (2026-09-30)

The read-only reconstruction audit used Luna High children for two bounded evidence searches from a Luna Medium primary. Retrospective assessment found those targeted repository-history and rollout searches fit the existing Luna Medium route. This was avoidable over-routing in applying the policy, not a routing-matrix defect; the matrix was not changed. Continue to reassess capability against each bounded task rather than carrying a higher route by default.
