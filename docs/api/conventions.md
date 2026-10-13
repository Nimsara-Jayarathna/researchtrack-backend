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
