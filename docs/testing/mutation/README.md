# Mutation Testing

Mutation testing is implemented with repository-local Stryker.NET and dedicated xUnit v2 harness projects so the normal xUnit v3 unit/mock suite remains unchanged.

## Current scopes

- Project selected business rules
- Auth complete feature/service layer (six independent subscopes)
- Submission selected business rules

For Auth details and commands, see:

- `auth/AUTH_MUTATION_SCOPE.md`
- `auth/AUTH_MUTATION_RUNBOOK.md`

The baseline stage intentionally uses `break = 0`. Final regression thresholds are set only after survivor analysis and a verified hardened score.
