# First dogfooding fixes

This slice starts from `9284dc23c00328029ac85eb79f08826c581fbd23` and preserves the secure review contract accepted in `7216f1246de10b301e232cd8949f513ff2313438`.

## Findings and implementation

- Project navigation now has display-name-only, 38-DIP project rows and independently scrolling projects below the pinned Add project, Profiles and Settings controls. Overview/Reviews are underlined tabs in the selected project's workspace. Clicking the current project from global navigation, or pressing Enter in the project list, returns to Overview. The splitter and responsive content column remain.
- Project defaults previously measured its entire stack, including nine profiles, inside a fixed-height, non-resizable window without a scroll container. Content could extend behind the action row. Its star-sized content row now contains a vertical ScrollViewer; Save/Cancel remain in the separate automatic-height row. The window is resizable, with a work-area height limit.
- Preview and execution use the same preparation core and `PromptComposer`. Preview permits empty Working Changes and Branch versus Base contexts and explicitly describes their empty state. Execution always prepares afresh, rejects empty scopes and missing selected paths/profiles as pre-run validation, and does not save Failed history for those requests. The runner also rejects inputs without execution eligibility.
- The apparent Git-discovery inconsistency was reproduced on `viv_vapp` read-only: Overview succeeded with 2 tracked changes and 2,710 untracked files. Context preparation constructed a Windows command line containing every changed/untracked path. `Process.Start` failed with native error **206**, which the old catch incorrectly translated to “Git was not found.” Full-scope diffs now omit per-path command-line arguments. Selected-path reviews preserve literal path filtering and reject oversized arguments with a safe scope-size explanation.
- `GitInspector` and `GitReviewContext` now share `GitRunner` and a process-wide absolute executable selection. The locator prefers normal Git for Windows installation locations, then GitForWindows registration and validated absolute PATH candidates. Candidates must match the installation layout and a native executable, and cannot traverse reparse ancestors or reside within a Git working tree. Relative entries, bare executable search, and repository-local substitutes are rejected. On this machine both paths use `C:\Program Files\Git\cmd\git.exe`.
- Overview also repeatedly split/scanned the complete status string once per changed path. Tracked count now comes from the already-parsed status totals, removing that quadratic work.

## Preparation performance and integrity

Read-only production probes on the original `viv_vapp` state measured inspection at about 2.0 seconds and one old fingerprint at **12.409 seconds**. Old preparation spent about 21.7 seconds before failing with the misleading Git-not-found error. A subsequent probe confirmed native error 206.

After the fixes, inspection took **1.317 seconds**, one fingerprint **4.538 seconds**, and complete preparation succeeded in **12.656 seconds**. These are observations on this machine, not latency guarantees. No Copilot review or write operation was run against the client repository.

Fingerprinting retains the same ordered evidence and content SHA-256 checks, but reads at most four files concurrently and combines their evidence in deterministic path order. Preparation takes before/after fingerprints instead of an additional redundant middle pass. The runner still verifies the prepared fingerprint immediately before execution and after review, discarding stale findings.

| State | Integrity evidence |
| --- | --- |
| Clean tracked files | Content hashes remain; Git stat caches, assume-unchanged/skip-worktree and restored timestamps cannot hide content edits. |
| Modified tracked files | Content hashes plus Git status detect further edits even when their changed-path set stays constant. |
| Staged files | Raw index hash, indexed object/mode/stage inspection and working content are retained. |
| Deleted files | Ordered path/deleted markers, status and index capture deletion/staging. |
| Renamed files | Old/new membership, content, status and index capture renames. |
| New untracked files | Non-ignored untracked membership and content hashes are retained. |
| Edited untracked files | Content hashes detect edits without requiring a status-category change. |
| HEAD, refs and index | Commit identity, symbolic HEAD, local refs and raw index hash remain. |
| Links/submodules | Link targets are fingerprinted without following file links; unsafe path traversal and submodules remain fail-closed. |

A status-only optimization was deliberately avoided: the regression test changes an assumed-unchanged tracked file while restoring its timestamp. Ignored-file behavior remains as in the accepted contract. The same-user transient-write/restore race remains the documented desktop trust assumption.

Preparation reports Fingerprinting repository, Inspecting repository, Building review context and Preparing prompt; the existing environment/launch/review/result stages remain. Git commands have a 30-second limit and total context preparation has a two-minute limit. Cancellation flows through subprocesses and bounded parallel hashing. Known failures have fixed, safe diagnostic messages for unavailable Git, Git launch/command failure, conflicts, sensitive files, oversized scopes, timeout and changes during preparation. Arbitrary process output is not stored as a preparation diagnostic.

## History and account state

New records use additive schema 3 with nullable tracked-change and untracked-file counts. Detail and handoffs show the separate counts. Schemas 1/2 continue to load, and their existing total is explicitly labeled legacy combined information; no split is inferred. New preparation records without a snapshot report unavailable count metadata.

After successful official login, Settings shows Sign-in completed and explains that the dedicated profile was updated and authentication will be verified when a review starts. Refresh/restart shows Saved Copilot account configured only for structurally understood saved-account objects. Empty account arrays/null metadata do not establish a configured account. Identity and live status remain unavailable on CLI 1.0.91. The scanner returns one additional structural boolean; it still never decodes account/credential string values, and its accepted/rejected configuration schemas and preflight evidence are unchanged. Successful login adds a fresh structural post-check. Sign out/switch remain disabled.

## Verification

- Full Release test run: **207 passed, 0 failed, 0 skipped** (185 before this slice). Release solution build succeeded with zero errors. The single NU1900 warning concerns the unavailable NuGet vulnerability feed; no package dependency changed.
- Automated coverage includes the shared composer for empty previews; no-history empty Start; selected scope isolation; 2 tracked changes plus 2,710 untracked paths exceeding the Windows command-line limit; deterministic fingerprints; content/index/ref/rename/deletion mutations; stat-hidden content; cancellation and preparation changes; executable safety/consistency; safe diagnostics; schema 1/2/3 compatibility; actual account presentation; project navigation and XAML scrolling/action structure.
- Actual Release WPF was inspected with synthetic registrations and Carlo's existing workspace. The synthetic workspace contained 36 registrations; scrolling reached Project 34 while global controls stayed visible and the right workspace did not move. All nine default profiles were reached at normal scaling and again after reducing the dialog to approximately 420 pixels high; Save/Cancel stayed accessible. Clean and changed prompts opened through the UI. Clean Start showed a validation explanation and left temporary history empty.
- The actual Release workspace showed compact `ai-optimization`, `DarkTower`, `viv_vapp` rows, underlined project tabs, and Saved Copilot account configured with unknown identity/live status. No disruptive login/sign-out was performed; completed-login presentation is verified with a controlled account state.
- A real production review used the existing dedicated profile against a synthetic changed fixture: Completed, one validated finding, tracked 1/untracked 0. All three handoffs were generated and compact history was saved to temporary acceptance storage.
- Final Release UI verification opened that completed history entry, inspected the finding and expanded captured details (tracked 1/untracked 0), and successfully copied the Codex handoff. The final compact list scrolled to Project 34 with pinned controls; dragging the splitter resized and reflowed the main content. Clicking the already-selected project from Settings returned to Overview.
- `viv_vapp` also passed actual Release WPF Overview and prompt-preview preparation with 2 tracked changes and 2,710 untracked files. Fingerprinting progress was visible during preparation and the final prompt opened successfully within the bounded preparation limit. The preview was closed without launching Copilot or saving a client review.
- The dedicated profile, token/provider override isolation, GitHub CLI fallback isolation, ephemeral cwd, `--add-dir`, `--disallow-temp-dir`, exact view/grep/glob tools, MCP/instruction/remote restrictions, executable-configuration preflight, exact protocol validation, cancellation and stale-result enforcement remain in effect. No security/authentication investigation was reopened.

Delegation: none.
