# AI Review Desk v0.1 Review Profiles

## Composition model

Every profile composes one shared reviewer policy with a small profile-specific focus block. Keep common authority, evidence, output-schema, and honesty requirements in one base policy; profiles must not copy or override it. The application supplies repository identity, snapshot metadata, scope, and deterministic diff/context separately from the profile.

The shared policy must state that Copilot is an independent reviewer only; may not modify files or execute commands; must use only provided read/search access and supplied context; must not infer unprovided repository state; should report findings only when evidence supports a plausible defect; should not invent findings to fill a quota; may return an empty finding list; should distinguish confirmed, probable, and possible certainty; and must include location/evidence/impact/recommendation where available. It should identify limitations and uncertainty, and not claim tests or tools ran unless evidence says so.

## Built-in profiles

| Profile | Review focus |
|---|---|
| **Standard implementation** | Correctness of the change, edge cases, error handling, maintainability issues that create concrete risk, and regressions visible in the supplied scope. |
| **Bug fix** | Whether the change addresses the reported behavior, handles adjacent inputs/states, and avoids masking symptoms or introducing a related defect. No assumption that unstated reproduction steps were tested. |
| **Regression** | Behavior changed relative to the supplied base/context, compatibility and neighboring flows, state transitions, and likely regressions. Avoid restating pre-existing issues as new findings unless the change materially affects them. |
| **Security** | Trust boundaries, input validation, authorization, secret handling, unsafe data flow, injection, and exposure introduced or worsened by the change. Findings need a concrete path and impact; do not claim exploitability without evidence. |
| **Database / EF** | Schema/model consistency, migrations, query behavior, transaction and concurrency risks, data integrity, nullability, and EF usage. Do not infer runtime database behavior absent evidence. |
| **API contract** | Request/response shape, validation, status/error behavior, compatibility, serialization, versioning, and consistency between implementation and visible contracts. |
| **Frontend** | UI state and interaction correctness, loading/error/empty states, accessibility concerns evident in changed code, and client/server contract handling. Do not claim browser behavior was executed. |
| **Architecture** | Boundary violations, unintended coupling, responsibility placement, lifecycle/concurrency concerns, and consistency with architecture evidence supplied in context. Avoid taste-only or speculative redesign suggestions. |
| **Test coverage** | Missing tests for changed behavior, weak assertions, uncovered edge cases, and tests that appear inconsistent with the implementation. Assess visible test changes/source only; do not claim tests were run. |

### Deferred profile

**Cross-repository Integration** may be described conceptually for future work: focus on contract alignment and compatibility across repositories. It remains unavailable until multi-repository scopes, identity, snapshot coordination, and integrity checking exist.

## Versioning

Built-in profile identifiers and versions are captured with each run. Wording changes that alter review behavior require a profile version increment so history remains interpretable. Custom profile editing is out of scope for v0.1.
