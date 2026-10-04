# Large-context certification

## Why this exists

Two broad real Sonnet 5.5 / High reviews produced output that failed the strict raw JSON contract, even though controlled small compatibility calls passed. A small deterministic fixture therefore establishes basic compatibility only; it does not establish output-envelope reliability near the size of broad repository reviews. Native Copilot response-schema support remains a separate investigation and is not assumed here.

## Product size contract

`large-context-1` classifies a composed prompt as **Large** at 1,500,000 characters or more. The boundary is deliberately just below the observed 1,516,396-character VIV prompt, so the reproduced workload is unambiguously gated. It is a product risk boundary, not a provider token limit. The review context ceiling remains 1,600,000 characters; the bounded diff/context input allowance is 1,550,000 characters so the synthetic certification fixture can reach the observed prompt band while remaining below the final context ceiling.

Preview and fresh Start Review preparation measure the exact composed prompt's UTF-16 .NET character count and UTF-8 byte count. Neither is presented as a token count. The runner may retain Copilot-emitted numeric usage telemetry, but no prompt, source, or response body is retained as size evidence.

## Model capability metadata

The restricted live `models.list` adapter retains only context limits and context tiers actually advertised by the pinned CLI, including `max_prompt_tokens`, `max_output_tokens`, `max_context_window_tokens`, and supported context tiers. Missing values remain unavailable. They are displayed separately from price, allowance, and certification. Context limits inform certification and drift checks; they do not confer trust by themselves.

## Trust and launch behavior

Existing certificates remain Basic certificates. A Basic certificate authorizes normal reviews according to current policy but never implies Large support. A Large certificate is additive and bound to CLI version, model ID, reasoning, Basic certificate, authority and tool manifest, strict output-envelope/schema contract, large suite and boundary versions, tested prompt size, and reported context capability. Legacy certificates are not promoted.

After fresh composition and before Copilot launches, Large reviews require a valid explicit model and matching Basic plus Large certificates. Auto is blocked for Large because the concrete model is unknown before allowance is spent. The error offers three user-controlled next steps: run the explicit large-context test, switch to Selected Paths, or choose a model with a valid Large certificate. Scope is never narrowed automatically.

The Settings operation **Test large-context compatibility** uses a deterministic app-owned synthetic repository, the production composer, profiles, authority and strict parser, with zero-findings and known-defect probes. It is separate from basic compatibility. The app warns that the very large prompt can cost materially more, exact cost is unknown, and no customer repository is used. It is never run automatically. The generated fixture is deleted afterward; persistence contains only numeric size/token/usage evidence, context-limit snapshots, envelope/schema outcomes, probe outcomes and status.

Context capability drift invalidates Large trust only when a relevant advertised limit or tier changes. Display names, prices, and other cosmetic/billing changes do not.

## Current decision

Sonnet 5.5 remains **Needs retest**. The previous controlled small calls remain Basic-only evidence and are not reused as Large evidence. No Large certification or paid model call was performed while implementing this architecture. Native response-schema support remains deferred.
