# ResearchTrack environment configuration

Each runtime component owns its own environment contract under `config/env/<service>/`.

- `.env.example` is committed and documents required keys only.
- `.env.local` contains real local values and is ignored by Git.
- Test/production values must be injected by the deployment platform or CI/CD secret store.
- Configurable business policies (for example institutional registration rules) are environment values, not constants in source code.
- Domain invariants (for example the existence of Student and Supervisor roles) remain in code.

Create local files with `./scripts/setup.sh`, then edit each service file before running that service. The setup script never overwrites an existing `.env.local`.

The Auth Service Story 1 values live in `config/env/auth/.env.local`, including institutional domains, student identifier policy, password policy, and password hashing settings.

## Shared JWT configuration

For local development, Auth Service and protected services load `Jwt__Issuer`, `Jwt__Audience`, and `Jwt__SigningKey` from `config/env/shared/.env.local`. Auth-specific token lifetimes remain in `config/env/auth/.env.local`.
