# Phase 1 — Copilot CLI contract proof

**Recommendation: NO-GO for Phase 2 readiness or WPF implementation.** This is an evidence-gated no-go: the CLI and its controls are available, but this run could not authenticate and therefore did not reach model/tool execution. The intended read-only boundary has not been proven or disproven experimentally. Repeat the proof with an explicitly provisioned isolated Copilot authentication method before Phase 2.

**Date/platform:** 2026-10-02; Windows 10.0.26300.0 (x64), .NET SDK 10.0.401/runtime 10.0.12, Node.js 24.21.0. **CLI:** GitHub Copilot CLI 1.0.91, obtained temporarily from the official `@github/copilot` npm package and run via its Node loader. It was not installed globally. `gh` and a preinstalled `copilot` command were absent.

## Evidence and recommendation

The durable harness is [`../phase1-harness/`](../phase1-harness/README.md). It creates a temporary synthetic Git repository containing source sentinels plus inert instruction, agent, skill, and hook markers. It launches Copilot from a separate owned working directory through `ProcessStartInfo.ArgumentList`, writes a prompt to stdin, captures stdout/stderr and exit status, and fingerprints fixture file paths/content before and after. Its process environment excludes GitHub/Copilot tokens, the user's Copilot home, and unrelated user configuration. The fixture and isolated state directories are deleted after each run.

| Contract question | Documented | Observed in this environment | Phase 1 result |
|---|---|---|---|
| Read/search an external repository | `--add-dir` adds a directory to allowed paths. | Not reached: authentication failed before any model/tool request. | Unresolved |
| Restrict capabilities | `--available-tools` makes only listed tools available; CLI help accepted `view,grep,glob` and `--deny-tool shell,write`. | Effective tool list and denied tool behavior not observable without a model run. | Unresolved |
| Instructions, agents, skills, hooks, MCP | `--no-custom-instructions` disables custom instructions; `--disable-builtin-mcps` disables built-ins; config `disableAllHooks` disables user/repository hooks. Critically, `--add-dir` also loads that directory's `.github/skills` and `.github/agents` as trusted configuration. An isolated `COPILOT_HOME` removes user extension/config inputs. | Flags/settings were supplied, but no discovery behavior was observed. | Unresolved; treat repo agents/skills as untrusted prompt input and verify their effect cannot add tools |
| Isolated state/configuration | `COPILOT_HOME` relocates CLI config/state. | Fresh `COPILOT_HOME` and `LOCALAPPDATA` were set; no credentials/user home were inherited. The failed pre-run does not prove all state/config paths obeyed those settings. | Partially established only as invocation setup |
| Authentication | Docs describe `/login`, Copilot/GitHub token variables, or `gh auth login`; token vars are sensitive. | With isolated home and credential-free environment, CLI emitted `No authentication information found` on stderr and exited `1`. No secret was passed or recorded. Authenticated, expired, and interactive first-run cases were not tested. | Unauthenticated failure observed; usable app auth unresolved |
| Structured output and exits | `--output-format json` emits JSONL. Programmatic documentation describes stdout JSONL and diagnostics on stderr; exact stable prompt-mode event fields are not specified by the programmatic reference. | The pre-agent auth error produced empty stdout, plain-text stderr, exit `1`, and no JSONL record. No success, empty finding, tool, denial, malformed, or partial stream captured. | Error shape observed; success parser contract unresolved |
| Streaming | `--stream on` is supported and is documented to progressively display responses. | No authenticated response to timestamp. | Unresolved |
| Cancellation/process tree | CLI documents interactive cancel keys; workflow exit `130` on interruption. | No active model/tool work to cancel. The harness can kill a timed-out process tree, but this is harness behavior, not proof of CLI descendants. | Unresolved |
| Forbidden capabilities / adversarial prompts | Tool allowlist and OS sandbox controls are documented. The CLI states ordinary shell execution is unrestricted by default when sandboxing is off. | No model/tool execution; write, delete, shell, Git mutation, and instruction-conflict attacks were not attempted against the fixture. | Unresolved |
| Repository invariance | Not a CLI guarantee. | Fingerprint including Git metadata was stable across the failed CLI invocation (`a9b9aefc1f274eb28d51e04e917f6f86452f9b48c62f4824a42045a4b5b9bfc4` before and after). Since the CLI performed no review, this is not evidence of a read-only boundary. | Harness mechanism works; reviewer invariance unresolved |
| Stdin prompt transport | Official programmatic guide documents piping a prompt to `copilot`; piped input is ignored when `-p` is also used. | Harness sends the prompt over redirected stdin using safe argument APIs, but authentication failure happened before prompt handling could be confirmed. | Documented; observation unresolved |
| Context/diff size | CLI exposes `--context default|long_context` (model-dependent). No universal safe byte/token limit is documented. | No model request/context accepted. | No threshold established; require conservative app cap and explicit oversized-context limitation until measured |
| Mid-run state changes | AI Review Desk can independently fingerprint state before/after. | Self-test changed a fixture file after the initial hash and confirmed the fingerprint changed. No active-run external mutation test occurred. | Detection algorithm tested; integration/stale-result behavior unresolved |

The probe requested `--experimental --sandbox`; its isolated settings requested `sandbox.enabled`, `allowBypass: false`, a read-only fixture path, hooks disabled, and outbound/local network denied. Authentication failed before the sandbox's effective status/policy could be inspected. Official docs call local sandboxing experimental; Windows requires a supported Windows Insider/BaseContainer configuration. The local OS version string alone does not establish that the backend is supported. The sandbox must be empirically available and fail closed on every supported product host before it can be part of the contract.

## CLI interface notes

- Supported programmatic input documented: `-p/--prompt PROMPT`, or a prompt piped to stdin (piped input is ignored if `-p` is also supplied). The harness uses redirected stdin; receipt by the model remains unobserved.
- Documented non-interactive controls include `--model`, `--reasoning-effort`, `--context default|long_context`, `--output-format json`, `--stream on|off`, `--available-tools`, `--deny-tool`, `--add-dir`, `--no-custom-instructions`, `--disable-builtin-mcps`, `--no-remote`, and `--no-remote-export`. The model can be selected by CLI flag, `COPILOT_MODEL`, isolated settings, or CLI default; docs say non-silent prompt output reports the selected model. Exact successful output was not observed.
- The tested prompt-mode authentication failure exited `1`. Do not assume a universal exit-code contract from that one failure: the programmatic reference defines `0/1/2/130` for `copilot workflow run`, not for all prompt-mode failures. Check process exit plus parse/validation outcome.
- CLI docs describe `COPILOT_HOME` for config/state and `COPILOT_AUTO_UPDATE=false` to avoid version changes during a run. Those settings were supplied to the harness; state access was not verified past pre-run authentication failure.
- Windows process-tree termination was not established. The harness kills the launched process tree on timeout/interrupt, but that cannot establish all Copilot-created descendants terminate or that the CLI handles cancellation cleanly.
- No precise safe diff/context threshold or universal model token limit was established. Keep v0.1 context sizing bounded and fail visibly on truncation/rejection until measured.

**Design constraints for a rerun:** keep `--available-tools=view,grep,glob`; explicitly disable custom instructions, hooks, and built-in MCP; use a clean application-owned `COPILOT_HOME`; pass prompt/context via stdin; disable remote/session export and auto-update; enable the experimental OS sandbox with bypass disabled, a read-only repository grant, and a closed network policy. The repository `--add-dir` behavior imports agents and skills, so Phase 2 must verify that those markers cannot expand tool authority or defeat the supplied review policy. Fail closed when the selected CLI version, sandbox host support, or requested controls are unavailable. Do not proceed to WPF implementation based on documentation alone.

## Source documents

Current official GitHub documentation consulted on 2026-10-02:

- [CLI command reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-command-reference)
- [CLI programmatic reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-programmatic-reference)
- [Running Copilot CLI programmatically](https://docs.github.com/en/copilot/how-tos/copilot-cli/automate-copilot-cli/run-cli-programmatically)
- [CLI configuration directory](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-config-dir-reference)
- [Configuring Copilot CLI permissions](https://docs.github.com/en/copilot/how-tos/copilot-cli/set-up-copilot-cli/configure-copilot-cli)
- [Using local sandboxing](https://docs.github.com/en/copilot/how-tos/cloud-and-local-sandboxes/using-local-sandboxing)
- [Understanding local sandbox filesystem policies](https://docs.github.com/en/copilot/concepts/agents/copilot-cli/understanding-local-sandboxing)

## Verification performed

- `dotnet build tools/ai-review-desk/phase1-harness/AIReviewDesk.Phase1Harness.csproj --no-restore` — succeeded, 0 warnings/errors.
- `dotnet run --project tools/ai-review-desk/phase1-harness --no-build -- --self-test` — passed deterministic fingerprint and changed-content detection.
- Harness `--probe` against CLI 1.0.91 — failed closed at missing authentication (exit 1); stdout empty; plain-text auth diagnostic on stderr; synthetic repository fingerprint including Git metadata unchanged (`a9b9aefc1f274eb28d51e04e917f6f86452f9b48c62f4824a42045a4b5b9bfc4` before and after).
- CLI `--version`, `--help`, `help sandbox`, `help environment`, `help permissions`, `help limits`, and `help config` inspected. No token values, customer repository data, or persisted model responses were captured.
