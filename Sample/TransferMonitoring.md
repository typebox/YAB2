---
concept: Rounding
type: ops-playbook
description: Operational procedures for monitoring and troubleshooting the transfer service.
audience: Operations
status: Active
---
# Transfer Monitoring Playbook

## What to Monitor
The transfer service validates funds using rounding policies before allowing money movement.

## How It Works
BDD Scenarios: [Rounding.feature](./Rounding.feature)
- Transfer amounts are rounded according to currency precision rules
- Validation ensures sufficient balance after rounding

Unit Tests: [TransferServiceTests](./TransferServiceTests.cs)
- `Should_Validate_Funds` — confirms the core validation logic

## Alerts
- If transfer validation fails unexpectedly, check rounding precision
- Monitor for decimal overflow errors in transfer amounts

## Incident Response
1. Check if rounding rules have changed
2. Run the functional tests to verify behavior:
   - `dotnet test --filter "FullyQualifiedName~Rounding"`
   - `dotnet test --filter "FullyQualifiedName~TransferServiceTests"`
3. Compare with the Business Rule documentation
