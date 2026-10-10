# AuthService Mutation Scope

Pamudi's Auth mutation scope deliberately targets the complete Auth **feature/business-service layer** rather than controllers, DTOs, EF Core mappings, migrations, startup code, configuration binders, or external email/token transport implementations.

## Included production targets

- `Features/Passwords/PasswordPolicyValidator.cs`
- `Features/Authentication/UserAuthenticationService.cs`
- `Features/Passwords/PasswordResetService.cs`
- `Features/Registration/RegistrationService.cs`
- `Features/Users/UserAccountService.cs`
- `Features/Users/UserDirectoryService.cs`

These targets cover password-policy boundaries, login decisions, password-reset lifecycle, registration/OTP decisions, password/account mutation, refresh-token revocation, and user-directory filtering/resolution.

## Dedicated mutation harness

`tests/ResearchTrack.AuthService.MutationTests` is intentionally separate from the normal xUnit v3 suite. It uses xUnit v2 for Stryker compatibility and contains focused copies/adaptations of the existing unit/mock coverage tests. It requires no external database; EF Core InMemory and SQLite in-memory are used where service behaviour needs persistence/transactions.

The normal `ResearchTrack.AuthService.Tests` project remains authoritative for regular CI and code-coverage reporting. When survivor analysis reveals a meaningful weakness, strengthen the normal test first and mirror/adapt the same scenario into the mutation harness.

## Auth subscopes

Each feature is mutated independently so reports stay understandable and a weak component cannot hide behind a stronger one:

1. password policy
2. authentication
3. password reset
4. registration
5. user account
6. user directory

No mutation break threshold is enforced during this baseline phase. After survivor analysis and hardening, establish an evidence-based regression threshold.
