# US-102 — Secure Login & Role-Based Access

## Scope

US-102 adds secure authentication/session lifecycle and reusable role authorization without migrating unrelated feature APIs.

### Public Auth API

- `POST /api/v1/auth/login`
- `POST /api/v1/auth/refresh`
- `POST /api/v1/auth/logout`
- `GET /api/v1/auth/me`

Registration remains under `/api/v1/auth/register/*`.

## Session design

- Access token: signed HS256 JWT, short-lived, `HttpOnly` cookie `ss_access_token`, path `/api`.
- Refresh token: 256-bit opaque random value in `HttpOnly` cookie `ss_refresh_token`, path `/api/v1/auth`.
- Only SHA-256 of the refresh token is persisted.
- Successful refresh atomically revokes the current token and issues a replacement.
- Replayed/revoked/expired refresh tokens receive `401`.
- Logout revokes the presented refresh token and expires both cookies.
- Local development may use `Cookie__Secure=false`; HTTPS deployments must set it to `true`.

## Credential security

Login normalizes email, verifies the PBKDF2 password hash, and always returns the same `Invalid email or password.` message for unknown users and bad passwords. The unknown-user path performs a dummy PBKDF2 verification to reduce the obvious timing difference used for account enumeration.

## Authorization

`ResearchTrack.BuildingBlocks.Api` provides:

- `ResearchTrack.Authenticated`
- `ResearchTrack.StudentOnly`
- `ResearchTrack.SupervisorOnly`

AuthService and ProjectService validate the same JWT issuer/audience/signing key. ProjectService integration tests prove `401` for missing/expired authentication, `403` for a Student on Supervisor-only policy, and success for a Supervisor.

Frontend role guards are convenience/UX only. Services remain the authority. Future Project endpoints must additionally apply resource-specific ownership/membership authorization where required.

## Database audit

No US-102 migration is required. `20260822111500_AddRegistrationFlow` already created `refresh_tokens` with:

- `UserId` foreign key
- unique `TokenHash`
- `ExpiresAt`
- nullable `RevokedAt`
- `CreatedAt`

The current user model has no separate account-disabled flag, so all persisted registered users are treated as active. Introducing suspension/deactivation should be a separate explicit account-management requirement rather than an undocumented schema change in this story.

## Acceptance criteria

| AC | Implementation |
|---|---|
| AC1 | Login verifies credentials, creates cookie session, and returns server-assigned role/user profile. Frontend routes by role. |
| AC2 | Unknown email and wrong password both return generic `401`; no cookies/session are created. |
| AC3 | Shared `SupervisorOnly` policy validates Supervisor authorization in ProjectService. |
| AC4 | Student token receives `403` for Supervisor-only policy; frontend also blocks Supervisor UI. |
| AC5 | Protected endpoints require valid JWT; frontend `/me` bootstrap + refresh/retry returns expired sessions to login. |
| AC6 | Logout revokes refresh token, clears cookies, and frontend clears local session state. |

## Required configuration

AuthService:

```text
Jwt__Issuer
Jwt__Audience
Jwt__SigningKey
Jwt__AccessTokenMinutes
Jwt__RefreshTokenDays
Cookie__Secure
```

ProjectService must receive the same:

```text
Jwt__Issuer
Jwt__Audience
Jwt__SigningKey
```

`Jwt__SigningKey` must contain at least 32 UTF-8 bytes.

## Verification

```bash
dotnet build ResearchTrack.sln
./scripts/test.sh auth
./scripts/test.sh project
./scripts/start-all.sh
```

Then verify login, `/me`, refresh rotation/replay rejection, Student `403`, Supervisor authorization, and logout through Gateway port `5000`.
