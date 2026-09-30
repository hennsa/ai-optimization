# AI-assisted development workflow

This file reconstructs reusable workflow guidance for development across repositories. Follow the user's task and local repository instructions; use judgment rather than treating this sequence as a rigid checklist.

## Understand and plan

- Establish the requested outcome, constraints, acceptance criteria, and relevant repository instructions before acting.
- Choose the least expensive primary model and reasoning effort that can reliably handle the current task, based on its actual depth, ambiguity, risk, verification needs, and expected work. Escalate when observed complexity warrants it rather than pre-paying for hypothetical difficulty. After a difficult portion is resolved, reassess the remaining work and de-escalate the primary where appropriate; do not retain a costly primary merely because an earlier phase needed it. Do not optimize cost so aggressively that safe, routine work is unnecessarily handed back to the human. Keep primary selection distinct from persisted/default selection and child routing.
- Inspect the narrowest useful set of files, configuration, history, and runtime evidence. Distinguish current authority from historical or superseded material.
- Make a concise plan when the task has meaningful dependencies or multiple stages. Keep moving on independent work while resolving uncertainty.
- Delegate bounded, independently actionable work when it reduces total effort or improves confidence. Give each child a self-contained prompt with scope, evidence needed, and acceptance criteria; route it under `MULTI_AGENT.md`.

## Implement

- Preserve existing user work and unrelated configuration. Make the smallest coherent change that fulfills the request.
- Keep secrets and machine-specific private data out of repositories, logs, and reports.
- Reassess capability after difficult reasoning settles requirements; route straightforward remaining work to a suitable lower-cost model.
- Keep the user informed with concise progress updates during sustained work. Ask for a decision only when material ambiguity or an authorization boundary prevents safe progress.

## Verify and integrate

- Verify the changed behavior against stated acceptance criteria using proportionate checks and evidence. Follow repository-specific verification instructions and report what was actually run.
- Review delegated results against their acceptance criteria and integration surface. Avoid repeating the full task unless evidence, risk, or a discrepancy calls for deeper review.
- Escalate verification for security, data loss, migrations, authentication, concurrency, rollback, or other consequential boundaries.
- Report changed files or settings, rationale, verification results, and material limitations. State clearly when a check could not be run or did not establish the desired result.

## Context management

- Give children only the context needed to complete their assigned task. For V2 routed children, follow the fork rules in `MULTI_AGENT.md`; with `fork_turns = "none"`, include all necessary context in the task message.
- Prefer concise findings with paths, symbols, and relevant evidence over large raw dumps. Preserve key decisions, assumptions, and unresolved questions when summarizing long work.
- Keep independent investigations separate until their findings are ready to integrate. Revisit broader context when evidence conflicts or assumptions change.
