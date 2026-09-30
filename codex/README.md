# Authored Codex Assets

This directory is the canonical, source-controlled home for reusable Codex assets authored and maintained by the AI-Optimization project. The active/runtime installation remains under `C:\Users\henns\.codex`.

## Current inventory

| Canonical source | Active installation | Status |
|---|---|---|
| `skills/workspace-onboarding/SKILL.md` | `C:\Users\henns\.codex\skills\workspace-onboarding\SKILL.md` | Behaviorally reconstructed from historical conversation evidence and surviving workspace-policy records; not a verbatim restoration. |
| `global-instructions/AGENTS.md` | `C:\Users\henns\.codex\AGENTS.md` | Authored global operating instructions. |
| `global-instructions/MULTI_AGENT.md` | `C:\Users\henns\.codex\MULTI_AGENT.md` | Authored global routing and delegation policy; the child-routing matrix is maintained here. |
| `global-instructions/AI_WORKFLOW.md` | `C:\Users\henns\.codex\AI_WORKFLOW.md` | Authored cross-repository workflow guidance, including reconstructed reusable workflow guidance. |

The files under `global-instructions/` are canonical source copies. The matching files under the active Codex home are installed/runtime copies. Keep model-routing policy in its existing design documents and `MULTI_AGENT.md`; the onboarding skill does not replace or modify routing policy. Add an asset to this inventory when its canonical source is added here.

## Install or update

These authored files are installed by copying their canonical source to the matching active path shown above. No synchronization script is needed.

Before installing or updating any file, compare the active file with its canonical source:

- If the active file is absent, create only its required parent directory and copy the source file.
- If the files are identical, no update is needed.
- If the active file differs, review and reconcile the differences first. Never silently overwrite a materially different active file. After an approved update, verify that the active and canonical copies match.

Keep the canonical source under version control. Do not copy runtime installation state back into this directory as if it were authored source.

The canonical global instruction copies are the reviewed authored text, not a backup of the Codex home. Install only the listed files. Do not copy `config.toml`, authentication or secrets, databases, sessions, logs, caches, runtime packages, generated state, machine-specific configuration, or Codex-owned bundled, system, vendor, or plugin assets.

## Excluded from this source tree

This inventory covers authored, reusable Codex assets only. It does not include authentication data or secrets; runtime databases, sessions, logs, caches, generated state, machine-specific paths or configuration; standalone runtime packages; or Codex/OpenAI system, bundled, vendor, and plugin-provided assets. The repository's `.gitignore` excludes the machine-local `.codex/` and session data.
