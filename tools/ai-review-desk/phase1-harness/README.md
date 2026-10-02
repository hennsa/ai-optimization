# Phase 1 Copilot CLI proof harness

Small Windows/.NET console harness for repeatable synthetic-fixture setup, CLI invocation, and repository fingerprinting. It uses `ProcessStartInfo.ArgumentList`, runs with an ephemeral application-owned working directory, and passes a deliberately small environment allowlist. It uses but does not recreate, delete, inspect, or modify the dedicated persistent profile at `%LOCALAPPDATA%\AIReviewDesk\Copilot`; no credential values are read, copied, exported, or logged.

## Run

```powershell
dotnet run --project tools/ai-review-desk/phase1-harness -- --self-test
dotnet run --project tools/ai-review-desk/phase1-harness -- --probe --node "C:\Program Files\nodejs\node.exe" "C:\path\to\@github\copilot\npm-loader.js"
dotnet run --project tools/ai-review-desk/phase1-harness -- --probe-case --node "C:\Program Files\nodejs\node.exe" "C:\path\to\@github\copilot\npm-loader.js" shell-attempt
dotnet run --project tools/ai-review-desk/phase1-harness -- --phase2-probe --node "C:\Program Files\nodejs\node.exe" "C:\path\to\@github\copilot\npm-loader.js"
dotnet run --project tools/ai-review-desk/phase1-harness -- --phase2-case --node "C:\Program Files\nodejs\node.exe" "C:\path\to\@github\copilot\npm-loader.js" read-absolute-outside-no-temp
```

The suite creates a separate synthetic Git repository and application-owned working directory for each probe under the OS temporary directory. It uses the persistent app profile authenticated beforehand through `copilot login --web-flow`, with `COPILOT_CACHE_HOME` redirected to temporary storage. The process environment excludes token variables. The prompt covers external read/search, exact tool inventory, file and Git mutation requests, shell execution, repository instruction/agent/skill/hook content, empty findings, stdin Unicode and 24 KiB context, concurrent mutation, and active cancellation. Tool manifests and JSONL event shapes are summarized without retaining raw transcripts. It does not depend on the experimental OS sandbox.

The JSON result includes CLI version, event names and timing, effective/requested/executed tools, MCP status, exit status, and pre/post fixture/Git fingerprints. Do not pass customer prompts or repositories. `--self-test` checks fingerprint determinism and change detection. `--probe-case` runs one named case from the suite. Authenticated model requests require network access; run the proof harness with the approved network-enabled execution context.

Phase 2 cases add synthetic outside-path, link, task, shell-alias, source-injection, workspace/repository MCP, cancellation, and concurrent-mutation probes. `--phase2-case` runs one case. The `*-no-temp` cases include `--disallow-temp-dir`; other cases challenge the original Phase 1 launch. `ManifestEvents` and `ToolsUpdatedDataShapes` show when a usable manifest appears relative to tool execution. Profile-level hook/MCP positive controls require temporarily adding only synthetic hook/server configuration to the dedicated profile and checking a temp marker; they are documented in the [Phase 2 report](../docs/phase2-copilot-cli-verification.md), not automated by this harness. Remove those synthetic profile files after testing. Do not commit generated JSON results or credential-bearing profile files.
