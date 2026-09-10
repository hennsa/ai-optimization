# Current Global Design

This file records the installed design, directly re-verified during the approved policy correction on 2026-09-10.

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

The installed primary model defaults are:

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
| `reasoning_light` | Terra | Low | Ordinary bounded reasoning with clear acceptance criteria |
| `reasoning_standard` | Terra | Medium | Normal bounded analysis, investigation, design, or debugging |
| `reasoning_deep` | Sol | High | Premium escalation for genuinely difficult or high-risk ambiguity, architecture, concurrency, or cross-system reasoning |
| `verifier_standard` | Terra | High | Normal substantive independent verification |
| `verifier_deep` | Sol | High | High-risk or difficult independent verification |
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

## Implemented routing correction

The generic reasoning roles now start with Terra rather than directly coupling ordinary analysis to Sol. The policy also adds `verifier_deep` for difficult or high-risk independent verification, while retaining `reasoning_deep -> Sol High` as a premium escalation. `MULTI_AGENT.md` is the detailed canonical routing source; it now defines task-based starting routes, escalation and de-escalation, verification levels, same-model delegation justification, and compact reporting.

The intended and currently verified global primary default is Terra High (`gpt-5.6-terra` at `high`). Primary-model selection is independent of delegated child routing.
