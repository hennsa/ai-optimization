# Primary Model Persistence Experiment

## Purpose

Record a controlled experiment on whether selecting a primary model and reasoning effort in Codex changes the top-level persisted model selection.

## Verified observation

Date: 2026-09-10

### Terra to Sol

Before selecting Sol High, the top-level persisted values were `model = "gpt-5.6-terra"` and `model_reasoning_effort = "high"`. After selecting Sol High, they were `model = "gpt-5.6-sol"` and `model_reasoning_effort = "high"`. The `config.toml` file hash and last-modified timestamp changed.

### Sol to Terra

A second controlled test began with `model = "gpt-5.6-sol"` and `model_reasoning_effort = "high"`. After selecting Terra High in a new Codex chat, the persisted values were `model = "gpt-5.6-terra"` and `model_reasoning_effort = "high"`. The `config.toml` file hash and last-modified timestamp changed again.

No prompt or agent action was required: selecting the model in the Codex UI was sufficient in both tests.

## Interpretation

The top-level `model` and `model_reasoning_effort` settings behave as persisted primary-selection state rather than fixed child-routing defaults. This conclusion is limited to the observed behaviour; it does not assert how Codex implements it internally.

## Implication for routing documentation

Routing documentation must distinguish the currently persisted primary selection from delegated child routing. Child model and effort selection remains governed by the explicit routing policy in `MULTI_AGENT.md`; the top-level values must not be used as evidence that children inherit the master's model family.
