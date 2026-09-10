# Goals and Principles

## Primary goal

Improve global Codex multi-agent/model-routing efficiency without reducing implementation quality.

The routing policy must be reusable across projects such as VIV, Dark Tower, source-control research, and future repositories.

It must not contain project-specific routing rules except as examples or evidence.

## Master model principle

Any model and reasoning level may be the master.

The master may delegate both downward and upward.

The master model must not determine the model family used for delegated tasks.

## Cheapest reliable route

For every delegated subtask, deliberately choose the lowest-cost model and reasoning level that can reliably satisfy its explicit bounded acceptance criteria.

Do not default delegated work to the master's model family.

Do not equate "low reasoning effort" with "lowest cost".

For example, Sol Low may still be more expensive than a sufficiently capable Luna or Terra route.

## Delegation should reduce work, not duplicate it

Delegation is not useful when:

1. a worker performs a bounded task; and
2. the master then rereads everything and effectively performs the same task again.

The master remains responsible for correctness and integration, but normal verification should check the worker's output against explicit acceptance criteria rather than reproduce the full task.

Deeper re-analysis is justified when:

- evidence is incomplete;
- the worker is uncertain;
- verification detects a discrepancy;
- assumptions changed;
- the task touches a high-risk boundary;
- targeted verification is insufficient.

## Same-model delegation

If a child uses exactly the same model and reasoning level as the master, the run must state why separate delegation still adds value.

Valid reasons include:

- independent verification;
- adversarial review;
- context isolation;
- useful parallel investigation.

Convenience alone is not sufficient.

## File editing and mechanical work

Ordinary file creation/editing should not consume premium reasoning when:

- the intended change is already defined;
- file scope is bounded;
- acceptance criteria are explicit;
- architecture/design has already been decided;
- a cheaper model can implement it safely.

The master should normally review the resulting diff rather than reproduce the edit.

## Verification strategy

Expensive verification should be concentrated where it materially improves correctness.

Normal implementation does not automatically require Sol High verification.

Potential tiers:

- integration verification by the master;
- independent substantive verification using an appropriate cheaper model;
- deep/high-risk verification using Sol High when justified;
- exceptional frontier verification only for unusually difficult/high-consequence work.

## De-escalation

After difficult reasoning resolves ambiguity, routing must be reassessed.

Example:

- Sol High resolves difficult transaction semantics.
- Terra implements the explicit design.
- Luna performs routine validation or documentation.

Do not keep premium capability assigned merely because it solved an earlier hard part of the task.

## Reporting contract

Every multi-agent run should report:

- master model and reasoning level;
- each delegated subtask;
- delegated model;
- reasoning level;
- why that model/level was chosen;
- outcome;
- how the master verified/integrated it.

If no delegation was used, report:

`Delegation: none`
