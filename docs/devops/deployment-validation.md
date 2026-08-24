# Backend deployment validation notes

The deployment implementation was statically checked before packaging.

Validated in the generation environment:

- all GitHub Actions workflow YAML files parse as YAML;
- `deploy/compose.yml` parses as YAML;
- deployment/local Bash scripts pass `bash -n` syntax validation;
- `deploy/templates` does not exist; `config/env` is the only committed environment-contract source;
- every expected local `.env.example` exists for shared, gateway, Auth, Project, GitHub, Jira, Meeting, Submission, and local DB admin configuration;
- the remote MySQL contract exists at `config/env/mysql/.env.example`;
- a fully materialized dummy Test deployment was generated from the canonical examples and passed `deploy/validate-env-files.sh`;
- the validator checks deployment files against their canonical `config/env` key sets;
- Gateway friendly variables are mapped in `ResearchTrack.Gateway/Program.cs` so the same contract works locally and in Docker;
- service DB names/users/passwords are cross-checked against `mysql.env` before upload;
- private Compose service URLs are checked before deployment;
- Test and Production runtime environment names are validated separately;
- all six EF Core context names used by image builds match the existing service DbContexts;
- EF Core package/tool version used by the Docker build matches the repository's EF Core 10.0.9 baseline.

Not executable in the packaging environment:

- `dotnet restore/build/test` because the .NET SDK is not installed in the artifact-generation container;
- `docker build`, `docker compose config`, and a live Compose deployment because Docker is not installed there;
- live SSH/GHCR/VPS validation because those require the project's real GitHub/VPS credentials and runtime.

Those checks are intentionally performed by the repository CI and the first `develop` deployment. Configure the `test` GitHub Environment first and validate Test before enabling Production deployment.
