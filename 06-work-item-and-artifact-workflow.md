# Work-item and Artifact Workflow

This document records the established workflow for persistent AI work. It is separate from global model/capability routing and does not establish a universal repository-governance policy.

## Model and capability routing

The global routing policy answers which model and reasoning effort are the least expensive options that can reliably perform a task, and when capability should escalate or de-escalate. That policy applies across repositories. Repository ownership and artifact policy do not change the routing objective. See [`01-goals-and-principles.md`](01-goals-and-principles.md) and [`02-current-global-design.md`](02-current-global-design.md).

## Work-item protocol

For each persistent objective, one `work-item.md` is the compact, resumable snapshot of current state. It summarizes where the work stands and what remains relevant; it is not a transcript or a replay of the full history. Use an existing repository template when present. No universal field schema is established here.

Related materials keep their established roles:

- Task files carry scope and execution instructions.
- Prompts launch or support work.
- Reports preserve completed evidence.
- Existing repository documentation and governance remain normative authority.

Use the work item to reference authoritative material rather than repeat it, load large context from its source rather than embed it, and summarize current state rather than replay history. Adopting this protocol does not itself authorize renaming, moving, or migrating historical material.

## Artifact policies

Artifact policy controls where AI workflow artifacts live and how they are treated by source control. It is independent of model routing and repository-operation authority.

| Policy | Established meaning | Repository-specific details |
|---|---|---|
| `external` | Workflow artifacts live outside the target repository. | Select and record the actual root and any source-control rules that apply to that location. |
| `local-untracked` | Workflow artifacts live locally and are not intended for source control. | Inspect existing ignore and untracked-file conventions. Propose needed handling; do not silently impose ignore rules or cleanup behavior. |
| `local-tracked` | Workflow artifacts live locally and are intended to be eligible for source control under repository policy. | Record which artifacts may be tracked. The policy alone does not authorize staging, committing, merging, pushing, or changing application code. |

The terms do not establish a universal artifact root, directory map, ignore mechanism, cleanup rule, or approval default. Those details belong to the repository or workspace using the policy. If a detail is absent, keep it unknown until the governing instructions or user resolve it.

## Repository governance and autonomy

Repository or workspace instructions separately define the authority to edit files, run tests or builds, stage or commit changes, merge, push, deploy, perform destructive operations, or take external actions. They also identify required human review gates. Artifact tracking permission is not permission to make application-code changes or publish them.

Read and preserve existing governance as normative authority. If authority is absent, conflicting, or unclear, do not infer it from an artifact-policy choice. Continue with read-only inspection and seek clarification before actions that require authorization. Task-specific instructions may authorize a bounded action without establishing a permanent repository rule.

## Onboarding and repository-specific structure

The canonical [`workspace-onboarding` skill](codex/skills/workspace-onboarding/SKILL.md) provides `inspect`, `new`, and `adopt` modes. `inspect` is read-only. `new` proposes a minimal workflow for a repository without one. `adopt` begins by inspecting existing organization, preserves its conventions, and requires approval before material structural or policy changes.

Artifact roots, category names, directory mappings, source-control treatment, and repository-operation permissions remain specific to each repository or workspace. A mapping may be absent or not applicable. No project's layout is a global template. Adoption does not silently create generic AI directories or documentation, or migrate and reorganize historical artifacts merely to conform to the workflow.

The VIV workspace's historical `external` policy and Dark Tower's reported `local-tracked` adoption are examples of repository-specific choices, not universal layouts. Their details are recorded only as examples in the historical evidence described by the reconstruction audit; they do not define defaults for other repositories.

## Provenance

The work-item and artifact-policy design is recorded from historical conversation evidence and surviving workspace-policy records. The active `workspace-onboarding` skill is a behavioral reconstruction, not a verbatim recovery of the original skill file. Unsupported historical mechanics and defaults remain unspecified.
