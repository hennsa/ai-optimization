# Current Global Design

This file records the installed design and the compatibility blocker found during the GPT-6 routing assessment on 2026-09-24.

## Installed global location

The shell environment did not expose `CODEX_HOME`, but the installed configuration referenced:

`C:\Users\henns\.codex`

Relevant installed files inspected:

- `config.toml`
- `AGENTS.md`
- `MULTI_AGENT.md`
- `AI_WORKFLOW.md`

Absent during reconnaissance:

- `AGENTS.override.md`
- `agents\`
- `agents\*.toml`

No project-specific routing files were present in the reconnaissance workspace.

The installed Codex CLI is `0.155.0-alpha.16.3`, at:

`C:\Users\henns\AppData\Local\OpenAI\Codex\bin\80f78947ad880e6e\codex.exe`

## Currently persisted primary selection

The active `config.toml` currently persists this primary selection:

- `model = "gpt-6-luna"` (Luna)
- `model_reasoning_effort = "high"`

The top-level `model` and `model_reasoning_effort` values in `config.toml` represent the currently persisted primary selection. They can change when a user selects another primary model or reasoning effort in Codex; they are not fixed global routing defaults or child model/effort defaults.

The preceding current-state record documented GPT-5.6 Terra High. The live configuration had already moved to GPT-6 Luna High when inspected for this task, so no `config.toml` edit was made.

The `[agents]` section enabled agents and configured concurrency, but did not define child model/effort defaults.

## GPT-6 compatibility blocker

The installed CLI is `0.155.0-alpha.16.3`. A minimal run using the active ChatGPT account returned HTTP 400:

> The `gpt-6-luna` model is not supported when using Codex with a ChatGPT account.

The CLI also warned that its local model metadata did not contain `gpt-6-luna`. The invocation reached model startup, but the service rejected it before a response was generated. Therefore GPT-6 routing has not been enabled in the global policy. The persisted primary value predates this task and remains unchanged.

The current Codex model manifest lists stable client version `0.155.0` as the minimum for GPT-6 Luna and Sol. The installed `0.155.0-alpha.16.3` is a prerelease below that stable minimum; Codex `0.156.0` was released on 2026-09-22. The next compatibility check requires a user-authorized upgrade to stable `0.156.0` or later, followed by another minimal CLI smoke test. This task did not install or upgrade Codex.

References: [Codex model manifest](https://github.com/openai/codex/blob/main/codex-rs/models-manager/models.json), [Codex 0.156.0 release](https://github.com/openai/codex/releases/tag/rust-v0.156.0).

## Current logical routing roles

These are the existing global starting routes; no GPT-6 route migration was applied:

| Role | Model | Effort | Purpose |
|---|---|---:|---|
| `mechanical_light` | GPT-5.6 Luna | Low | Deterministic checks, known commands, formatting, result collection |
| `mechanical` | GPT-5.6 Luna | Medium | Mechanical work with limited judgment |
| `implementation_light` | GPT-5.6 Terra | Low | Straightforward narrow implementation |
| `implementation_standard` | GPT-5.6 Terra | Medium | Normal bounded production implementation |
| `implementation_deep` | GPT-5.6 Terra | High | Difficult implementation |
| `reasoning_light` | GPT-5.6 Terra | Low | Ordinary bounded reasoning with clear acceptance criteria |
| `reasoning_standard` | GPT-5.6 Terra | Medium | Normal bounded analysis, investigation, design, or debugging |
| `reasoning_deep` | GPT-5.6 Sol | High | Difficult ambiguity, concurrency, architecture, or cross-system reasoning |
| `verifier_standard` | GPT-5.6 Terra | High | Normal substantive independent verification |
| `verifier_deep` | GPT-5.6 Sol | High | High-risk or difficult independent verification |
| `reasoning_frontier` / `verifier_frontier` | GPT-6 Astra | High | Exceptional reasoning or high-consequence verification |

There were no dedicated logical roles for:

- repository explorer;
- broad codebase exploration;
- researcher;
- documentation researcher.

## Child capability selection

These are logical routing roles, not named Codex agent configurations. The installed multi-agent policy requires each child spawn to specify model and reasoning effort explicitly.

Logical role names and task names were not treated as sufficient capability selection by themselves.

The active global guidance supplies explicit starting assignments; it does not configure implicit child defaults. Route by demonstrated capability, risk, ambiguity, and bounded acceptance criteria.

## Historical GPT-5.6 evidence

The dated reconnaissance and persistence experiments remain historically accurate in `04-terra-reconnaissance.md` and `experiments/primary-model-persistence.md`; they were not rewritten during the compatibility assessment.

`MULTI_AGENT.md` is the detailed canonical routing source; it defines task-based starting routes, escalation and de-escalation, verification levels, same-model delegation justification, and compact reporting. Primary-model selection is independent of delegated child routing and is not evidence that delegated children inherit the master's model family.
