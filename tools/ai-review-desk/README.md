# AI Review Desk v0.1 — Product and Architecture Design

## Executable preview

The desktop includes project Overview/Reviews navigation, a draggable workspace splitter, separate tracked/untracked summaries, additive profile defaults, inspectable shared policy and profile instructions, and final prompt preview. The production Copilot runner now completes validated reviews, including zero findings, structured findings and independent handoffs. See [integration verification](docs/copilot-integration-verification.md) for actual acceptance evidence and limits.

The installed CLI 1.0.91 can execute inline hooks from `config.json`, which may also hold authentication state. Under the refined credential rule, a narrow structural scanner checks that file without decoding, copying, hashing or returning credential values. Unknown or executable configuration still blocks launch. Saved-account hydration from the dedicated profile is allowed; `--no-auto-login` is omitted because it prevents that allowed mechanism on this version. Token/provider environment overrides are excluded and GitHub CLI fallback is isolated. Account identity/status cannot be obtained through a supported noninteractive command in this version; sign-out and switching remain disabled.

Help/runtime and synthetic state establish Windows keyring support and plaintext fallback possibilities; the exact storage backend/target for Carlo's account remains unobserved. Live controls now establish usable authentication selected by the dedicated profile when ordinary login is enabled, with token overrides absent and real gh excluded. No credential API or account-changing test was used. Token fields are skipped in-place, and only fixed structural categories and presence booleans escape. Official browser Sign in is available when version/configuration checks pass; no new sign-in was performed for testing.

Production constructs a cleared environment with benign Windows runtime variables and a fixed System32-only PATH, never querying token variables or inheriting the parent PATH/PATHEXT. Executables are resolved by full path; preflight blocks GitHub CLI in applicable Windows/application/cwd search directories and blocks ambiguous access. Both supported prompt-mode repository hook/extension switches are explicitly false, and Windows policy/managed-setting paths and registry keys are checked without reading contents. All other tool, MCP, instruction, remote, update and temp restrictions remain enforced. Completed JSONL must supply the exact tool manifest, disabled MCP evidence, correlated tool completions and structured findings; repository changes, cancellation or partial/invalid streams prevent normal handoffs. Transport is explicitly UTF-8.

On Windows with the .NET 10 SDK and Git installed:

```powershell
dotnet build tools/ai-review-desk/AIReviewDesk.slnx -m:1
dotnet test tools/ai-review-desk/AIReviewDesk.slnx --no-build -m:1
dotnet run --project tools/ai-review-desk/src/AIReviewDesk.App --no-build
```

Use **Add project** to choose any folder within a Git working tree, confirm the detected root/defaults, and select projects from the sidebar. Invalid folders receive a contextual Cancel/Choose another dialog. **Project defaults** changes the display name, base ref and selected profiles. Each project exposes Overview and Reviews; Profiles and Settings remain global. **Remove project** removes only its registration. **Refresh** inspects local state without fetching; activation refresh does not disable the workspace. Git details expand to show staged/unstaged/untracked/conflict counts and commit identities. Remote-tracking refs are local observations and may be stale.

The application uses WPF UI 4.3.0 and CommunityToolkit.Mvvm 8.4.2 (MIT; verified compatible with .NET 10). Application state is stored in `%LOCALAPPDATA%\AIReviewDesk\app-state.json`, with a recovery backup; compact run records go under `Reviews`. The desktop is single-instance. Copilot profile inspection checks configuration names and narrow JSON structure, skipping sensitive values without decoding them. Source is divided into App (WPF), Core (models, prompt composition and handoffs), and Infrastructure (Git, Copilot boundary, transport and persistence), with behavior tests under `tests/AIReviewDesk.Tests`.

The following sections remain the broader v0.1 design; capabilities beyond this executable preview are planned work.

## Purpose and authority

AI Review Desk is a focused Windows desktop application for obtaining technically constrained, independent code reviews from GitHub Copilot CLI. Its workflow is:

> Select repository → select scope → select profile → run constrained review → inspect structured findings → hand findings to ChatGPT or Codex for assessment and verification.

ChatGPT remains the reasoning, planning, architecture, and assessment layer. Codex remains the primary engineering and implementation agent. GitHub Copilot Pro is used here as an independent reviewer and critic. Carlo remains the final decision maker. A Copilot result is evidence to assess; it never authorizes or becomes an implementation task automatically.

## Product principles

- **Ease of use is a v0.1 requirement.** The normal experience is a focused desktop product, not a configuration utility or exposed terminal.
- **Own the review context.** AI Review Desk inspects Git state and prepares the review context. Copilot should not receive general shell or Git authority.
- **Enforce review-only behavior technically.** A prompt alone is not a security boundary. Implementation waits on the CLI contract and adversarial proof.
- **Preserve repository integrity.** Capture relevant state before and after every run and mark results stale if the repository no longer matches.
- **Keep customer repositories clean.** Project registry, settings, run history, and diagnostics live outside them under `%LOCALAPPDATA%`.
- **Minimize retained source data.** Do not persist full diffs by default; raw streamed events are transient unless an explicit diagnostic need and retention policy are later accepted.
- **Keep the capability narrow.** This product is an independent reviewer, not another general coding agent or a replacement for Codex Desktop.

## v0.1 product shape

### Projects and repository state

Maintain an application-local project registry. The project list and selected project view should show repository name and path, current branch, configured or likely base, working-tree state, changed-file count, and a concise diff summary. Derive these values from Git when possible. Project settings may include display name, repository path, default base branch, and default review profile.

Add a project with a folder picker. Detect whether it is a Git repository, infer useful defaults, and allow easy switching. Use normal controls for settings; ordinary use must not require editing JSON or configuration files. Do not fetch remotes automatically. A project-configured base takes precedence over guesses.

### Review scopes

1. **Current working changes** — staged, unstaged, and untracked changes as explicitly represented in the review context.
2. **Current branch versus configured base** — compare the current branch with the configured base using a locally available base and merge-base; do not fetch.
3. **Selected changed paths** — optionally narrow either scope to chosen changed files/paths. The UI must explain when a path is outside the changed set or cannot be included.

Cross-repository execution is outside v0.1. Scope selection and the exact diff construction rules must be deterministic and recorded in run metadata.

### Primary flow and screens

The main project/review view should make the sequence clear: choose project, inspect repository state, choose scope and (when applicable) paths, choose profile, start review, inspect findings, then copy a handoff. Use progressive disclosure for detailed Git values and expandable diagnostics.

Expected areas:

- **Projects:** repository list, folder-picker add flow, state summary, project defaults.
- **Reviews/history:** active review progress, structured findings, prior run records, stale/changed-state indication.
- **Profiles:** built-in profile descriptions and focus areas; no custom editor in v0.1.
- **Settings:** General, Appearance, Copilot, Review defaults, Data/history, and About.

During execution, show friendly stages and meaningful progress (preparing context, starting reviewer, receiving review, validating repository state, parsing result). Do not show a terminal by default. Raw/activity output may be expanded for diagnostics, subject to the retention rules below. Present findings as structured cards/items, not only as a text blob.

CLI switches and security mechanics are product policy; do not expose them as individually configurable ordinary-user settings.

### Reviewer boundary and Git ownership

AI Review Desk owns deterministic inspection concepts: branch, HEAD, configured and resolved base, merge-base, status, staged/unstaged/untracked state, changed paths, diff statistics, review diff, and a repository fingerprint captured before and after the run.

The intended Copilot environment uses a dedicated persistent profile at `%LOCALAPPDATA%\AIReviewDesk\Copilot`, authenticated through Copilot CLI's official OAuth login flow. AI Review Desk does not manage or persist the credential value. Each review launches from an empty ephemeral app-owned working directory, supplies the reviewed repository separately through `--add-dir`, and uses `--disallow-temp-dir`. Phase 2 reproduced the effective `view`, `grep`, `glob` model tool manifest with CLI 1.0.91, but proved that profile hooks and custom MCP servers can execute outside that manifest. Before launch, the product must reject executable hook/MCP/plugin/extension configuration in its dedicated profile and run directory; `disableAllHooks=true` alone did not suppress a synthetic profile hook. AI Review Desk supplies the Git diff/context through stdin and disables remote/session export, custom instructions, built-in MCP, and auto-update. Completed JSONL and repository fingerprints must validate before findings are presented.

Phase 1 evidence is documented in the [contract proof report](docs/phase1-copilot-cli-contract-proof.md); the [Phase 2 independent verification](docs/phase2-copilot-cli-verification.md) defines the revised launch and required safeguards. The JSONL tool list is observed after model tool execution in tested read runs, so it validates a completed result rather than protecting startup. Do not weaken the boundary silently to make a review run; a contract failure blocks or invalidates that review.

Fingerprint equality is the minimum integrity condition: if the relevant repository state differs after execution, the result is marked changed/stale and cannot be presented as a clean review of the original snapshot. Where practical, record enough components to help distinguish an external user/Codex change from a reviewer-side mutation; attribution is secondary to detecting the change.

### Results, history, and retention

A parsed review result contains a summary, zero or more findings, and limitations. An empty finding list is valid. The shared reviewer policy must instruct Copilot to report only evidence-backed issues and explicitly discourage invented findings to fill space.

Each finding supports ID, severity, certainty (`confirmed`, `probable`, `possible`), category, title, file, line or range when available, evidence, impact, and recommendation. Parsing must tolerate a valid empty review and must surface malformed or incomplete output as an execution/parse limitation rather than inventing fields.

A run record should retain enough metadata to identify exactly what was reviewed: run ID and timestamps; app version; Copilot CLI version and model when available; profile and profile version; project and repository identity; branch and HEAD; configured/resolved base and merge-base; scope and selected paths; changed-file count; diff hash; before/after repository fingerprints; execution status; parsed result; limitations and errors. Do not persist full customer source diffs by default. Avoid making raw JSONL permanent history by default because it may contain source excerpts. A simple file-based store under `%LOCALAPPDATA%` is acceptable; use SQLite only if investigation establishes a concrete benefit.

### Result handoff modes

- **Copy result:** a clean independent review record with repository/snapshot metadata, status/limitations, summary, and findings.
- **Copy for ChatGPT:** the review plus: “Assess these independent review findings against the implementation context. Separate valid findings from false positives or findings requiring repository verification. Do not assume the reviewer is correct.”
- **Copy for Codex:** the review plus: “Independently verify each Copilot finding against the current repository and actual change. Do not implement a finding merely because Copilot reported it. Report each finding as confirmed, rejected, or unresolved with evidence. Do not modify anything unless the current task explicitly authorizes implementation.”

Handoff copies the result as text; it does not start another agent or modify repository files.

## Architecture direction

Target Windows, .NET 10, WPF, MVVM with `CommunityToolkit.Mvvm`, and likely `Wpf.Ui` for the Fluent-style control layer. Confirm current package support and licensing during implementation planning; do not add packages during this design task.

Keep the structure deliberately small:

```text
tools/ai-review-desk/
  AGENTS.md
  README.md
  work-item.md
  docs/
    copilot-cli-contract-proof.md
    review-profiles.md
    implementation-plan.md
  src/                       # only in later implementation phases
    AIReviewDesk.App/
    AIReviewDesk.Core/
    AIReviewDesk.Infrastructure/
  tests/                     # only in later implementation phases
```

- **App:** WPF/Wpf.Ui views, navigation, view models, and user interactions.
- **Core:** UI-independent project, repository snapshot, scope, profile, run, result, finding, and handoff concepts plus application orchestration contracts.
- **Infrastructure:** Git inspection, Copilot process execution and JSONL transport, persistence, and clipboard formatting.

Prefer installed `git.exe` over a Git library unless a concrete requirement justifies a library. Process execution must eventually use safe argument APIs rather than shell-built command strings, redirect input/output, support cancellation, and terminate the process tree. These are implementation requirements, not permission to expose arbitrary process execution to Copilot.

## Explicit v0.1 non-goals

- Copilot code modification or automatic fixes
- Arbitrary command execution or normal test/build execution by Copilot
- Copilot-initiated commit, push, merge, rebase, reset, or checkout
- GitHub PR management
- General-purpose Copilot chat, ACP integration, custom MCP integrations, or Copilot subagents/fleet
- Web research
- Custom profile editor
- Multi-repository review execution
- Automatic remote fetching
- Replacing Codex implementation or verification workflows

## Assumptions and unresolved questions

Phase 1 and Phase 2 observations apply to Copilot CLI 1.0.91 and the tested Windows host; they do not establish behavior for later CLI versions or the future product process. The three-tool model boundary is viable only with the Phase 2 profile/configuration preflight, temp-directory restriction, completed-run validation, and stale-result rule. Read reach remains a local-desktop trust assumption, not an OS sandbox guarantee. See [the Phase 2 report](docs/phase2-copilot-cli-verification.md). Keep future ideas distinct from v0.1 scope and do not turn undocumented behavior into a product guarantee.

## Related documents

- [Current work item](work-item.md)
- [Copilot CLI contract and proof plan](docs/copilot-cli-contract-proof.md)
- [Review profile design](docs/review-profiles.md)
- [Implementation plan](docs/implementation-plan.md)
