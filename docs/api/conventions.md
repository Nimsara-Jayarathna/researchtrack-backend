# API conventions

- Public API prefix: `/api/v1`.
- React calls only the gateway.
- Resource/service routes are configured in YARP; project-nested GitHub/Jira/Meeting/Submission routes have higher routing priority than the generic project route.
- API success envelope: `success=true`, `data`, `meta`.
- API failure envelope: `success=false`, `error`, `meta`.
- `meta.traceId` is returned on every standardized API response and `X-Correlation-ID` is propagated where supplied safely.
- Use domain-specific error codes in the owning service; never make the frontend depend on human-readable message strings.
- Do not expose secrets, stack traces or raw database errors.
- Swagger UI is enabled in Development/Testing or when `OpenApi:Enabled=true`.
