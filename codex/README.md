# Authored Codex Assets

This directory is the canonical, source-controlled home for reusable Codex assets authored and maintained by the AI-Optimization project. The active/runtime installation remains under `C:\Users\henns\.codex`.

## Current inventory

| Canonical source | Active installation | Status |
|---|---|---|
| `skills/workspace-onboarding/SKILL.md` | `C:\Users\henns\.codex\skills\workspace-onboarding\SKILL.md` | Behaviorally reconstructed from historical conversation evidence and surviving workspace-policy records; not a verbatim restoration. |

Add an asset to this inventory when its canonical source is added here. Keep model-routing policy in its existing design documents and global routing files; the onboarding skill does not replace or modify routing policy.

## Install or update

The skill is a single-file asset and is installed by copying its canonical `SKILL.md` to the active path shown above. No synchronization script is needed.

Before copying, compare the active file with the canonical source:

- If the active file is absent, create the skill directory and copy the source file.
- If the files are identical, no update is needed.
- If the active file differs, review and reconcile the differences first. Never silently overwrite a materially different active file.

Keep the canonical source under version control. Do not copy runtime installation state back into this directory as if it were authored source.

## Excluded from this source tree

This inventory covers authored, reusable Codex assets only. It does not include authentication data or secrets; runtime databases, sessions, logs, caches, generated state, machine-specific paths or configuration; standalone runtime packages; or Codex/OpenAI system, bundled, vendor, and plugin-provided assets. The repository's `.gitignore` excludes the machine-local `.codex/` and session data.
