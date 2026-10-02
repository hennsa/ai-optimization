# Phase 1 Copilot CLI proof harness

Small Windows/.NET console harness for repeatable synthetic-fixture setup, CLI invocation, and repository fingerprinting. It uses `ProcessStartInfo.ArgumentList`, runs with an application-owned working directory, and passes only a deliberately small environment allowlist to Copilot. No credentials or existing user Copilot state are copied.

## Run

```powershell
dotnet run --project tools/ai-review-desk/phase1-harness -- --self-test
dotnet run --project tools/ai-review-desk/phase1-harness -- --probe --node "C:\Program Files\nodejs\node.exe" "C:\path\to\@github\copilot\npm-loader.js"
```

The probe creates and deletes a synthetic Git repository under the OS temporary directory. Its `.github` tree contains harmless instruction, agent, skill, and hook markers. The prompt is written over stdin. The run asks for one sentinel from the extra directory, restricts the advertised tool set to `view,grep,glob`, denies `shell,write`, disables built-in MCP and custom instructions, and disables hooks through isolated settings. It uses fresh `COPILOT_HOME` and `LOCALAPPDATA` directories but runs as the current Windows identity and attempts the normal Windows Credential Manager OAuth lookup. Token environment variables are excluded. `USERPROFILE` and `APPDATA` are retained as Windows profile variables; the explicit fresh `COPILOT_HOME` remains set. It does not depend on the experimental OS sandbox.

The JSON result includes UTC timing, stdout/stderr, exit status, isolated-home activity, and pre/post content fingerprints. Treat captured CLI output as transient synthetic evidence; do not pass customer prompts or repositories. `--self-test` checks fingerprint determinism and that an external content change is detected. On this host, the isolated invocation of CLI 1.0.91 fails before model/tool execution with `No authentication information found`, so the basic `--probe` does not claim to complete the authenticated adversarial Phase 1 suite. The repository fingerprint remained unchanged across that failed invocation.
