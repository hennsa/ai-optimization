# Global multi-agent routing policy

This is the canonical detailed policy for global delegation. It applies across repositories and describes logical routing roles, not static named agent profiles. A role is a starting route; select capability from the task's explicit acceptance criteria, ambiguity, risk, and consequence.

## Primary and routing principles

- Any supported model and effort may act as primary. The user or invocation may select the primary independently of child-routing policy; the primary model does not constrain child capability.
- Treat persisted `model` and `model_reasoning_effort` as saved/default selection state, not proof of a particular turn's effective settings. Use reliable runtime/session metadata for that turn when reporting its primary model and effort. Never infer or guess them from the saved default, routing policy, child settings, or prior turns. If current-turn metadata is unavailable to the agent, report the primary model/effort as unavailable/not authoritatively exposed, or omit it.
- Delegation may route downward, sideways, or upward.
- For every routed delegated task, explicitly set both `model` and `reasoning_effort`. Never rely on inheritance or defaults.
- Choose the lowest-cost route that can reliably meet the bounded acceptance criteria. Low reasoning effort and low-cost model selection are different decisions.
- Luna Medium is the normal route for routine, mechanical, and clearly bounded work. Luna High is for substantial but bounded implementation, reasoning, or verification. Sol High is for genuinely difficult reasoning or verification. Astra High is exceptional/frontier work only. Luna Low is not part of the verified matrix.

## Logical role matrix

| Role | Model | Effort | Typical work |
|---|---|---|---|
| `mechanical_light` | `gpt-6-luna` | `medium` | Deterministic checks, known commands, formatting, result collection |
| `mechanical` | `gpt-6-luna` | `medium` | Mechanical work requiring limited judgment |
| `implementation_light` | `gpt-6-luna` | `medium` | Straightforward, narrow implementation |
| `implementation_standard` | `gpt-6-luna` | `high` | Normal bounded production implementation |
| `implementation_deep` | `gpt-6-luna` | `high` | Difficult implementation after requirements are settled |
| `reasoning_light` | `gpt-6-luna` | `medium` | Ordinary reasoning with clear acceptance criteria |
| `reasoning_standard` | `gpt-6-luna` | `high` | Bounded investigation, design, or debugging |
| `reasoning_deep` | `gpt-6-sol` | `high` | Difficult ambiguity, cross-system semantics, or unresolved architecture |
| `reasoning_frontier` | `gpt-6-astra` | `high` | Exceptional frontier-level reasoning |
| `verifier_standard` | `gpt-6-luna` | `high` | Substantive independent verification |
| `verifier_deep` | `gpt-6-sol` | `high` | Difficult or high-risk independent verification |
| `verifier_frontier` | `gpt-6-astra` | `high` | Exceptional, high-consequence verification |

## Task routing and escalation

- Use Luna Medium for targeted repository inspection, bounded exploration, mechanical edits, project wiring with settled requirements, documentation, routine tests, and bounded diagnostics.
- Use Luna High for substantial but scoped implementation, reasoning, debugging, architecture with bounded criteria, regression-test work, and normal substantive independent verification.
- Escalate to Sol High for conflicting authority, subtle cross-system behavior, difficult root-cause analysis, unresolved architecture, or high-risk verification such as security, authentication, migrations, data integrity, rollback, or difficult concurrency.
- Use Astra High only when frontier-level reasoning or exceptional consequences justify it and Sol High is insufficient.
- Reassess after ambiguity is resolved. Hand deterministic remaining work down to a suitable cheaper route rather than keeping premium capability assigned by inertia.

## V2 backend and fork behavior

The historical feature state is retained in `config.toml`: `multi_agent = true` and `multi_agent_v2 = false`. This feature value does not force GPT-6 models onto V1: the current GPT-6 model catalog selects the V2 backend. Do not describe GPT-6 as using V1. The runtime exposes child `model` and `reasoning_effort` overrides on its V2 spawn surface.

For a routed V2 child with model or effort overrides, set `fork_turns = "none"` by default. A positive integer string may be used deliberately to provide a limited number of recent turns when that context is useful and the override remains supported. Do not use a full-history fork (`fork_turns = "all"`, or omission where it defaults to full history) for a child requiring a model or effort override. The child task message must include enough context, requirements, evidence, and acceptance criteria to work independently when using `none`.

The configured concurrency limit is four spawned agent threads per session. `interrupt_message = true` preserves a model-visible record when an agent is interrupted.

## Verification and delegation efficiency

The primary agent normally performs integration verification: check the acceptance criteria, relevant returned evidence and diff/output, integration points, and required test results. Do not automatically repeat the entire delegated investigation or implementation.

Use an independent verifier when independence materially increases confidence. Use Sol High for difficult or high-risk verification and Astra only for exceptional, high-consequence verification. Re-analyze the full task only when evidence is incomplete, findings conflict, assumptions changed, a check disagrees with the result, uncertainty remains, or targeted verification is insufficient at a high-risk boundary.

Ordinary file creation or edits should use an appropriate cheaper route when the design is settled, scope is bounded, and acceptance criteria are clear. If child model and effort exactly match the primary's, state why separate delegation adds value, such as independence, adversarial review, context isolation, or useful parallelism. Convenience alone is not sufficient.

## Reporting contract

For each multi-agent run, report the primary model and effort only when reliable runtime/session metadata for the current turn is available to the agent; otherwise state that they are unavailable/not authoritatively exposed, or omit them. Do not substitute a self-report or an inference from saved configuration, routing policy, child settings, or a different turn. For each delegated task, report the exact child model and effort passed to `spawn_agent`, why that route was chosen, the outcome, and how the primary integrated or verified it. Treat spawn arguments as authoritative for the routed settings; claim actual child runtime settings only when reliable child runtime/session metadata is available. Include a justification for same-model/same-effort delegation. When no child is used, report `Delegation: none`.
