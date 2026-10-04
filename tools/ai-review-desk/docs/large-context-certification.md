# Large-context certification

## Why this exists

Two broad real Sonnet 5.5 / High reviews produced output that failed the strict raw JSON contract, even though controlled small compatibility calls passed. The small deterministic fixture establishes basic compatibility only. It does not establish output-envelope reliability near the size of broad repository reviews. Native Copilot response-schema support remains deferred.

## Large v2 product size contract

The versioned product contract is `large-context-2`:

- **Normal:** fewer than 1,500,000 final composed prompt characters.
- **Large v2:** 1,500,000 through 1,600,000 final composed prompt characters, inclusive.
- **Unsupported:** more than 1,600,000 final composed prompt characters.

These are product risk boundaries, not provider token limits. The 1.5M boundary sits below the observed 1,516,396-character VIV prompt, which twice reproduced output-contract drift. The 1.6M ceiling covers the observed 1,565,905-character workload with headroom while retaining an explicit product limit.

The exact prompt returned by `PromptComposer` is authoritative. Repository preparation asks that same composer for the empty-context prompt length using the actual snapshot, scope, selected paths and profiles. It subtracts that exact overhead from the 1.6M ceiling and feeds the remaining context budget into the existing bounded Git/diff collection. Any further truncation retains a visible application-limit marker. The result is measured again after production composition. Character and UTF-8 byte counts are compact evidence only; neither is represented as a token count.

If a prompt somehow exceeds the ceiling, execution blocks before model launch and recommends **Selected Paths**. Re-certification cannot authorize a prompt beyond the product ceiling.

## Model capability metadata

The restricted live `models.list` adapter retains only context limits and tiers actually advertised by pinned CLI 1.0.91, including `max_prompt_tokens`, `max_output_tokens`, `max_context_window_tokens`, and supported context tiers. Missing values remain unavailable. They appear separately from price, account allowance, and certification. They inform drift checks but do not confer trust by themselves.

## Trust and launch behavior

A valid Basic certificate may authorize ordinary reviews under existing policy. It never implies Large support. Large v2 requires a matching Basic certificate plus a Large v2 certificate for the CLI, model ID, reasoning effort, authority and tool manifest, strict output-envelope/schema contract, Large suite and boundary versions, and relevant advertised context limits. The certification probe runs at exactly the 1.6M composed-prompt ceiling and authorizes the complete Large v2 tier. Token usage is recorded only when emitted by Copilot; it is evidence, never a tier definition.

Certificates from `large-context-1` remain historical evidence and do not authorize Large v2. Migration preserves their Basic status and does not infer Large v2 trust. In particular, Luna remains Basic Certified with Large v2 unverified until the explicit test is run; Sonnet 5.5 remains Needs retest.

Large reviews require an explicit model and matching Basic plus Large v2 certificates before model launch. Auto is blocked because its runtime model is unknown before allowance is spent. Users can run the explicit Large test, select **Selected Paths**, or choose a model with Large v2 certification. Scope is never narrowed automatically.

## Large test operation

The Settings action **Test large-context compatibility** is explicit and never runs automatically. Before starting, the app warns that it sends a very large synthetic prompt, may cost materially more than normal compatibility tests, exact cost is not known beforehand, and no customer repository is used.

The fixture is generated programmatically inside an app-owned disposable repository. Its deterministic C# source declarations are synthetic and mostly low-semantic-noise; clean/zero-findings and known-defect probes use the production composer, authority, profiles, envelope and schema validation. The fixture is removed after testing. Persisted evidence contains only numeric size/token/usage information, context-limit snapshots, envelope/schema outcomes, probe outcomes and status. No prompt, source or model-response contents are stored.

Context capability drift invalidates Large trust when an advertised prompt/output/context limit or context tier changes. Cosmetic model names and billing metadata do not.

## Current decision

Sonnet 5.5 remains **Needs retest**. Its prior small controlled calls remain Basic-only evidence and are not reused as Large evidence. No Large certification or paid model call was performed while implementing this size-tier correction.
