# Terra High Reconnaissance

This is the historical, read-only reconnaissance result from before the 2026-09-10 approved policy correction. It must not be read as the current installed state: direct verification during implementation confirmed the primary default remains Terra High, and the generic reasoning and verification role mappings below have since been corrected. See `02-current-global-design.md` for the current installed routing record.

## 1. Installed files inspected

`CODEX_HOME` was unset in the shell, but `config.toml` declared:

`CODEX_HOME = C:\Users\henns\.codex`

under `[mcp_servers.node_repl.env]`.

The following installed global files were inspected:

- `config.toml`
- `AGENTS.md`
- `MULTI_AGENT.md`
- `AI_WORKFLOW.md`

Absent:

- `C:\Users\henns\.codex\AGENTS.override.md`
- `C:\Users\henns\.codex\agents\`
- `agents\*.toml`

No project-level `AGENTS.md`, `AGENTS.override.md`, `MULTI_AGENT.md`, or `AI_WORKFLOW.md` existed in the reconnaissance workspace.

Material routing instructions in global/personal `SKILL.md` files were also inspected.

## 2. Relevant role table

No role defined read/write capability. These were logical routing labels rather than installed custom-agent configurations.

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

There were no named explorer, repository-inspection, or researcher roles.

## 3. Routing/default/inheritance behaviour

The global primary defaults were reported as:

- `model = "gpt-5.6-terra"`
- `model_reasoning_effort = "high"`

The `[agents]` section only enabled agents and set concurrency. It did not define child model/effort defaults.

The installed policy required every child spawn to explicitly specify model and effort.

It also stated that logical role names, `task_name`, and custom agents were not trusted for capability selection.

Default subagent model/effort values were intentionally omitted.

The local files did not establish a model/effort inheritance rule for omitted values.

Reported precedence:

1. invocation-specific explicit model/effort; explicit user requests override automatic routing;
2. more-specific repository instructions;
3. global `AGENTS.md` / `MULTI_AGENT.md`;
4. global scalar primary defaults in `config.toml`.

No project-specific routing document existed in the reconnaissance workspace.

## 4. VIV routing evidence

Confirmed:

- session metadata contained several VIV-related sessions;
- one inspected candidate session used Sol as primary model.

Unavailable/unknown:

- the brief search did not recover the exact child task names;
- exact child spawn arguments were not recovered;
- the actual logical role names selected in the VIV run could not be confirmed.

The reconnaissance intentionally stopped rather than spending substantial effort reconstructing old session history.

## 5. Most likely cause of Sol-only delegation

### Strongly supported: role taxonomy/model coupling

The taxonomy directly maps generic reasoning levels to Sol and maps normal verification to Sol High.

The observed sequence:

- Sol Low
- Sol Medium
- Sol High

closely mirrors:

- `reasoning_light`
- `reasoning_standard`
- `verifier_standard`

### Strongly supported: explicit spawn-time selection driven by those labels

The installed policy requires explicit child model/effort.

The exact role-to-child mapping was not confirmed because invocation evidence was unavailable.

### Ruled out: configured Sol fallback/default

The installed scalar model default was Terra High and no child default keys existed.

### Ruled out for the Low/Medium children: simple parent inheritance

A Sol High parent alone does not explain children running at Sol Low and Sol Medium.

### Possible: ambiguous inspection classification

There was no explorer/researcher role.

`Investigation` was explicitly associated with `reasoning_standard` / Sol, while deterministic discovery was associated with Luna.

A generic inspection task could therefore be classified into the more expensive route even when its acceptance criteria were bounded.

### Possible: routing instructions not followed in the historical run

The installed policy stated that mundane work should be routed downward.

Exact historical child invocation evidence was unavailable.

### Historical runtime limitation

The documentation contained a prior warning that a named request for Luna Low had once executed as Sol High.

This could not explain the observed Sol Low / Sol Medium pattern and was not considered the primary cause.

## 6. Important gaps for the next assessment

- exact child spawn arguments from the VIV run;
- whether each inspection had sufficiently bounded acceptance criteria for Luna/Terra;
- why reasoning roles were selected rather than mechanical/discovery routes;
- whether the historical VIV run predated or bypassed the currently installed explicit-routing instructions.

## 7. Files likely relevant to a future correction

- `C:\Users\henns\.codex\config.toml`
- `C:\Users\henns\.codex\AGENTS.md`
- `C:\Users\henns\.codex\MULTI_AGENT.md`

A project-level override is not currently required because the intended policy is global.

## Reconnaissance conclusion

The strongest current explanation is a policy-taxonomy problem rather than a Codex model-inheritance or default-model problem.

Delegation: none
