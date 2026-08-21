# ResearchTrack Git Strategy

ResearchTrack uses a simple two-environment Git strategy.

## Permanent branches

| Branch | Purpose | Deployment |
| --- | --- | --- |
| `main` | Stable production-ready code | Production |
| `develop` | Integrated sprint work | Test |

Do not create permanent `qa`, `staging`, `production`, or `release` branches.

## Temporary branches

| Branch pattern | Starts from | Merges to | Use |
| --- | --- | --- | --- |
| `feature/RT-xx-name` | `develop` | `develop` | Normal Jira story work |
| `bugfix/RT-xx-name` | `develop` | `develop` | Defects found before production |
| `hotfix/RT-xx-name` | `main` | `main`, then `develop` | Urgent production fixes |

Delete temporary branches after merge.

## Feature and bugfix merge gate

`feature/*` and `bugfix/*` branches must merge to `develop` by pull request only.

Required before merge:

- Jira story or bug is identified.
- Scope matches acceptance criteria.
- Required backend implementation is complete.
- Build, unit tests, relevant integration tests, and formatting checks pass.
- GitHub Actions CI passes.
- No passwords, tokens, API credentials, or `.env.local` files are committed.
- DB migrations are included when schema changes are required.
- API contract changes are documented.
- Kafka topic or event changes are documented when applicable.
- At least one teammate reviews the PR.
- Blocking review comments are resolved.
- No unresolved merge conflicts remain.

Use **Squash and Merge** for feature and bugfix PRs into `develop`.

## Test environment

Every push to `develop` represents the Test environment candidate.

The Test environment is isolated from Production and contains:

- API Gateway
- Auth Service
- Project Service
- GitHub Service
- Jira Service
- Meeting Service
- Submission Service
- `mysql-test` with separate service databases
- `kafka-test`
- object storage for test files

Database-per-service means logical ownership. One Test MySQL server may host separate service databases.

Test validation includes:

- Functional QA
- Acceptance criteria verification
- Integration testing
- API testing
- E2E testing with the frontend
- Regression testing
- Authentication and RBAC checks
- GitHub and Jira integration testing
- Kafka event testing
- Defect validation

Fix Test defects through `bugfix/RT-xx-description` branches. Do not commit fixes directly to `develop`.

## Production release gate

`develop` merges to `main` by pull request only after release validation.

Required before merge:

- Agreed sprint or release scope is complete.
- Included stories meet acceptance criteria.
- CI is green.
- Unit, integration, E2E, regression, and load-test evidence is available as applicable.
- Critical defects are resolved.
- High or blocking security issues are resolved.
- Authentication and RBAC are verified where affected.
- DB migrations are tested.
- Kafka changes are tested where applicable.
- Production images build successfully.
- Production configuration variables are identified.
- Secrets remain outside the repository.
- Documentation is updated when the change requires it.
- Monitoring or service diagnostics are available where relevant.
- Team or QA approval is recorded.

## Production environment

Production is isolated from Test and contains:

- Production service deployment
- `mysql-prod` with separate service databases
- `kafka-prod`
- object storage for production files

Never share database data, Kafka state, object-storage files, credentials, OAuth secrets, or environment variables between Test and Production.

## Versioning

Create official version tags only from `main`.

Use:

```text
vMAJOR.MINOR.PATCH
```

For example:

```text
v1.0.0
```
