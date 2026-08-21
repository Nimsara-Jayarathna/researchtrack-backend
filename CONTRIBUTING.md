# Contributing

## Branching Model

- `main`: stable production-ready branch, pull requests only.
- `develop`: integration branch for completed sprint work, pull requests only.
- Create feature and bugfix branches from `develop`.
- Create urgent production hotfixes from `main`, then merge the approved fix back to `develop`.
- Do not push directly to `main`.
- Do not push directly to `develop` except for repository administration changes agreed by the team.
- Use Squash and Merge for `feature/*` and `bugfix/*` pull requests into `develop`.
- See `docs/devops/branching-strategy.md` for the full ResearchTrack Git strategy.

## Branch Naming Convention

Use:

```text
<type>/RT-xx-short-title
```

Allowed types:

- `feature`
- `bugfix`
- `hotfix`

Examples:

- `feature/RT-14-jira-connection`
- `bugfix/RT-41-login-validation`
- `hotfix/RT-52-production-login-failure`

## Commit Message Convention

Use short, meaningful conventional messages:

- `feat: ...`
- `fix: ...`
- `chore: ...`
- `docs: ...`
- `refactor: ...`
- `test: ...`

## Local Verification

Before PR, run:

```bash
./scripts/check.sh
```

For database integration changes, also run:

```bash
./scripts/test.sh integration
```

## Artifacts Never to Commit

- `bin/`
- `obj/`
- `TestResults/`
- `.env.local`
- `.env.admin.local`
- credentials, tokens, OAuth secrets, or database passwords

## PR Expectations

- Keep pull requests small and focused.
- Include Jira reference, affected services, API/DB/Kafka/configuration changes, test evidence, and known limitations.
- Require at least one teammate approval before merge.
- Resolve blocking review comments before merge.
