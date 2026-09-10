# AI Optimisation - Codex Context Pack

This directory is a durable context pack for refining the global Codex multi-agent/model-routing setup.

The goal is to improve cost and usage efficiency without reducing implementation quality.

## How to use this directory

Use this directory as the local Codex project for AI optimisation work.

For assessment or implementation chats, Codex should read the files in this directory first and treat them as the current project context.

The global Codex configuration itself lives outside this directory, under the user's Codex home. The current reconnaissance identified that location as:

`C:\Users\henns\.codex`

This directory does not replace the installed global configuration. It documents the intent, evidence, decisions, and planned changes so that work on the global configuration is repeatable and reviewable.

## Files

- `01-goals-and-principles.md` - intended global behaviour and efficiency principles.
- `02-current-global-design.md` - current installed routing design as reconstructed from the global configuration.
- `03-viv-real-world-evidence.md` - evidence from the VIV implementation run that exposed routing inefficiency.
- `04-terra-reconnaissance.md` - read-only reconnaissance of the installed global configuration.
- `05-assessment-scope.md` - questions the next assessment must answer before any changes are made.
- `PROMPT-sol-high-assessment.md` - ready-to-use prompt for the next Codex assessment pass.

## Important rule

Do not modify the global Codex setup merely because a possible improvement has been identified.

The required sequence is:

1. inspect;
2. assess;
3. agree the smallest coherent correction;
4. implement;
5. verify;
6. record the final routing policy and evidence.

The policy must remain global and reusable across unrelated repositories.
