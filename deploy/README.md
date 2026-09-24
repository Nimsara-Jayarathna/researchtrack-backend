# Deployment files

`compose.yml` is the backend Compose definition for the Test VPS deployment. Production runs on Azure. See `deploy/azure/` and `docs/devops/azure-production/`.

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

See `config/env/README.md` for the local/deployment configuration model and `docs/devops/configuration/TEST_VPS.md` for the Test GitHub Environment settings and VPS prerequisites.

## Container reconciliation behavior

`deploy/scripts/deploy-stack.sh` treats container recreation as an explicit deployment postcondition rather than assuming `docker compose up` noticed every moving-tag, environment, or bind-mounted configuration change.

- Application services are first reconciled normally by Compose. The script then verifies each running container uses the exact image ID pulled for the current `test`/`production` tag. If an environment file changed and Compose left the existing container in place, or if the running image ID is stale, that service is force-recreated and verified again.
- Prometheus and Grafana are force-recreated on every deployment. Their configuration is bind-mounted and the workflow replaces the host provisioning tree, so recreation guarantees the newly uploaded Prometheus rules/configuration and Grafana datasource/dashboard provisioning are active without an SSH/manual restart.
- The MySQL reconciliation script is also bind-mounted. Before database provisioning, the deployment compares the script mounted in the existing MySQL container with the newly uploaded host script and recreates MySQL only when the mounted script is stale. The named `mysql-data` volume is retained.
- `verify-health.sh` independently checks that each application container is running the exact pulled image, all expected Prometheus alert rules are loaded, and both ResearchTrack Grafana dashboards plus the Prometheus datasource are provisioned.

Named volumes (`mysql-data`, `prometheus-data`, and `grafana-data`) are not deleted by these recreations.
