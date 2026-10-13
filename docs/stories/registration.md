# Registration and email verification

Auth owns registration policy, institutional role inference, OTP verification,
account persistence and session creation. Before local verification, complete
[local setup](../development/local-setup.md), configure Auth/shared JWT and Brevo
settings, provision the Auth database and apply its migrations. API examples below
are request shapes; replace placeholders with local test values and keep secrets
out of checked-in files.

## Endpoints and flow

Use the gateway (`http://localhost:5000` locally):

| Method | Path | Behavior |
| --- | --- | --- |
| GET | `/api/v1/auth/register/config` | Return non-secret registration/password constraints. |
| POST | `/api/v1/auth/register/init` | Validate email, persist OTP state and send email. |
| POST | `/api/v1/auth/register/verify` | Verify OTP and return a short-lived registration token/role information. |
| POST | `/api/v1/auth/register/complete` | Consume verified registration state, create user/session and write auth cookies. |
| POST | `/api/v1/auth/register` | Retained direct-registration compatibility endpoint. |

For the email verification flow:

1. Read registration configuration and collect the email.
2. Submit `{"email":"student@students.example.edu"}` to `register/init`.
3. Submit `{"email":"student@students.example.edu","otp":"<received-code>"}`
   to `register/verify` and retain its registration token.
4. Submit the completion payload using that token:

```json
{
  "registrationToken": "<token-from-verify>",
  "fname": "Example",
  "lname": "Student",
  "name": "<registration-number-if-required>",
  "password": "<password-meeting-configured-policy>"
}
```

When verification reports that role selection is required, completion also accepts
`role` with `STUDENT` or `SUPERVISOR`. This is configuration-dependent: a role
already inferred into the verified session takes precedence over a submitted role.
Do not describe all registration modes as ignoring the role field.

The retained direct-registration endpoint accepts:

```json
{
  "firstName": "Example",
  "lastName": "Student",
  "email": "<institutional-email>",
  "registrationNumber": "<registration-number>",
  "password": "<password-meeting-configured-policy>"
}
```

For that endpoint, `fname`, `lname` and `name` are compatibility aliases for the
canonical profile fields; canonical fields take precedence. Its submitted role
is not trusted: it resolves the role from allowed institutional domains. The
controller retains this endpoint separately from the OTP completion flow.

## Policy configuration

The Auth environment contract includes:

- Domain and identifier rules: `Registration__DomainRestrictionEnabled`, student
  and supervisor email domains, `Registration__StudentEmailPrefixRestrictionEnabled`,
  `Registration__StudentIdentifierPattern`.
- Student registration-number requirement and email matching switches.
- First/last name, email and registration-number length limits.
- `Registration__OtpExpirySeconds` and `Registration__SessionExpirySeconds`.
- `PasswordPolicy__MinimumLength`, maximum length and uppercase/lowercase/digit/
  special-character requirements.
- `PasswordHashing__Iterations`, salt size and hash size.
- Brevo URL/API key/sender identity, Auth token lifetimes and cookie security.

Use the complete [Auth template](../../config/env/auth/.env.example) for exact
keys and required values. The public configuration endpoint exposes constraints,
not database credentials, OTP values, hashes, JWT keys or provider secrets.

## Validation, persistence and security

Email is normalized to lowercase; student registration identifiers are normalized
to uppercase where applicable. Server validation enforces configured required
fields, lengths, email/identifier rules and password policy. Duplicate checks are
backed by database unique constraints. Passwords use PBKDF2-SHA256 with random salts
and configured work factors; plaintext passwords are not persisted or returned.
OTP and registration-session state have expiry/consumption checks. Registration
completion creates the authenticated session; see [session lifecycle](../development/us-102-secure-login-rbac.md).

Auth owns users, OTPs, registration sessions and refresh-token persistence through
its migration history. Do not infer the current schema from the old single-migration
README description; review the service's `Persistence/Migrations/` and model snapshot.

| Original acceptance concern | Current verification focus |
| --- | --- |
| Successful registration | Valid completion persists the user and creates a session; direct compatibility route remains separately covered. |
| Role assignment | Domain/session inference is authoritative; configured role selection is validated when inference is absent. |
| Invalid institutional email | Restrictions and identifier switches are enforced according to configuration. |
| Duplicate prevention | Service validation plus database constraints prevent conflicting accounts. |
| Required fields | Invalid input returns field-specific validation errors. |

## Verification

1. Run `./scripts/test.sh auth` for deterministic service/unit tests.
2. Use the dedicated test database and `./scripts/test.sh integration` when
   validating MySQL constraints/migrations.
3. For manual flow verification, start Auth/Gateway and configured dependencies,
   use a controlled test inbox, and inspect the Auth Swagger document for current
   request/response schemas. Check invalid/expired OTP, consumed/expired registration
   token, duplicate account and configured role-selection cases.

Login, refresh, logout, password changes and authorization are documented in
[US-102](../development/us-102-secure-login-rbac.md). MFA is outside this registration flow.
