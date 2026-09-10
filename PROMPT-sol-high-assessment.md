# Prompt: Sol High Global Routing Assessment

> Historical assessment prompt. The approved policy correction was implemented on 2026-09-10; do not use this prompt as the current installed-state description.

Use **Sol High**.

This is an assessment-only pass.

Do not modify files.
Do not create or update the global Codex configuration.
Do not delegate to sub-agents.

## Context

This local project is a durable context repository for refining the installed global Codex multi-agent/model-routing setup.

Read these files first:

- `README.md`
- `01-goals-and-principles.md`
- `02-current-global-design.md`
- `03-viv-real-world-evidence.md`
- `04-terra-reconnaissance.md`
- `05-assessment-scope.md`

The AI Optimisation project that produced this context originally existed in ChatGPT, not Codex. This local directory now serves as the Codex-side source of truth.

The installed global Codex configuration is separate from this project and was previously identified under:

`C:\Users\henns\.codex`

Do not repeat the full reconnaissance unless a specific material claim needs verification against the installed global files.

## Task

Challenge the current diagnosis and determine the smallest coherent correction required to improve global multi-agent/model-routing efficiency without reducing implementation quality.

The strongest current hypothesis is:

- the global philosophy is mostly correct;
- the installed role taxonomy couples generic reasoning categories directly to Sol;
- there are no explicit cheaper explorer/researcher routes;
- normal verification is coupled to Sol High;
- downward handoff/de-escalation and anti-duplication verification rules are not explicit enough.

Do not accept that hypothesis blindly.

## Required assessment

Determine whether the evidence supports:

1. role taxonomy/model coupling as the primary cause;
2. missing exploration/research routing as a contributing cause;
3. `verifier_standard -> Sol High` being overly expensive as a normal default;
4. absence of explicit downward handoff/de-escalation as a policy gap;
5. insufficient distinction between low reasoning effort and low-cost model choice;
6. insufficient anti-duplication rules between delegated work and master verification;
7. missing same-model/same-effort delegation justification;
8. any additional configuration or implementation problem not yet identified.

Clearly state any conclusion you disagree with.

## Global routing requirements

Preserve these principles:

- any model/reasoning level may be the master;
- the master may delegate upward or downward;
- child routing must not default conceptually to the master's model family;
- every delegated task should use the lowest-cost capability that can reliably satisfy explicit bounded acceptance criteria;
- premium reasoning should be concentrated where ambiguity, consequence, or verification value justifies it.

Define normal starting routes and escalation triggers for:

- targeted repository inspection/search;
- broad repository exploration;
- external/documentation research;
- mundane file creation/editing;
- project/solution wiring;
- documentation;
- routine test execution;
- regression test creation;
- normal implementation;
- complex implementation;
- debugging;
- architecture/design;
- independent verification;
- security/safety/data-loss/migration/concurrency-sensitive review.

Treat these as guidance, not rigid task-to-model mappings.

## Verification policy

Define three separate concepts.

### 1. Integration verification

Normally performed by the master.

The master checks:

- acceptance criteria;
- relevant returned evidence;
- relevant diff/output;
- integration surface;
- tests/results needed to substantiate acceptance.

The master should not automatically repeat the entire delegated investigation or implementation.

### 2. Independent verification

Use another agent when independence materially increases confidence.

Assess whether Terra High is an appropriate normal starting point for substantive independent verification.

### 3. Deep/high-risk verification

Use Sol High when consequence or reasoning difficulty warrants it, including examples such as:

- security boundaries;
- authentication/authorization;
- migrations;
- data loss/corruption;
- transaction/rollback semantics;
- difficult concurrency;
- difficult architecture;
- conflicting or incomplete evidence.

Astra should remain exceptional.

## Full re-analysis

Define when the parent or verifier should substantially redo a delegated task.

Triggers should include at least:

- incomplete evidence;
- unresolved uncertainty;
- conflicting findings;
- changed assumptions;
- acceptance-test discrepancy;
- high-risk boundary where targeted verification is insufficient.

Otherwise verification should not duplicate the worker's complete reasoning process.

## De-escalation/downward handoff

Assess and, if appropriate, define this rule:

When high-capability reasoning resolves ambiguity and turns the remaining work into bounded deterministic implementation, reassess routing and hand the remaining work downward where appropriate.

Example:

- Sol High resolves difficult transaction semantics.
- Terra implements the explicit design.
- Luna performs routine validation or documentation.

Do not keep premium capability assigned merely because it solved an earlier hard part.

## Same-model delegation

If child model and reasoning level are identical to the master, require an explicit reason why a separate child adds value.

Valid examples:

- independent verification;
- adversarial review;
- context isolation;
- useful parallel investigation.

Convenience alone is insufficient.

## Low/Light guidance

Explicitly distinguish:

- low reasoning effort;
- low-cost model.

Do not assume Sol Low is the cheapest appropriate route merely because its reasoning effort is Low.

Assess useful roles for Luna Low/Medium and Terra Low/Medium before escalating model family.

## File-editing policy

Ordinary file creation/editing should normally use cheaper capability when:

- the intended change is defined;
- file scope is bounded;
- acceptance criteria are explicit;
- architecture has already been decided.

The master should review the resulting diff rather than reproduce the edit.

## Reporting contract

Every future multi-agent run should report:

- master model and reasoning level;
- each delegated subtask;
- child model;
- reasoning level;
- why that route was chosen;
- outcome;
- how the master verified/integrated it.

If the same model/effort as the master was used, include the justification.

If no child was used:

`Delegation: none`

## Required output

Return the assessment in the conversation only.

Structure it as:

1. Assessment of the existing diagnosis.
2. Root cause ranked:
   - primary;
   - contributing;
   - ruled out/unsupported.
3. Smallest coherent policy changes.
4. Proposed routing matrix.
5. Verification policy.
6. Escalation policy.
7. De-escalation/downward-handoff policy.
8. Same-model delegation policy.
9. Low/Light usage policy.
10. Exact installed global files/sections that would need modification.
11. Role definitions that should:
    - remain;
    - be renamed;
    - change model/effort;
    - become escalation-only;
    - be added;
    - be removed.
12. Final classification:
    - documentation clarification only;
    - small policy/configuration correction;
    - moderate routing redesign;
    - fundamental redesign.

Prefer modifying the existing structure over proliferating roles.

Do not implement changes in this pass.

Finish with:

`Delegation: none`
