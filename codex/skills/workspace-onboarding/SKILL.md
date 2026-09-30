---
name: workspace-onboarding
description: Inspect, set up, or adopt a repository-specific AI work-item and artifact workflow while preserving existing repository governance. Use when onboarding a workspace or repository to a persistent Codex work-item workflow.
metadata:
  short-description: Onboard repository work-item workflows
---

<!-- Provenance: Behaviorally reconstructed from historical conversation evidence and surviving workspace-policy records; this is not a verbatim restoration. Historical details not supported by that evidence remain intentionally unspecified. -->

# Workspace Onboarding

Help establish or understand a repository-specific workflow for persistent AI work. Keep three concerns separate:

1. **Model and capability routing** is governed by the global Codex routing policy. Do not set or change model, reasoning effort, delegation, or routing rules here.
2. **Work-item workflow** describes how an objective is represented and resumed.
3. **Artifact policy and repository governance** describe where workflow artifacts live, how they are treated by source control, and what repository operations Codex may perform.

Choose one mode based on the request: `inspect`, `new`, or `adopt`. If the requested mode or target workspace is unclear and would change the work, ask before proceeding.

## Shared rules

- Read applicable workspace and repository instructions before recommending changes. Existing repository documentation and governance remain normative authority.
- Keep category names, directory maps, artifact roots, and repository permissions specific to the repository. Do not impose another project's layout as a template.
- Do not infer permission to edit, test, stage, commit, merge, push, deploy, or perform destructive or externally consequential operations from an artifact policy.
- Treat repository-specific instructions as the place to discover and record authority for those operations. If authority is absent, conflicting, or unclear, proceed with read-only discovery and request clarification or approval before consequential operations.
- Preserve existing user work and repository state. Do not migrate or reorganize historical artifacts merely to make a workflow uniform.
- Keep evidence clear: distinguish what current instructions explicitly say, what repository practice suggests, and what remains unknown. Present recommendations as proposals when approval is needed.

## Work-item protocol

For each persistent objective, use one canonical `work-item.md` as a compact, resumable snapshot of its current state. Follow an existing repository template or convention when present. The file should summarize the present state rather than replay history; do not invent a fixed schema when the repository has none.

Preserve the established roles of related materials:

- Existing task files remain scope and execution instructions.
- Prompts remain launcher or support artifacts.
- Reports remain evidence of completed work.
- Existing documentation and governance remain normative authority.

Reference existing material instead of repeating it. Load large context from its authoritative source instead of embedding it. Summarize current state instead of replaying the history. Do not rename, move, or migrate existing task, prompt, report, or documentation files just to adopt `work-item.md`.

## Artifact policies

Select and record an artifact policy independently of repository-operation authority. Confirm the repository-specific artifact root. Confirm and report a category mapping only when one already exists or is needed; `none` or `not applicable` are valid outcomes. Do not assume either from the policy name.

- **`external`**: Workflow artifacts live outside the target repository. Confirm the actual external root and any separate source-control rules that apply there.
- **`local-untracked`**: Workflow artifacts live locally within the repository or workspace and are not intended for source control. Inspect existing ignore and untracked-file conventions. Propose any needed repository changes; do not silently add ignore rules, attributes, or cleanup behavior.
- **`local-tracked`**: Workflow artifacts live locally within the repository or workspace and may be included in source control according to repository instructions. This permits considering those artifacts for tracking; it does not authorize staging, committing, pushing, or changing application code.

If repository instructions distinguish artifact tracking from code changes, preserve that distinction. If the source-control meaning or artifact root is unclear, record the uncertainty and ask rather than assuming.

## Mode: inspect

Inspection is read-only. Examine applicable instructions, repository documentation, existing task and report conventions, relevant directory structure, and current repository state as needed. Do not create, edit, move, ignore, stage, or delete files.

Report:

- existing work-item and artifact practices;
- explicitly stated artifact policy, root, and category mapping, if any;
- existing governance for edits, tests/builds, Git operations, deployment, destructive actions, and human review;
- how tasks, prompts, reports, and normative documentation are used;
- conflicts, gaps, and unknowns, separating evidence from inference.

Do not characterize an undocumented practice as an established rule.

## Mode: new

Use for a repository or workspace that genuinely lacks an established workflow. First inspect enough to avoid conflicting with existing documentation or conventions. Propose the minimum repository-specific policy needed for the requested work: artifact policy and root, category mapping only if needed, work-item use, and any repository-operation authority that must be explicit.

Do not create generic AI directories, workflow documentation, or templates solely because this skill is being used. Do not choose a policy or grant repository authority on the user's behalf when material details are unclear. Make changes only when the user authorized them and any required approval has been obtained.

## Mode: adopt

Use when a repository already has tasks, prompts, reports, documentation, governance, or other established organization.

1. Inspect the existing organization and applicable governance before proposing a change.
2. Map the requested work-item workflow onto existing categories and locations where practical. Preserve established names and structures.
3. Identify the minimum policy that is missing, keeping artifact placement/tracking separate from repository-operation authority.
4. Present material structural or policy changes for approval before applying them. Do not silently create generic AI directories or documentation.
5. Do not migrate, rename, move, or reorganize historical artifacts merely to conform to the workflow. Preserve existing tasks, prompts, reports, and normative documentation in their current roles.
6. Apply only the approved, in-scope changes. An adoption request does not authorize unrelated repository edits or Git publication.

When adoption is complete, report the selected artifact policy and root, the repository-specific category mapping if one exists or is needed (otherwise `none` or `not applicable`), work-item convention, artifact source-control treatment, independently established repository-operation permissions, approvals still needed, and any remaining uncertainty.
