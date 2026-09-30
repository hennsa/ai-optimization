# Global Codex instructions

These instructions apply across repositories. Follow the user's current request and the repository's applicable instructions.

Before delegating, consult `~/.codex/MULTI_AGENT.md` for delegation, routing, model selection, reasoning effort, verification-agent use, concurrency, and fork behavior. Consult `~/.codex/AI_WORKFLOW.md` for the general AI-assisted development workflow, including planning, implementation, verification, context management, and escalation.

For every routed child task, select and explicitly pass both the model and reasoning effort. Do not rely on the primary model or inherited child defaults. GPT-6 currently uses the V2 multi-agent backend according to model catalog metadata even while `features.multi_agent_v2 = false`; follow the V2 fork rules in `MULTI_AGENT.md`.

Keep delegation bounded, independently actionable, and useful. The primary agent remains responsible for integration and acceptance, without automatically repeating the complete delegated task. Report the primary model and reasoning effort only when reliable runtime/session metadata for the current turn is actually available to the agent. Never infer or guess them from persisted defaults, invocation selection alone, routing policy, child settings, or prior turns. If unavailable, report `Primary model/effort: unavailable (not authoritatively exposed)` or omit them. For each child, report the exact model and reasoning effort explicitly passed to `spawn_agent`; describe these as routed settings, and claim actual child runtime settings only when reliable child runtime/session metadata is available. Preserve the delegation report: delegated tasks, route rationale, outcomes, and integration checks. If no delegation was used, report `Delegation: none`.
