# Current Global Design

This is the current global Codex routing policy and verified runtime state as of 2026-09-24. The global optimization setup is reusable across repositories; project repositories remain model-routing agnostic.

## Active configuration and runtime

The active Codex home is `C:\Users\henns\.codex`. The active `config.toml` was read directly and persists:

```toml
model = "gpt-6-luna"
model_reasoning_effort = "high"
```

The `[agents]` section enables agents and sets a concurrency limit; it does not define child model or effort defaults. Each child delegation must explicitly select both.

The recommended execution environment for GPT-6 work is the standalone Codex CLI, version `0.156.1`. The Windows Codex desktop app currently has a separate, older bundled runtime and is not the preferred GPT-6 execution path. This runtime difference is not a project blocker.

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

## GPT-6 smoke evidence

The reported standalone CLI metadata is `0.156.1`. The previous GPT-6 HTTP 400 did not recur. Delegated GPT-6 Luna Medium, Luna High, and Sol High executions succeeded. Luna Low was not proven because the smoke runtime/tool was unavailable, not because the model was rejected. Primary GPT-6 execution completed successfully, although its exact model/effort identity was not exposed programmatically.

These smoke results resolve the previous old-alpha CLI compatibility blocker for the routes used by the current workflow. No broad compatibility investigation is needed unless new evidence appears.

## Superseded routing and historical evidence

Before this migration, the operational matrix used GPT-5.6 Luna Low/Medium, GPT-5.6 Terra Low/Medium/High, and GPT-5.6 Sol High routes; the two frontier roles already used GPT-6 Astra High. That matrix is superseded by the active GPT-6 matrix above. The earlier dated reconnaissance and primary-model experiments remain historical records in `04-terra-reconnaissance.md` and `experiments/primary-model-persistence.md`; they were not cosmetically rewritten.

The persisted primary selection is independent of child routing. The top-level `model` and `model_reasoning_effort` values can change when the user selects another primary model and do not act as child defaults.
