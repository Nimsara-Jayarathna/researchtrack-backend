# Deployment files

`compose.yml` is the shared backend Compose definition for Test and Production.

GitHub Actions generates a non-secret `deploy.env` file per remote environment. Compose uses it to derive the project name, image tag, gateway network alias, and external edge network.

`config/env` is the canonical environment-contract location for both local development and deployment. This directory intentionally contains **no duplicate environment templates**.

GitHub Environment multiline secrets are created from the corresponding `config/env/<component>/.env.example`, materialized as `env/*.env` files during deployment, validated against the canonical contract, and uploaded to the VPS with restrictive permissions.

Deployment-only contents here are limited to orchestration/runtime infrastructure:

- `compose.yml`
- `Dockerfile.service`
- `image/*.sh`
- `scripts/*.sh`
- `validate-env-files.sh`
- `mysql/reconcile-databases.sh`

See `config/env/README.md` for the local/deployment configuration model and `docs/devops/backend-deployment.md` for GitHub settings, networking, GHCR, migrations, secrets, and VPS prerequisites.
