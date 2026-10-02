# Phase 1 Copilot CLI proof harness

Small Windows/.NET console harness for repeatable synthetic-fixture setup, CLI invocation, and repository fingerprinting. It uses `ProcessStartInfo.ArgumentList`, runs with an application-owned working directory, and passes only a deliberately small environment allowlist to Copilot. No credentials or existing user Copilot state are copied.

## Run

```powershell
dotnet run --project tools/ai-review-desk/phase1-harness -- --self-test
dotnet run --project tools/ai-review-desk/phase1-harness -- --probe --node "C:\Program Files\nodejs\node.exe" "C:\path\to\@github\copilot\npm-loader.js"
```

The probe creates and deletes a synthetic Git repository under the OS temporary directory. Its `.github` tree contains harmless instruction, agent, skill, and hook markers. The prompt is written over stdin. The run asks for one sentinel from the extra directory, restricts the advertised tool set to `view,grep,glob`, denies `shell,write`, disables built-in MCP and custom instructions, and requests the experimental OS sandbox with the fixture path read-only, bypass forbidden, and network disabled. It uses fresh `COPILOT_HOME` and `LOCALAPPDATA` directories. Credential environment variables and the existing user Copilot home are intentionally omitted, so this probe is expected to establish the unauthenticated failure shape unless the test setup itself changes.

The JSON result includes UTC timing, stdout/stderr, exit status, isolated-home activity, and pre/post content fingerprints. Treat captured CLI output as transient synthetic evidence; do not pass customer prompts or repositories. `--self-test` checks fingerprint determinism and that an external content change is detected. The harness does not claim to test mutation prevention, prompt execution, streaming, or cancellation unless the CLI reaches those code paths.
