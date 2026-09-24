# Current Global Design

This file records the installed design, directly re-verified during the GPT-6 Luna-first routing migration on 2026-09-24.

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

At the time of this migration, the active `config.toml` persisted this primary selection:

- `model = "gpt-6-luna"` (Luna)
- `model_reasoning_effort = "high"`

The top-level `model` and `model_reasoning_effort` values in `config.toml` represent the currently persisted primary selection. They can change when a user selects another primary model or reasoning effort in Codex; they are not fixed global routing defaults or child model/effort defaults.

The preceding current-state record documented GPT-5.6 Terra High. The live configuration had already moved to GPT-6 Luna High when inspected for this migration, so no `config.toml` edit was needed.

The `[agents]` section enabled agents and configured concurrency, but did not define child model/effort defaults.

## Logical routing roles

The current global logical routing roles are:

| Role | Model | Effort | Purpose |
|---|---|---:|---|
| `mechanical_light` | GPT-6 Luna | Low | Deterministic checks, known commands, formatting, result collection |
| `mechanical` | GPT-6 Luna | Medium | Mechanical work with limited judgment |
| `implementation_light` | GPT-6 Luna | Medium | Straightforward narrow implementation |
| `implementation_standard` | GPT-6 Luna | High | Standard bounded production implementation |
| `implementation_deep` | GPT-6 Luna | High | Technically difficult but well-specified implementation; escalate only if ambiguity or semantics warrant it |
| `reasoning_light` | GPT-6 Luna | Medium | Clear, bounded reasoning |
| `reasoning_standard` | GPT-6 Luna | High | Substantial but well-bounded investigation, design, or debugging |
| `reasoning_deep` | GPT-6 Sol | High | Genuinely difficult ambiguity, concurrency, architecture, or cross-system reasoning |
| `verifier_standard` | GPT-6 Luna | High | Normal substantive independent verification |
| `verifier_deep` | GPT-6 Sol | High | High-risk or difficult independent verification |
| `reasoning_frontier` / `verifier_frontier` | GPT-6 Astra | High | Exceptional reasoning or high-consequence verification |

There were no dedicated logical roles for:

- repository explorer;
- broad codebase exploration;
- researcher;
- documentation researcher.

## Child capability selection

These are logical routing roles, not named Codex agent configurations. The installed multi-agent policy requires each child spawn to specify model and reasoning effort explicitly.

Logical role names and task names were not treated as sufficient capability selection by themselves.

The active global guidance supplies explicit starting assignments; it does not configure implicit child defaults. Route by demonstrated capability, risk, ambiguity, and bounded acceptance criteria. Luna High is the operational baseline for substantial well-bounded work, not a guarantee that Luna is sufficient for every task. Reevaluate this Luna-first matrix against representative real workloads as model capability and cost change.

## Historical GPT-5.6 routing and evidence

The prior GPT-5.6 role assignments and persistence experiments remain in historical evidence, including `04-terra-reconnaissance.md` and `experiments/primary-model-persistence.md`. They have not been rewritten to imply GPT-6 was used in those observations.

`MULTI_AGENT.md` is the detailed canonical routing source; it defines task-based starting routes, escalation and de-escalation, verification levels, same-model delegation justification, and compact reporting. Primary-model selection is independent of delegated child routing and is not evidence that delegated children inherit the master's model family.
