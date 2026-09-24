# First Deployment, Cutover and Validation

## Do not merge/cut over before prerequisites

Required:

- OIDC trust configured,
- RBAC configured,
- production GitHub configuration ready,
- infrastructure workflow works,
- VM stack works,
- Container Apps environment works,
- private networking works.

## Recommended first proof

Deploy infrastructure.

Deploy one harmless/test Container App.

Prove:

```text
VM → Container App
Container App → MySQL
Container App → Kafka
Prometheus → Container App /metrics
Nginx → Container App
```

Then deploy all seven applications.

## Database

Before live cutover:

- provision/validate DB users,
- copy required legacy production data,
- run dbcheck,
- run migrations,
- verify application reads/writes.

## DNS

Only after Azure is healthy:

```text
api.researchtrack.blipzo.xyz
→ VM public IP

grafana.researchtrack.blipzo.xyz
→ VM public IP
```

## TLS

After DNS resolves:
- obtain Let's Encrypt certificates,
- validate automatic renewal,
- validate HTTP→HTTPS redirect.

## External integrations

Update after HTTPS is live:

- GitHub App callbacks,
- GitHub webhook URL,
- Jira OAuth callback,
- Jira webhook URL,
- any external IP/domain allowlists.

## Final verification

- registration/login,
- project access,
- GitHub integration,
- Jira integration,
- all seven health endpoints,
- Kafka smoke test,
- Prometheus,
- Grafana,
- rate limiting / forwarded IP,
- HTTPS/cookies,
- callback/webhook delivery.

## Rollback

Keep old production data/host available temporarily during cutover.

DNS rollback should remain possible until Azure production passes final acceptance.

The normal `main` workflow itself must not silently fall back to VPS.
