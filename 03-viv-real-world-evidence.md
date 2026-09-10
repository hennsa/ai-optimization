# VIV Real-World Evidence

This document records the implementation run that exposed the routing-efficiency concern.

The VIV details are evidence only. They must not become project-specific global routing rules.

## Implementation slice

A VIV implementation slice added:

- a new .NET 10 local-development executable;
- project and solution wiring;
- database bootstrap logic;
- tests;
- developer documentation;
- runtime SQL verification;
- safety checks.

## Routing used

- Master: Sol High
- Delegated solution/deployment inspection: Sol Low
- Delegated migration/safety inspection: Sol Medium
- Independent final verification: Sol High

## Outcome

The delegation worked functionally.

The final Sol High verification found three real issues that were subsequently corrected:

- SQL endpoint identity;
- authentication mode handling;
- rollback error preservation.

This is considered evidence that premium verification can add real value at important safety boundaries.

## Usage impact

The run was expensive:

- 5-hour allowance dropped from 100% to 11%;
- weekly allowance dropped from 100% to 86%.

## Concern

Delegated work remained entirely in the Sol model family, including bounded or mechanical work such as:

- solution/project convention inspection;
- deployment packaging inspection;
- file/project creation;
- straightforward editing;
- documentation;
- routine tests.

This does not match the intended global objective of deliberately routing delegated work to the lowest-cost sufficiently capable model.

## Desired interpretation

The lesson is not "avoid Sol High".

The lesson is:

- use Sol High where difficult reasoning or risk justifies it;
- use cheaper models for bounded execution, inspection, and mechanical work;
- de-escalate after the difficult part is resolved;
- preserve expensive independent verification where it materially improves correctness.
