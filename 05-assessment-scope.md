# Assessment Scope Before Any Change

This is the historical assessment scope that led to the approved 2026-09-10 correction. Its questions have been answered and the approved policy has been implemented; retain it as decision evidence, not as a current prohibition on the completed change.

## Root-cause questions

1. Is role taxonomy/model coupling the primary cause of Sol-heavy routing?
2. Are generic labels such as `reasoning_light` and `reasoning_standard` misleading because they appear capability-neutral while selecting Sol?
3. Does the absence of explorer/researcher routing create unnecessary escalation?
4. Is `verifier_standard -> Sol High` too expensive as a normal default?
5. Is there an explicit enough distinction between:
   - low reasoning effort;
   - low-cost model selection?
6. Is downward handoff/de-escalation missing after hard reasoning resolves ambiguity?
7. Are master verification rules too vague and therefore prone to duplicated work?
8. Should same-model/same-effort delegation require an explicit justification?
9. Can the correction be achieved mostly through policy wording and role semantics, without adding many new agent roles?

## Routing guidance that must be defined

The final global policy should provide normal starting routes and escalation triggers for:

- targeted repository inspection/search;
- broad repository exploration;
- research;
- mundane file edits;
- project/solution wiring;
- documentation;
- routine tests;
- regression test creation;
- normal implementation;
- complex implementation;
- debugging;
- architecture/design;
- independent verification;
- security/safety/data-loss/migration/concurrency-sensitive review.

These must be guidance, not rigid mappings.

## Verification levels to define

### Integration verification

Normally performed by the master against explicit acceptance criteria and evidence.

### Independent verification

Use a separate agent when independence materially adds confidence.

### Full re-analysis

Use when uncertainty, conflicting evidence, changed assumptions, high-risk boundaries, or verification discrepancies make targeted checking insufficient.

## Expected minimal direction

The current hypothesis is that the system needs a routing correction rather than a fundamental redesign.

Potential changes to assess include:

- make generic reasoning labels model-neutral or explicitly premium;
- add cheaper exploration/research routes;
- make Sol High verification risk-triggered instead of routine;
- add de-escalation/downward-handoff guidance;
- add anti-duplication verification guidance;
- require same-model delegation justification;
- explicitly route ordinary editing/documentation/testing downward where acceptance criteria are already clear.

These are hypotheses only. The next assessment must challenge them before implementation.
