---
concept: Rounding
description: Money operations use specific, consistent rounding policies to ensure predictable results across all currencies.
status: Active
---
# Rounding

Money operations use specific rounding policies to ensure consistency across currencies.

## Logic Intent
The `TransferService` is responsible for ensuring that money moves safely between accounts.
It enforces specific buffers for large transfers to mitigate risk.

## Business Rules
- [RULE-001] Transfers must be greater than zero.
- [RULE-002] Transfers over $1,000 require a 1% balance buffer.
- [RULE-003] Standard transfers only require balance >= amount.

## BDD Verification
Verified by [Rounding.feature](./Rounding.feature) using Reqnroll.


## Physical Anchors
[yab-hash:TransferService.ValidateFunds:nDrXDksGridYO4DKjgohXJeXsermMYYsKS2wJhJTmkE=]
[yab-hash:TransferService:tAZPylKluN1Iak5v7lgg90VYCkMBBZ0lpSJoQX/+oF4=]