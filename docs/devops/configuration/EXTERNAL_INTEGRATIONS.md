# External Integrations and DNS

This page covers configuration held **outside** GitHub Actions and Azure: DNS, the GitHub App, Jira, Brevo and the frontend. Values from these systems end up in the service bundles described in [SERVICE_CONFIGURATION.md](SERVICE_CONFIGURATION.md).

Production hostnames (defaults in both Azure workflows; override with the GitHub variables `PRODUCTION_API_HOSTNAME` / `PRODUCTION_GRAFANA_HOSTNAME`):

| Purpose | Hostname | Served by |
|---|---|---|
| Public API | `api.researchtrack.blipzo.xyz` | Nginx on the VM → private `rt-gateway-prod` |
| Monitoring UI | `grafana.researchtrack.blipzo.xyz` | Nginx on the VM → `grafana:3000` |

All public API routes go through the Gateway. Paths below are relative to the API hostname.

## Before the first Production deployment

These are needed so the env bundles validate. The Production validator requires `https://` URLs and rejects `localhost` and any `TEST_HOSTNAMES`. Setting the final values early is fine: nothing external calls them until DNS points at Azure.

| Item | Where configured | Env bundle key(s) | Value |
|---|---|---|---|
| Frontend origin (CORS) | Frontend hosting | `GATEWAY_ENV_FILE`: `FRONTEND_ORIGIN` | `https://<frontend-host>` (exact origin, no path) |
| Password-reset link base | Frontend | `AUTH_ENV_FILE`: `PasswordReset__FrontendBaseUrl` | `https://<frontend-host>` |
| Transactional email | Brevo account | `AUTH_ENV_FILE`: `Brevo__BaseUrl`, `Brevo__ApiKey`, `Brevo__SenderEmail`, `Brevo__SenderName` | Production API key (secret), verified sender |
| GitHub App identity | GitHub → Settings → Developer settings → GitHub Apps → *your app* | `RT_GITHUB_SERVICE_ENV`: `GitHub__AppId`, `GitHub__AppSlug`, `GitHub__ClientId`, `GitHub__ClientSecret`, `GitHub__PrivateKeyBase64` | App ID and slug; client secret and private key (base64 of the `.pem`) are secrets |
| GitHub return origin | — | `RT_GITHUB_SERVICE_ENV`: `GitHub__FrontendReturnOrigin` | `https://<frontend-host>`. The service redirects users to `/github/access-updated` and `/github/request-access` on this origin |
| Jira OAuth app | Atlassian developer console | `JIRA_ENV_FILE`: `Jira__ClientId`, `Jira__ClientSecret`, `Jira__RedirectUri`, `Jira__SyncIntervalMinutes` | See the Jira note below |

A separate GitHub App (or at least a separate webhook secret and client secret) for Production, rather than reusing Test's, is recommended.

## Only during the Production cutover

Change these **after** Azure passes validation on the private checks (see [FIRST_DEPLOYMENT_CHECKLIST.md](FIRST_DEPLOYMENT_CHECKLIST.md)). Changing them earlier sends real traffic to an unready backend.

1. **DNS A records** (at the DNS provider for `blipzo.xyz`):
   - `api.researchtrack.blipzo.xyz` → VM public IP
   - `grafana.researchtrack.blipzo.xyz` → VM public IP

   The IP is shown in the *Azure Infrastructure - Production* run summary, or run `az network public-ip show -g rg-researchtrack-prod -n pip-researchtrack-prod --query ipAddress -o tsv`.
2. **TLS:** re-run *Azure Infrastructure - Production*. Certbot obtains both certificates (HTTP-01 on port 80) and Nginx switches to HTTPS. Renewal is automatic. No GitHub value changes.
3. **GitHub App** settings, which must match the running code:

   | GitHub App field | Value | Code |
   |---|---|---|
   | Setup URL and Callback URL | `https://api.researchtrack.blipzo.xyz/api/github/access-source/install/callback` | `GitHub__SetupCallbackUrl`; also sent as the OAuth `redirect_uri` (`GitHubUserAuthorizationClient`) |
   | Webhook URL | `https://api.researchtrack.blipzo.xyz/api/github/webhooks` (`/api/v1/github/webhooks` also routes) | `GitHubWebhooksController` |
   | Webhook secret | the same value as `GitHub__WebhookSecret` | HMAC validation |
4. **Jira OAuth** callback / webhook: point them at the Production API hostname (see the note below).
5. **Allowlists:** update any external allowlist that references the old VPS IP to the Azure VM public IP.
6. Set the GitHub variable `PRODUCTION_VERIFY_PUBLIC_ENDPOINT=true`.

Keep the old VPS Production DNS target recorded until final acceptance, so a DNS rollback stays possible.

### Jira note: confirm before cutover

On the branch this guide was written from, `ResearchTrack.JiraService` contains only persistence code. No controller consumes `Jira__ClientId`, `Jira__ClientSecret` or `Jira__RedirectUri`, and there is no Jira webhook endpoint yet. The keys exist in the contract, and the Production validator requires `Jira__RedirectUri` to be HTTPS. The **exact callback/webhook paths must be taken from the Jira feature code once merged**. The Gateway already routes `/api/v1/jira/**` and `/api/v1/projects/{projectId}/jira/**` to the Jira service.

## Test equivalents

Test uses the same kinds of external settings, but with Test hostnames behind the VPS reverse proxy. Keep them out of Production values: list those hostnames in the Production variable `TEST_HOSTNAMES` so the validator enforces the separation.
