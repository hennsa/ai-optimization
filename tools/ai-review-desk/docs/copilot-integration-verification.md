# Copilot integration slice — blocked, incomplete

2026-10-02, Windows, .NET 10.0.401, installed Copilot CLI 1.0.91. Changes are working-tree implementation only. No release acceptance, commit or push is claimed. The original pass is retained below; the continuation section supersedes its counts and current-state statements.

## Blocking contract evidence

CLI 1.0.91's installed `help config` documents executable global hook and status-line settings in `config.json`. The current [GitHub configuration reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-config-dir-reference) identifies that file as managed authentication/plugin state. Allowing it as opaque state cannot prove that executable contributions are absent.

An independent verifier ran six isolated CLI probes using fresh profiles, empty run directories and disposable caches. The primary repeated the decisive positive control. Offline BYOK pointed only to an unreachable localhost endpoint; no real credentials or repository source were supplied.

| Probe | Observed result |
| --- | --- |
| Fresh profile, no authentication | Exit 1: no authentication information; no hook marker. |
| Unversioned inline hook, `disableAllHooks=true` | No marker; reached model startup. |
| Unversioned inline hook, setting absent | No marker; reached model startup. |
| Profile hook file positive control | Harmless marker command executed under constrained flags. |
| `{version:1,hooks:{sessionStart:[...]}}` in `config.json` | Harmless inline marker executed under constrained flags. Primary reproduced independently, including the explicit deny list and temp restriction. |
| Same versioned inline hook plus `disableAllHooks=true` | Marker suppressed in this case; reached model startup. This does not repair the Phase 2 profile-hook-file bypass or replace inspection. |

Early events showed `github-mcp-server:disabled`, `githubiq:disabled`, and `session.tools_updated.data` containing model only. BYOK calls were stopped after 8–25 seconds. **None produced a completed tool checkpoint or terminal result.** These probes prove startup risk, not completed review compatibility. All seven synthetic profile/run/cache trees and marker files were removed; the dedicated authenticated profile was never changed and credential contents were never opened.

The dedicated profile has `config.json`. The app rejects it based on its filename, without opening it. Do not delete or rewrite authentication state to bypass this. Even an empty profile is blocked by the explicit unverified integration contract. Enabling execution requires a credential-preserving resolution and real authenticated production validation.

Installed help also confirms no top-level `auth`, `status`, or `logout` command and rejects `login --status`. `/logout` is interactive only. Account status and identity remain unknown. Sign out and Switch account are disabled. `login --web-flow` is wired through the official CLI, subject to preflight, but was not exercised against the authenticated profile or a browser.

## Working-tree implementation

- Workspace default/minimum widths and draggable splitter; main content fills remaining width. Every project exposes Overview and Reviews. Global Profiles/Settings remain available; the global Reviews destination is removed.
- Add Project opens the native picker outside the disabling busy gate. Hands-on verification found the additional first-click cause: activation refresh used that same disabling gate and swallowed the activation click. Refresh now runs without disabling controls, prevents overlap and only applies to the same still-selected registration. No timing or focus workaround was added.
- Invalid folders get a contextual Cancel/Choose another dialog; failed addition preserves the existing project and screen. Tracked changes and untracked files are separate.
- Visible read-only Shared Reviewer Policy and actual built-in profile instructions. Additive defaults migrate legacy single-profile IDs, normalize duplicates/case and preserve catalogue order. Security + Database / EF can omit Standard. Run metadata captures profile/shared-policy versions.
- One composer builds the read-only final prompt and the execution prompt: shared policy once, each chosen lens once, snapshot, scope, bounded diff/context and structured schema last. No profile changes technical permissions.
- Local working/merge-base branch/selected-changed-path scopes. Context excludes obvious sensitive filenames and bounds/truncates content with notes; this filename screen is not a general secret detector. Copilot still has reviewed-repository read reach. Prompt-injected findings always require independent assessment.
- Repository fingerprints include HEAD/symbolic HEAD, refs, status, index and tracked/nonignored untracked bytes; links are fingerprinted by target without following them. Conflicts, submodules and unsafe traversal fail closed. Input preparation checks coherence; changed preview becomes Stale before launch. Post-run fingerprint/stale code is present but no authenticated run tested it.
- Compact JSON history retains identity, time, scope, profiles/versions, status, CLI metadata, hashes, validated structured result or generic failure reason. Raw protocol and diffs are not saved. Non-completed findings are discarded. Completed-only result/ChatGPT/Codex copies require independent verification and never authorize implementation.

## Implemented technical controls and limits

Version policy accepts Windows CLI **1.0.91 only**, with execution additionally blocked while its integration contract is unverified. Discovery resolves native executable or Node/npm loader from absolute PATH entries; wrappers and relative PATH components are excluded. CLI runs through `ProcessStartInfo.ArgumentList`, redirected stdin and a cleared/allowlisted environment. Credential, provider, hook and Node override environment variables are not inherited.

Preflight rejects unknown top-level profile sources, links, nonempty hook/MCP/plugin/extension/agent/skill contribution directories, MCP/LSP/provider/permission configuration, and any opaque `config.json`. Only the exact `disableAllHooks=true` settings object is accepted. Known inert CLI state names are allowed without reading them. Runs must start empty with no linked ancestors or known ancestor executable configuration. Repository LSP/extensions/plugins and local/repository settings sources are rejected. This is conservative scaffolding, **not proof that the complete executable surface, including managed policy, has been certified**.

Fixed review arguments contain separate `--add-dir`, mandatory `--disallow-temp-dir`, available/allowed tools `view,grep,glob`, the Phase 1 explicit deny list, `--disable-builtin-mcps`, `--no-custom-instructions`, `--no-remote`, `--no-remote-export`, `--no-ask-user`, `--no-auto-update`, JSON streaming, disabled eager PowerShell resolution/experimental features and transient logging. Run cwd is app-owned and separate from the reviewed repository. No arbitrary CLI flag editor exists. The actual reviewer process is currently never launched.

Completed-stream scaffolding rejects malformed/duplicate JSON, nonzero exit, cancellation, missing terminal/manifest/MCP evidence, nonexact tool manifests, unexpected tools/MCP/startup events, contradictory terminal failure signals, and malformed structured findings. It permits a valid empty finding list and manifest arrival after first tool execution. Tests use synthetic frames; **real completed 1.0.91 terminal/checkpoint shapes still require verification**. Cancellation kills the entire process tree and discards partial findings; a controlled parent/child test passes, but live Copilot cancellation remains unverified in this product path.

## Verification

Final Release verification: `dotnet build tools/ai-review-desk/AIReviewDesk.slnx -c Release --no-restore -m:1` succeeded with 0 errors and 1 NU1900 warning (NuGet vulnerability-feed access unavailable). The subsequent `dotnet test tools/ai-review-desk/AIReviewDesk.slnx -c Release --no-build --no-restore -m:1` passed **83 tests, 0 failed, 0 skipped**, in 23 seconds. The historical 18-test baseline is separate from this working-tree evidence. Final whitespace, source/artifact and credential-pattern checks are recorded with delivery status.

Manual checks performed: corrected Release app starts; restored/maximized layouts fill available width; workspace splitter drags; project Overview/Reviews navigation selects the owning project; Add Project opens on one click; non-Git dialog shows selected path; Choose another reopens picker; Cancel preserves prior overview; shared policy and profile instructions are visible; Security + Database / EF selection excludes Standard; prompt preview contains the production composition; changed prepared input returns Stale with all handoffs disabled; a fresh Start review returns Failed at opaque-config preflight with all handoffs disabled; compact failed/stale history appears; Settings shows CLI 1.0.91, unknown identity/authentication and disabled sign-out/switch controls.

The initial manual launch exposed a read-only TextBox binding startup exception; fixed with OneWay bindings. Prompt preview contrast was corrected after hands-on inspection, then visually reverified in the final Release build with readable policy/profile/snapshot/scope/diff text and visible Copy prompt/Close controls. No real successful/zero-findings/known-defect review, live Copilot cancellation, during-run external mutation, actual validated findings cards/copy actions or browser OAuth/sign-out was verified. These remain required acceptance work, together with automated App navigation/Add workflow coverage and managed configuration certification.

## Delegation Report

Primary model/effort: unavailable (not authoritatively exposed). These are exact routed child settings, not claims about actual child runtime metadata.

| Task | Routed model / effort | Route rationale | Primary integration and verification |
| --- | --- | --- | --- |
| Core models, profiles, composer, handoffs and tests | gpt-6-luna / high | Substantial bounded implementation with settled product requirements | Reviewed composition; requested six distinct sections and stronger handoffs; integrated migration/version metadata; ran final suite. |
| Git scope/context/fingerprint implementation and tests | gpt-6-luna / high | Bounded local Git/data-integrity work | Reviewed paths, link handling, diff bounds and fingerprints; integrated service/UI APIs; ran disposable-Git tests in final suite and stale-preview manual check. |
| WPF shell, profiles, review/account UX | gpt-6-luna / high | Bounded App implementation | Ran actual Release app; found/fixed binding crash, remaining activation first-click issue and preview contrast; inspected navigation, splitter, profiles, preview and blocked/stale UX. |
| Independent startup/authentication security investigation and targeted review | gpt-6-sol / high | Authentication and executable startup outside model authority require stronger independent scrutiny | Independently reproduced inline-hook execution; enforced config block; fixed cache/run, discovery, LSP and validator issues. Completed protocol remains explicitly unverified. |

## Source-control delivery

This section records the original pass. See the continuation below for current verification and delivery.

No new commit; no push, because required secure vertical-slice acceptance did not pass. Baseline local HEAD is `3cf03e8f4598026b3c6dc9b58da9844bb7c61175`. Remote `refs/heads/main` was queried and equals that SHA. Working tree intentionally contains this incomplete task's 14 modified and 13 new source, test and documentation files; it is not clean. All changes are within `tools/ai-review-desk`. Tracked `git diff --check` and separate new-file whitespace checks passed. Source review and credential-pattern scans found no introduced credential values or unrelated/generated artifacts. Application Runs is empty; no Copilot or Review Desk process remains; the dedicated profile's file inventory is unchanged. No generated outputs or credentials are included. Do not treat the working tree as an accepted release.

## Security-contract continuation — still blocked

### Version and evidence boundary

The installed native executable reports **1.0.91**, commit `216810c5`, SHA-256 `9DB6BFF0CF719556B0C8BF74488C2FC58ADF1421EA2DE240417D60EF7B7F529C`. Investigation used local help, isolated synthetic profiles, and shipped `package/app.js`/native runtime extracted in memory from its compressed executable archive. A raw executable string scan alone missed supported controls; this was corrected using the actual archived application. Source inspection explains controls but does not certify every reachable startup path.

Current GitHub [configuration documentation](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-config-dir-reference) and [hooks documentation](https://docs.github.com/en/copilot/reference/hooks-reference) were used as leads. The observations below apply to installed 1.0.91, not an assumed newer CLI contract.

### Authentication and the unresolved mixed file

- Local login help and shipped native runtime establish a Windows Credential Manager backend, including Windows keyring support and `CredReadW`/`CredWriteW`/`CredDeleteW`/`CredEnumerateW` bindings. **Carlo's actual OAuth storage mode and credential target remain unproven.** No credential-reading API, credential enumeration, real `config.json` parsing, browser sign-in, logout or account switching was performed. Backend availability does not prove this account used it.
- `login --help` explicitly allows plaintext `config.json` fallback when the system store is unavailable/errors. Source contains a plaintext-consent branch that refuses consent when stdin or stdout is not a TTY. This was source-verified, not a live OAuth/keychain failure experiment. Production rejects `config.json` before opening it; it cannot accept plaintext fallback or certify an opaque mixed file.
- Fresh-process native tests with fake nonsecret state established a supported schema: `loggedInUsers` and `lastLoggedInUser` select host/login; `authTokens[host + ':' + login]` holds an object with a `token` field. Bare string token-map values are not that schema. Metadata alone returned identity metadata but no token with keychain disabled. Fresh processes were required because clearing global configuration cache did not clear cached authentication handles. These tests did not read or fabricate real credentials.
- A fresh empty isolated home, no token variables and constrained production-like flags exited 1 with `No authentication information found`, before any completed model/terminal stream. It did not inherit the existing sign-in. No authenticated state or tokens were copied into it.
- `config.json` can combine fallback credentials, account/keychain-selection metadata, plugin/runtime state and executable inline hooks. The original constrained inline-hook positive control remains decisive. No supported mechanism was established to obtain the needed authenticated metadata in a clean home or certify the mixed file without possible secret access. Exact keychain target-selection and browser-authenticated clean-home behavior remain unresolved. **Authentication/executable-configuration separation has not been established.**

### Authentication precedence and environment

1.0.91 help/source supports `COPILOT_GITHUB_TOKEN`, `GH_TOKEN`, `GITHUB_TOKEN` and GitHub CLI auto-login fallback. An isolated fake `gh.exe` returned failure and logged argument names only; no real GitHub CLI account/token was accessed. With fresh homes, a minimal benign environment including `PATHEXT`, disabled keychain and `COPILOT_OFFLINE=1`, the CLI attempted `gh auth token --hostname github.com` seven times by default and zero times with `--no-auto-login`; both exited unauthenticated. The primary independently verified the shipped `autoLogin === false` to `disableAutoLogin` binding and integrated/tested the fixed flag. Its additional controls lacked a valid positive fallback path and are not counted as independent behavioral reproduction.

Production now fixes `--no-auto-login`. It clears the child environment and queries only `SystemRoot`, `WINDIR`, `PATH`, `TEMP`, `TMP`, `USERPROFILE`, `APPDATA`, `LOCALAPPDATA`, `USERDOMAIN`, `USERNAME`, `HOMEDRIVE`, `HOMEPATH`, `COMSPEC`, `ProgramData`. It then sets app-owned `COPILOT_HOME`, `COPILOT_CACHE_HOME`, `COPILOT_AUTO_UPDATE=false`, `USE_TGREP=false`, `GITHUB_COPILOT_PROMPT_MODE_REPO_HOOKS=false`, `GITHUB_COPILOT_PROMPT_MODE_EXTENSIONS=false`. Authentication variables are never queried/copied. `COPILOT_ALLOW_ALL`, provider/BYOK/keychain overrides, Node injection, debug/custom execution, proxy and other parent authority controls are absent. This environment has not completed an authenticated production review.

### Repository hook/extension controls

The shipped prompt-mode hook loader permits repository hooks when `COPILOT_ALLOW_ALL` is exactly `true`, `GITHUB_COPILOT_PROMPT_MODE_REPO_HOOKS` is exactly `true`, or cwd is trusted in configuration. Project extensions require `GITHUB_COPILOT_PROMPT_MODE_EXTENSIONS` exactly `true`. User/plugin/session extension categories are not all governed by that project switch. Production fixes both switches false, omits `COPILOT_ALLOW_ALL`, rejects opaque/trusted-folder configuration and uses empty app-owned cwd.

Fourteen isolated startup probes used fresh cleared environments, keychain-disabled homes, unreachable localhost BYOK, fake credentials, harmless markers and disposable repositories. Negatives reached early `session.tools_updated`; positives were stopped. Their invocation included `--available-tools view,grep,glob` plus `--allow-all-tools`, and extensions used `--experimental`. **These were discovery probes, not the exact production deny-list invocation or completed-review acceptance.**

| Source / discovery | Switch unset or false | Switch true |
| --- | --- | --- |
| User inline v1 hook in `config.json` | Executes; migrates hooks into settings | Not needed |
| `.github/hooks` in cwd | No marker | Executes |
| Same repository only via `--add-dir`, empty cwd | No marker | No marker; true case reports zero repository hooks |
| Inline `.github/copilot/settings.json` hook in cwd | No marker | Executes |
| `.github/extensions/.../extension.mjs` in cwd, experimental enabled | No marker | Executes before tool checkpoint |
| Same extension only via `--add-dir`, empty cwd | Not repeated | No marker |

This establishes the tested cwd/add-dir distinction and existence of both controls in 1.0.91. It does not certify exhaustive later prompt-driven discovery, trusted folders, plugins, agents/skills or remote managed configuration. Local help documents `.github/skills`/`.github/agents` through `--add-dir`; restrictions and real acceptance evidence remain necessary. Custom MCP out-of-band startup remains established by Phase 2; no new live MCP positive experiment was claimed.

### Configuration classification and machine policy

| Relevant source | Current production treatment |
| --- | --- |
| Profile `config.json` | Opaque/unsafe: reject by filename, never open. Covers auth/inline-hook/status-line/provider/permission/trust/plugin ambiguity. |
| Profile `settings.json` | Accept only exactly `{ "disableAllHooks": true }`, with size/shape checks. Defense in depth, not universal hook-disable proof. |
| Profile `hooks`, `extensions`, `installed-plugins`, `plugin-data`, `servers`, `agents`, `skills`, `instructions` | Require empty contribution trees; reject links. Plugin-provided hook/MCP/extension/LSP state cannot be blindly trusted. |
| Profile MCP/LSP/provider/BYOK/permissions and unknown top-level config | Reject unknown sources. Local help describes custom MCP, process-launching LSP, provider API-key commands and saved permissions. |
| Known CLI lock/session/history/log state names | Never open contents; provisionally state, not an exhaustive certified input boundary. Global gate remains closed. |
| Run cwd/ancestors | Empty app-owned cwd; reject links and known hook/Copilot/Claude/MCP/LSP/extension/plugin ancestor sources. |
| Reviewed repository | Never cwd. Fixed prompt switches; additionally reject `.github/lsp.json`, `.github/extensions`, `.github/plugins`, `.github/copilot/settings[.local].json`, `.claude/settings[.local].json`. Hooks use tested prompt gate; arbitrary add-dir MCP/behavioral contributions still require full acceptance. |
| Built-in MCP/instructions/remote/export/ask-user/update/model tools | Fixed disabling/restriction flags; separate preflight is required for out-of-band startup. |
| Windows machine policy | Existence-only checks; contributions or ambiguous/inaccessible sources block. Never read policy contents/registry values. |
| Remote/server-managed settings | Shipped ingestion exists; full execution impact/disable contract unverified. Global gate covers this; `--no-remote` is not assumed to disable it. |

Machine checks cover `%ProgramData%\GitHub\Copilot\policy.d` (absent/empty only), `%ProgramFiles%\GitHubCopilot\managed-settings.json`, `%ProgramFiles(x86)%\GitHubCopilot\managed-settings.json`, and both registry views of `HKLM\SOFTWARE\Policies\GitHub\Copilot` **and** `HKLM\SOFTWARE\Policies\GitHubCopilot`. All were absent in host read-only probes. Shipped code contains policy discovery, directory, managed schema and ingestion. No live administrator policy injection/non-disableability experiment was performed; ordinary hook-disable settings are not trusted to suppress policy. Reparse/unreadable/ambiguous paths and registry errors fail closed. Absence evidence joins preflight fingerprinting; this is not an OS sandbox or continuous policy monitor.

### Account UX and execution gate

Policy remains **Windows CLI 1.0.91 only**; later versions need new evidence. Missing/version/configuration-blocked states are separate. Actual authentication/identity remain unknown without supported noninteractive CLI output; no signed-in identity is fabricated from storage. Official `login --web-flow` remains wired but Sign in is disabled when setup is blocked; no real browser flow was exercised. Sign out/Switch remain disabled; `/logout`/`/user` are interactive concepts, not an app pseudo-API. An official interactive management launcher is not implemented while startup security is unresolved.

`ReviewContractVerified` remains **false**. Start review is visibly disabled with an explanation; preparation/preview remain usable. Settings explains opaque-config blockage, without mislabeling a supported version. Navigation now scrolls to top on area changes so scrolled Settings cannot hide review scope controls.

Before enabling: establish secret-free dedicated auth/config separation and complete startup/policy/plugin/MCP/agent/skill/managed-setting coverage, then perform all real production-run checks. **No real successful/zero-findings/known-finding/multiple-profile review, live Copilot cancellation, during-run stale classification, completed real JSONL/tool/MCP validation or completed copy handoff was performed in this continuation.** Synthetic tests do not satisfy these criteria.

### Continuation verification and delegation

Final Release build (`--no-restore -m:1 /p:UseSharedCompilation=false`): **passed, 0 errors, 1 NU1900 warning** for unavailable NuGet vulnerability feed. Final Release suite (`--no-build --no-restore -m:1`): **97 passed, 0 failed, 0 skipped**, 22 seconds. New coverage checks benign-only environment lookups, fixed prompt switches/auto-login flag, account classification and policy absence/presence/unreadable/ambiguous failures without content reads. Existing useful launch/tool/temp/version/stream/zero-findings/composition/cancel/stale/handoff tests remain.

Manual continuation checks before final navigation fix: actual Release Settings showed 1.0.91, unavailable identity and clear setup/config blockage with disabled account actions. One-click Add Project opened native picker; a non-Git folder showed contextual dialog; Cancel preserved selection/screen. The scrolled-area defect was observed and fixed. Final post-fix checks are recorded with delivery below.

Primary model/effort: unavailable (not authoritatively exposed). Exact routed settings for this continuation:

| Task | Model / effort | Why selected | Primary verification/integration |
| --- | --- | --- | --- |
| Authentication initial route | gpt-6-sol / high | Independent security scrutiny | Capacity failure; no findings/edits used. |
| Authentication retry | gpt-6.1-sol / high | Same high-risk task after capacity failure | Independently inspected shipped keyring/token-selection/auto-login bindings; integrated flag. Fake-gh matrix is delegated behavioral evidence, not independently reproduced. |
| Repository/config discovery | gpt-6-luna / high | Bounded source/help/marker matrix | Corrected raw-scan absence inference with compressed app source; checked gates and probe/production flag distinction. No code imported. |
| Machine-policy guard/tests | gpt-6-luna / high | Bounded read-only fail-closed checks | Reviewed paths/errors/reparse/no-content handling, fixed platform annotations, full suite/build passed. Child targeted test stalled; not claimed passing. |

Synthetic homes, markers, caches, fake-gh helpers and runtime fixtures were cleaned after verifying exact bounded paths. No synthetic hook/MCP/plugin state was added to the real profile; no account action was taken. Delivery remains uncommitted/unpushed and the accumulated tree is preserved.

Final post-fix WPF checks: scrolled Settings → project Reviews restored scrollbar value 0 with the scope picker visible; Start review remained disabled and its explanation visible. Security + Database / EF were selected with Standard unchecked; the actual preview showed the shared policy, those two profiles in catalogue order, repository snapshot, scope and supplied context. Read-only profile details displayed Security's actual instructions. Restored and maximized layouts filled available width. The splitter's original-pass manual verification remains historical; its drag was not repeated in this continuation. Real findings/zero-findings/progress/cancellation/stale/completed handoff UX and browser/account-management execution remain unverified, because production execution stays blocked. The app was closed after verification.

Final delivery: **no implementation commit and no push**. Local `main` and freshly queried remote `refs/heads/main` both remain `3cf03e8f4598026b3c6dc9b58da9844bb7c61175`. Working tree is intentionally dirty: **14 modified + 15 new files**, all scoped to `tools/ai-review-desk`. Tracked and new-file whitespace checks passed. Credential-pattern/source checks found no introduced secret values; these scans are supporting hygiene, not a general secret detector. Runs is empty; synthetic fixture inventory checks found no remaining investigation homes/helpers. The real dedicated profile's file inventory is unchanged, its sensitive contents were never opened, and no synthetic executable configuration was added. This is useful partial implementation/evidence, not secure-review acceptance.
