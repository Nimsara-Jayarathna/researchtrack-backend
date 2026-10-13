# API conventions

- Public API prefix: `/api/v1`.
- React calls only the gateway.
- Resource/service routes are configured in YARP; project-nested GitHub/Jira/Meeting/Submission routes have higher routing priority than the generic project route.
- API success envelope: `success=true`, `data`, `meta`.
- API failure envelope: `success=false`, `error`, `meta`.
- `meta.traceId` is returned on every standardized API response and `X-Correlation-ID` is propagated where supplied safely.
- Use domain-specific error codes in the owning service; never make the frontend depend on human-readable message strings.
- Do not expose secrets, stack traces or raw database errors.
- Swagger UI is enabled in Development or when `OpenApi:Enabled=true` (the test factory explicitly enables it).
- The gateway UI lists independent service documents at `/swagger/{service}/v1/swagger.json` for `auth`, `project`, `github`, `jira`, `meeting`, and `submission`, plus its local `/swagger/v1/swagger.json`. Enable OpenAPI on both gateway and downstream services; see the root README Swagger section for production setup.

## Response examples

Typical successful JSON response:

```json
{
  "success": true,
  "data": {},
  "meta": { "traceId": "<trace-id>", "timestamp": "2026-10-13T00:00:00Z" }
}
```

Validation failure (illustrative values):

```json
{
  "success": false,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Validation failed.",
    "fieldErrors": [{ "field": "email", "errors": ["Email is required."] }]
  },
  "meta": { "traceId": "<trace-id>", "timestamp": "2026-10-13T00:00:00Z" }
}
```

Bind field errors to controls by `field`, and handle programmatic behavior by
`error.code` rather than matching human-readable messages. Optional error details
are operation-specific. Health responses, provider webhook acknowledgements and
OpenAPI JSON have their own formats; do not force them into this envelope.

## Authentication and resource access

Auth issues HttpOnly access and refresh cookies. Shared policies are
`ResearchTrack.Authenticated`, `ResearchTrack.StudentOnly` and
`ResearchTrack.SupervisorOnly`. Missing/expired authentication produces 401;
authenticated callers without the required permission receive 403 where applicable.
Services may use resource-specific responses to avoid exposing inaccessible records.
The owning service must validate project access as well as role; frontend route
guards are a user-experience convenience, not authorization.

Read [registration](../stories/registration.md),
[session lifecycle](../development/us-102-secure-login-rbac.md),
and [Swagger/OpenAPI setup](openapi.md) for concrete flows and schemas.
