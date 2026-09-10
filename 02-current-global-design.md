# Current Global Design

This file records the installed design as reconstructed by a read-only Codex reconnaissance.

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

## Global primary defaults

The installed primary model defaults were reported as:

- model: Terra
- reasoning effort: High

The `[agents]` section enabled agents and configured concurrency, but did not define child model/effort defaults.

## Logical routing roles

The installed global role taxonomy was reported as:

| Role | Model | Effort | Purpose |
|---|---|---:|---|
| `mechanical_light` | Luna | Low | Deterministic checks, known commands, formatting, result collection |
| `mechanical` | Luna | Medium | Mechanical work with limited judgment |
| `implementation_light` | Terra | Low | Straightforward narrow implementation |
| `implementation_standard` | Terra | Medium | Normal bounded production implementation |
| `implementation_deep` | Terra | High | Difficult implementation |
| `reasoning_light` | Sol | Low | Small bounded analysis |
| `reasoning_standard` | Sol | Medium | Investigation, design, analysis, debugging |
| `reasoning_deep` | Sol | High | Difficult architecture/investigation/cross-system reasoning |
| `verifier_standard` | Sol | High | Independent substantive verification |
| `verifier_frontier` | Astra | High | Exceptional/high-consequence verification |

There were no dedicated logical roles for:

- repository explorer;
- broad codebase exploration;
- researcher;
- documentation researcher.

## Child capability selection

The installed multi-agent policy was reported to require each child spawn to specify model and reasoning effort explicitly.

Logical role names and task names were not treated as sufficient capability selection by themselves.

The documentation intentionally omitted default child model/effort values.

## Important structural observation

The design contains a tension:

- the orchestration policy says to choose the lowest-cost reliable capability;
- generic reasoning roles are directly coupled to Sol.

This means a task can be judged "light reasoning" and still route to Sol even if Luna Medium or Terra Medium could reliably satisfy the bounded acceptance criteria.

That tension is the main subject of the current optimisation work.
