# Primary Runtime and Reporting Evidence

## Purpose

Record the September 2026 investigation of primary-model reporting in Codex Desktop. This evidence supplements `primary-model-persistence.md`: persisted selection, per-turn runtime settings, child routing, and completion prose are distinct records.

## VIV Desktop session

The parent rollout is `C:\Users\henns\.codex\sessions\2026\09\29\rollout-2026-09-29T16-55-30-01a0eda9-e72e-7db3-9079-6dbd73d8a267.jsonl`. Its `session_meta` (line 1) identifies Codex Desktop, `source=vscode`, cwd `C:\Repos\Tweedekamer`, and CLI version `0.158.0-alpha.2.1`.

| Turn | Turn ID | Per-turn record | Completion report |
|---|---|---|---|
| VIV investigation | `01a0eda9-eb0d-76c2-852b-63e3032c7a23` | Parent rollout line 8: `turn_context` records `gpt-6-sol` / `high`. | Line 339 says primary `gpt-6-luna` / `high`; this conflicts with the per-turn record. |
| VIV implementation | `01a0edc7-7dd1-76d3-a9e8-76860ca91773` | Parent rollout line 347 records `gpt-6-sol` / `high`; line 343 also records `thread_settings_applied` as Sol High. Runtime log IDs 43894 and 43903 include `model=gpt-6-sol`, `codex.turn.reasoning_effort=high`, and `codex.request.reasoning_effort=high`. | Line 983 says primary `gpt-6-luna` / `high`; this conflicts with the per-turn record. |
| Later narrow frontend correction | `01a0edfd-6d2a-7503-a037-c3edebf913ef` | Parent rollout lines 993 and 1011 record `gpt-6-luna` / `high`; lines 987–988 apply Luna High thread settings. | Line 1095 reports Luna High, consistent with this later turn. |

The parent rollout's `spawn_agent` calls at lines 65, 71, 433, and 439 explicitly pass `gpt-6-luna`, `reasoning_effort=high`, and `fork_turns=none`. The child rollout `session_meta.source.subagent.thread_spawn` records the parent thread and task path; each child's line 8 `turn_context` records Luna High:

- Investigation frontend: `01a0edab-eb00-7df1-8676-d19445e81069`, rollout `rollout-2026-09-29T16-57-42-01a0edab-eb00-7df1-8676-d19445e81069.jsonl`.
- Investigation ZAPP: `01a0edac-0524-7652-82cf-e50e307ee8eb`, rollout `rollout-2026-09-29T16-57-48-01a0edac-0524-7652-82cf-e50e307ee8eb.jsonl`.
- Implementation ZAPP: `01a0edc9-6a55-7ef2-be06-f01fc5f6e463`, rollout `rollout-2026-09-29T17-29-55-01a0edc9-6a55-7ef2-be06-f01fc5f6e463.jsonl`.
- Implementation frontend: `01a0edc9-8dec-7a43-a4cf-128c09ba4c10`, rollout `rollout-2026-09-29T17-30-04-01a0edc9-8dec-7a43-a4cf-128c09ba4c10.jsonl`.

## Interpretation and limits

- Per-turn runtime/session metadata is authoritative evidence for the model and effort recorded for that historical turn. A thread-level model field reflects a later/current thread setting and must not be applied retrospectively.
- Spawn arguments establish the exact model and effort routed to a child. Child per-turn metadata corroborates the executed child settings when available.
- Completion prose is generated self-report, not runtime metadata. The two VIV reports' Luna High primary claim was incorrect. The records do not reveal exactly why the primary generated that claim.
- The user-level persisted selection was observed as Sol High at 2026-09-30 07:49 UTC, then Luna Medium at 09:47 UTC when this documentation turn began; the latter matched this turn's recorded settings. Neither observation establishes the value at the September 29 turns. Invocation-time selection and persisted selection can change independently of historical per-turn records.
- Standalone CLI and Desktop used different runtimes in the investigation: standalone `0.157.1`, Desktop `0.158.0-alpha.2.1`. Runtime/configuration behavior should be compared using records from each invocation.
- Quota consumption does not identify the model used.
- Local records establish the requested/effective Codex model slug and effort, but do not provide an independent server-side model-resolution receipt or expose the exact Desktop selector transport payload.
