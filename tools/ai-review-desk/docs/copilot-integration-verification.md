# Copilot integration slice — blocked, incomplete

Windows, .NET 10.0.401, installed Copilot CLI 1.0.91. **Current result (2026-10-03): structural inspection resolved; real profile passes preflight; matched runtime comparisons isolate the required `--no-auto-login` as the authentication blocker in this saved-profile prompt path; execution gate retained.** Verified partial work is on `codex/ai-review-desk-secure-review`. The final runtime continuation supersedes earlier uncertainty about the cause. Earlier sections are historical evidence, not current delivery/release claims.

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

## Focused completion — structural inspection resolved, authentication blocked

### Checkpoint and refined credential rule

The initial 29-file accumulated diff was scoped entirely to this application. Before new implementation, 97 tests passed, Release build passed (one NU1900 warning for the unavailable vulnerability feed), whitespace and source/credential-pattern checks passed. The existing work was preserved and committed as **`93ad239`** on **`codex/ai-review-desk-secure-review`**, explicitly describing secure execution as blocked. Main remained at `3cf03e8f4598026b3c6dc9b58da9844bb7c61175`.

The user's refined requirement permits structural inspection while forbidding materialization, retention, copying, logging, persistence, display, return, comparison, transformation, transmission or exposure of OAuth values. The official CLI remains the credential consumer. This supersedes the earlier filename-only refusal; it does not permit general authentication deserialization or account extraction.

### Scanner and supported structure

`CopilotConfigScanner` opens a bounded, read-only memory-mapped file view and passes its borrowed bytes directly to `Utf8JsonReader`. The pointer/view/file are released before return; no managed file buffer, raw JSON object, token string, token copy or credential hash is created. Unsafe code is confined to borrowing the read-only OS view. It never writes authenticated configuration. This necessarily exposes transient input bytes to the JSON tokenizer; it never materializes their credential values into application strings/objects.

Only fixed **property names** are matched using `ValueTextEquals`, including escaped names. All string **values** remain undecoded. The understood `authTokens` map has dynamic keys that are skipped without decoding/comparing/copying; each entry must be exactly an object with a string `token`, which is skipped in-place. The API returns only two presence booleans. Errors return a fixed enum category and fixed message with no input/inner exception. Unknown keys, duplicates, unexpected shapes, malformed/oversized/unreadable input and unknown nested state block. Credential presence is not authentication success.

The narrow 1.0.91 whitelist accepts strict `loggedInUsers`/`lastLoggedInUser` host/login/optional-kind metadata, `authTokens`, empty installed-plugin/trusted-folder containers, and observed inert startup state (`firstLaunchAt`, boolean `appTipShown`, **true-only** completed `reasoningSummariesCleanupDone`). Shipped runtime/application references were inspected for those startup-state roles. Account values are also skipped, never used as a supported identity API. Hooks/status-line commands, MCP, plugins/extensions, agents/skills, LSP, providers/API-key commands, permissions/commands and unknown authority structures block.

Settings use the same scanner: mandatory `disableAllHooks:true`, optional `experimental:false`, nothing else. The latter was added only after the exact real launch's `--no-experimental` persisted that benign field. `experimental:true` remains blocked. The preflight fingerprint contains structural categories/presence bits, not credential bytes or a credential-derived hash. Equal fingerprints do not promise identical credential contents; immediate repeated preflight and the existing same-user external-write assumption still apply.

### Real profile and authentication evidence

The actual `%LOCALAPPDATA%\AIReviewDesk\Copilot` profile **passes the implemented production preflight**. Its config structure contains account metadata and the three understood startup-state fields; no `authTokens` field was observed. Only schema labels/types were inspected to refine that whitelist; no identity/credential values were decoded. Absence of that field does **not** prove a usable Vault token. Windows Credential Manager backend support and plaintext fallback remain established by previous help/runtime/synthetic evidence; Carlo's actual storage target/mode is still unproven. No credential retrieval/enumeration, real account switching/logout, new OAuth flow or token-environment access occurred.

Under the refined rule, understood plaintext token-field structure can pass inspection because its value is skipped, never consumed by the app. This is not a rejection guarantee for the CLI's plaintext fallback, nor proof that it occurred here. Authentication and executable configuration remain in the same CLI-managed file; structural validation separates their treatment without copying authenticated state into another home.

The first disposable comment-only Git review ran through **`CopilotService.RunAsync`**, with the temporary contract gate opened only after scanner tests and real preflight passed. It reached preparation/start/review/result-validation stages, exited 1, and returned **Failed** with zero accepted findings. Before/after repository fingerprints matched. A diagnostic repeat of the same fixed invocation/environment identified the CLI's **No authentication information found** condition, without returning raw stderr. No JSONL review events, model manifest, terminal result, MCP evidence or structured findings were obtained. The validator was not weakened.

A bounded fresh-process native auth-selection comparison used fake account/token state, fake failing `gh.exe`, isolated homes and no real credential sources. Both opt-out states returned no selected auth state: the positive control failed, so this probe is **inconclusive**. Do not claim that `--no-auto-login` suppresses all saved-profile authentication, or that another sign-in will fix this. The immediate blocker is authentication under the exact required contract; its underlying cause remains unresolved.

### Preserved execution boundary and UX

All existing controls remain: Windows **1.0.91 only**; dedicated home; empty app-owned ephemeral cwd; separate `--add-dir`; `--disallow-temp-dir`; exact `view,grep,glob` allowlist and explicit deny list; built-in MCP/custom instructions/remote/export/ask-user/update disabled; `--no-auto-login`; experimental/eager PowerShell disabled; controlled stdin prompt; JSON streaming; pre/post repository fingerprints; bounded fail-closed validator; process-tree cancellation; completed-only handoffs. No arbitrary flags or new credential consumer was added.

Preflight still covers profile contribution directories (`hooks`, `extensions`, `installed-plugins`, `plugin-data`, `servers`, `agents`, `skills`, `instructions`), unknown top-level/MCP/LSP/provider/permission sources, cwd/ancestor contributions and links, and repository LSP/extensions/plugins/Copilot/Claude settings. Repository hook/project-extension switches are fixed false; trusted folders remain empty-only. Policy paths/registry views remain existence-only guards described above; ambiguity/access failures block. No new policy injection experiment was run. Remote managed settings/full runtime acceptance remain incomplete.

The benign OS environment allowlist and six fixed app variables above are unchanged; token/provider/Node/authority overrides are not queried/copied. Tested GitHub CLI fallback suppression remains required; unrelated fallback is not allowed to repair this failure. It has not yet been proven compatible with successful dedicated-profile authentication in this exact runner.

`ReviewContractVerified` is **false** in the delivered code, with an updated authentication/unfinished-acceptance explanation shared by runner and UI. Settings truthfully reports supported CLI/configuration but unknown identity/authentication. Official browser Sign in remains available after safe version/configuration checks; unsupported Sign out/Switch stay disabled. No interactive commands are injected.

### Verification, limits and delivery

**132 tests passed, 0 failed/skipped**, final Release suite in 22 seconds. **35 scanner cases** use synthetic random sentinel credentials and verify no escape via returns/serialization/errors/preflight diagnostics/review metadata/history/handoff paths. Cases cover approved metadata, hooks/MCP/plugins/extensions and other authority categories, unknown nested fields, malformed/duplicate/escaped JSON, size/access failures, secret-bearing settings, unchanged file contents, and a preflight fingerprint independent of credential bytes. Static assertion messages avoid printing a sentinel on failure. Existing environment/policy/version/tools/temp/terminal/cancel/stale/zero-findings/composition/handoff tests remain. Sentinel tests and code review support the scanner property; they are not a universal information-flow proof or a general secret detector.

Final Release build passed with **0 errors, 1 NU1900 warning** for the unavailable vulnerability feed. WPF launch initially timed out/reported no targetable window, then recovered when the delayed window appeared. Manual checks verified Overview/Reviews navigation, scroll reset, supported CLI/configuration state with unknown authentication/identity, enabled official Sign in (not clicked), disabled Sign out/Switch, the visible authentication gate with Start review disabled, and Security + Database / EF selection with Standard unchecked. Actual preview heading counts were shared policy 1, Security 1, Database / EF 1, in catalogue order, with snapshot and scope visible. The session was closed. Corrected the misleading static Sign in tooltip and disabled Cancel review when no review runs; these two small UI corrections require the final rebuilt binary, with no claim of a live-cancellation test. Splitter/Add Project/invalid-folder/profile-details evidence remains historical; these flows were not repeated. Real successful/zero-findings/known-finding/multiple-profile executions, live cancellation, during-run stale, completed real tool/MCP validation and completed copy actions were **not reached**, because the first review could not authenticate.

All temporary synthetic fixtures, native probes, fake credentials/gh helpers and caches are removed after checking their bounded absolute cleanup root. Application Runs is empty. The official CLI persisted `experimental:false` in real settings; no synthetic hook/MCP/plugin/config was placed in the real profile, and the scanner never rewrote its config. Source/whitespace checks are completed before committing this useful verified follow-up on the feature branch. **No merge or push to main is performed**; exact feature delivery hashes/remote state are reported in the completion response. This is partial security progress, not secure-review acceptance.

**Delegation: none.** The primary performed the focused inspection, implementation, tests and real attempt directly. Primary model/effort: unavailable (not authoritatively exposed).

## Focused runtime continuation — automatic-login opt-out blocks the saved-account path

Started from clean, pushed feature HEAD `54bc580b4e3e453feb502bb38148928922fc20d4`; main remained `3cf03e8f4598026b3c6dc9b58da9844bb7c61175`. No scanner, UI, profile-composition, environment or launch-policy code was changed. The previous synthetic auth-selection comparison lacked a positive control; the real-profile pairs below supersede its uncertainty on this host's prompt path.

### Controlled setup

All checks used installed **CLI 1.0.91**, the same npm loader/native binary as production, the existing `%LOCALAPPDATA%\AIReviewDesk\Copilot`, separate empty disposable cwd/cache/log directories, synthetic README context, and the real `CopilotContract.StartInfo` and `ReviewArguments`. Process creation remained redirected, `UseShellExecute=false`, `CreateNoWindow=true`, with no alternate user credentials. The host context was Windows user `HENNSA-RTX\henns`, session 1. The parent had no `COPILOT_GITHUB_TOKEN`, `GH_TOKEN` or `GITHUB_TOKEN` variable (existence-only checks). No parent token/provider/Node/authority values were queried or copied; every child environment was explicitly cleared and constructed. Working directory and profile resolution did not depend on a shell wrapper or reviewed-repository cwd.

Ordinary-login diagnostics removed **only `--no-auto-login`** from the otherwise fixed arguments. This was a controlled comparison, not a production policy change. To prevent unrelated GitHub CLI authentication, matched pairs prepended an app-owned `gh.exe` helper that always exits 1, writes only an invocation marker, and never reads credentials or runs GitHub CLI. A further positive control restricted PATH to that helper, Node and Windows System32, excluding the real GitHub CLI. All other tool/temp/MCP/instruction/remote/ask-user/update/experimental restrictions and both false repository prompt switches remained in force.

Preflight passed before every launch. Scanner output contained only `CredentialFieldPresent=False`, `AccountMetadataPresent=True` for the real profile. Raw protocol/diagnostic lines were transient; only fixed event names/counts, fixed error categories and validator success/count escaped. No raw stderr, credentials, identity values or review text were logged or saved. The real profile was not rewritten or copied; no new sign-in/logout/switch was performed.

### Actual comparison results

| Case | Difference from production | Result |
| --- | --- | --- |
| Baseline | None | Exit 1; `No authentication information found`; no JSONL frames. |
| PATHEXT | Add only `PATHEXT` | Same authentication failure. |
| Ordinary login + PATHEXT | Omit opt-out; prepend failing gh helper | Exit 0; real terminal/manifest/MCP/structured-result validation passed; 0 findings. Helper invoked and returned failure. |
| Matched negative + PATHEXT | Same PATH/profile/environment as preceding row; restore opt-out | Exit 1; no auth, no frames; helper not invoked. |
| Benign Windows runtime set | Add `PATHEXT`, `ProgramFiles`, `ProgramFiles(x86)`, `ProgramW6432`, `ALLUSERSPROFILE`, `PUBLIC`, `OS`, `PROCESSOR_ARCHITECTURE`, `NUMBER_OF_PROCESSORS`, `SESSIONNAME` where present | Same authentication failure. No authority variable restored. |
| Native executable | Required opt-out; bypass Node/npm loader; same failing-gh PATH/PATHEXT | Same authentication failure. |
| Empty home + ordinary login | Fresh isolated home; helper/PATHEXT; no credential/state copies | Exit 1; no auth; helper invoked and failed. Existing global sign-in was not inherited. |
| Explicit profile path | Required opt-out; add supported but deprecated `--config-dir` pointing to the same dedicated home | Same authentication failure. |
| Ordinary login, no extra variables | Production allowlist; helper PATH; omit opt-out | Exit 0; validator passed; 0 findings. Helper not invoked. |
| Matched negative, no extra variables | Restore only opt-out in preceding case | Exit 1; no auth/frames; helper not invoked. |
| Ordinary login, narrow PATH | Helper + Node + System32 only; PATHEXT; omit opt-out | Exit 0; validator passed; 0 findings. Only failing helper available/invoked; real gh excluded. |
| Actual production transport | `CopilotProcess.RunAsync`, unchanged production args/environment | Exit 1; stderr present; **0 stdout frames**. Matched diagnostic instrumentation above identifies the no-auth condition. |

The three successful diagnostic runs each emitted `session.mcp_servers_loaded`, `session.tools_updated`, `assistant.message`, `session.usage_checkpoint`, and terminal `result`. The existing unmodified validator accepted exact `{view,grep,glob}`, both disabled built-in MCPs, no unexpected tool/MCP/startup evidence, and `{"findings":[]}`. These observations improve real protocol evidence, but **do not satisfy production review acceptance** because the required opt-out was absent and the full production service/Git-review path was not completed.

### Cause, authentication source and limits

No missing benign environment dependency was found: ordinary login succeeds with the existing production allowlist, while restoring only the opt-out fails. The Node wrapper, explicit home path, process creation flags and tested Windows context additions do not explain the failure. The shipped application's actual binding is `disableAutoLogin: e.autoLogin === false` on `ProcessAuthHandle`, with the same option passed into session-manager authentication. The prompt path calls `current()` and rejects absent auth before model review. This source binding and the matched live pairs establish that **`--no-auto-login` prevents automatic selection of this saved account in the tested 1.0.91 prompt path**, not merely the observed gh fallback. This is not a universal assertion about all cached/session/account states or future versions.

The successful source is **existing authentication selected by the dedicated profile**: token overrides were absent, the real gh executable was excluded in a positive control, the only gh helper returned failure, and a fresh home could not authenticate. The real dedicated config had no plaintext `authTokens` field; no authentication files from another home were supplied/copied. These controls rule out the tested environment/GitHub CLI/unrelated-home fallback explanations. Windows Credential Manager backend support remains established from the shipped runtime, and keychain-backed selection is consistent with this evidence. **The exact credential-store target/backend used by this account was not directly observed**, and no supported noninteractive CLI auth-source/identity command was established. Do not promote that inference into a direct Vault-storage proof. No credential-returning API was used to fill that gap.

No supported secret-free explicit account-selection alternative compatible with the required opt-out was established. Restoring ordinary automatic login would violate the current required launch contract, even though the diagnostic helper blocked gh; it is not delivered as a workaround. The unresolved dependency is a supported way for 1.0.91 to select the dedicated saved account while retaining **`--no-auto-login`**, or separately approved/reverified CLI contract work. Broad environment inheritance, token injection, raw credential consumption, interactive command automation and an unsupported pseudo-API are not solutions.

### Delivered state and verification

The child environment stays unchanged: allowlisted `SystemRoot`, `WINDIR`, `PATH`, `TEMP`, `TMP`, `USERPROFILE`, `APPDATA`, `LOCALAPPDATA`, `USERDOMAIN`, `USERNAME`, `HOMEDRIVE`, `HOMEPATH`, `COMSPEC`, `ProgramData`, plus fixed app home/cache, auto-update/tgrep false and both prompt-mode hook/extension switches false. Candidate additions were investigation-only. The scanner, machine/repository preflight, exact tools/denials, all disabling flags including `--no-auto-login`, post-run validator, integrity checks and process-tree cancellation remain intact. `ReviewContractVerified` stays **false**; no WPF/profile/UI code was touched or manually retested in this pass.

No tests were added because no runtime/environment fix was justified. Existing environment/flag coverage remains. Final complete Release suite: **132 passed, 0 failed/skipped**, 32 seconds. Final Release build: **passed, 0 errors, 1 NU1900 warning** for the unavailable NuGet vulnerability feed. Whitespace checks passed; source/credential-pattern checks found no introduced secret values. No first accepted production review, production zero-findings/known-finding/multiple-profile run, live cancellation, during-run stale or completed handoff was reached. Three diagnostic zero-finding responses are explicitly separate from those acceptance cases.

Only this bounded evidence and current-state documentation is committed on the feature branch; main is not merged/pushed. Synthetic helper/native-context fixture trees are removed after resolving/checking the exact cleanup root; no synthetic executable configuration was added to the real profile. Exact commit/remote/clean-tree status is in the completion response.

**Delegation: none.** The primary performed the comparison directly; model/effort unavailable (not authoritatively exposed).
