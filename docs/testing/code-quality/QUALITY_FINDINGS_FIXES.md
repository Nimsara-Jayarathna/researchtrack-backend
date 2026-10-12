# SE3112 — SonarQube findings remediation (2026-10-12)

## Starting baseline (actual completed SonarQube Cloud run)

- Analysis ID: `117303d4-4803-402e-95cc-a9c75a0390a5`
- Commit: `e1e3f3f` on `main`
- Quality gate: `OK`; measures: 26,818 LOC, 85 code smells, 105 minutes of technical debt, 2.4% duplication, cyclomatic complexity 4,518, cognitive complexity 2,214.
- **The updated source has not been scanned.** Re-run both CI workflows to establish real post-fix numbers. Do not use the old automatic-analysis 36K-LOC result as a before/after comparison.

## Applied production changes

1. **Authentication cookie hardening**: `AuthCookieOptions` defaults to `Secure=true` outside Development/Testing. The service refuses to start when `Cookie:Secure=false` is supplied in production-like environments. The service retains configurable HTTP cookies only for Development/Testing (tests intentionally specify false). Production runbooks already specify `Cookie__Secure=true`. Both session and deletion cookies share the validated value.
2. **Dummy password timing guard**: generate a random throwaway password for the dummy hash instead of a fixed string in source. The value is not a credential and is never persisted.
3. **Health responses**: use `context.RequestAborted` when writing JSON, so a cancelled client request can stop the response work.
4. **Host lifetime**: `await app.RunAsync()` for AuthService and Gateway, replacing `app.Run()`.
5. **IDE0005**: remove the eight production-source imports identified by Roslyn's code-style check. EF migrations are *not* edited.
6. **Native CI scope**: `dotnet format style` receives repository-relative `--include`/`--exclude` paths, covering only each production project and skipping migrations/generated files; mock regression check catches accidental absolute paths.
7. **Sonar GitHub Action**: move only this workflow to Node.js 24-capable actions (`checkout@v5`, `setup-dotnet@v5`, `setup-java@v5`, `upload-artifact@v6`).

## Requires manual review in SonarQube

- Cookie rule: `Secure` depends on a configuration value, but the config is now validated before the service can start. The static analyzer might still flag this because it cannot infer the environment guard. Inspect the finding after the scan. Do not blindly mark it fixed unless the runtime behavior is verified; review contextual security finding in SonarQube as appropriate.
- `DesignTimeDatabase.CreateConnectionString`: `User=design;Password=design` is an intentionally dummy connection string for EF migration/model scaffolding. No deployed runtime credential is loaded from it. It is intentionally unchanged to preserve the design-time tooling's contract. Document as safe dummy when reviewing any hotspot; do not treat as a real leaked credential.
- The project's real DB and auth secrets must remain in secure deployment configuration. No secrets were added to source.

## Verification checklist

```bash
bash ./scripts/code-quality.sh self-test
bash ./scripts/code-quality/sonar-cloud.sh self-test
bash ./scripts/code-quality.sh setup
bash ./scripts/code-quality.sh check ci
# Optional on a review branch: bash ./scripts/code-quality.sh fix && bash ./scripts/code-quality.sh check
```

Re-run the main-branch **Backend SonarQube Cloud Analysis** with the existing `production` GitHub Environment; inspect security findings, post-fix metrics and quality gate. Quality gate enforcement can be enabled with `SONAR_ENFORCE_GATE=true` *after* reviewing the conditions and coverage implications. Leave other students' unit, mutation and performance workflows untouched.

### Manual runtime checks

- Production/Staging `Cookie__Secure=true`: host starts and session/deletion responses send `Secure; HttpOnly; SameSite=Strict` cookies.
- Production/Staging `Cookie__Secure=false`: host must reject startup; no insecure auth session is issued.
- Development/Testing `Cookie__Secure=false`: local HTTP login/refresh/logout continues to work.
- Health endpoint with client cancellation: verify cancellation does not surface as unrelated server failure.
