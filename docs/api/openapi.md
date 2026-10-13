# Swagger and OpenAPI

Complete [local setup](../development/local-setup.md) and start the gateway plus
the services whose documents you need. Open `http://localhost:5000/swagger`.

## Service selector and document URLs

The gateway UI lists six independent business-service specifications and its own
local specification. It does not merge paths/models from the services. An empty
`paths` object in the gateway document is expected when it has no local controllers.

| Selection | Path on the gateway |
| --- | --- |
| Swagger UI | `/swagger` |
| Auth | `/swagger/auth/v1/swagger.json` |
| Project | `/swagger/project/v1/swagger.json` |
| GitHub | `/swagger/github/v1/swagger.json` |
| Jira | `/swagger/jira/v1/swagger.json` |
| Meeting | `/swagger/meeting/v1/swagger.json` |
| Submission | `/swagger/submission/v1/swagger.json` |
| Gateway | `/swagger/v1/swagger.json` |

For Production prepend `https://api.researchtrack.blipzo.xyz`. These service paths
require deployment of the gateway service-OpenAPI implementation. Direct local
service documents remain available at `/swagger/v1/swagger.json` and the UI at
`/swagger` on ports 5101–5106.

## Enablement and deployment

1. Set `OpenApi__Enabled=true` on the gateway and each desired service using their
   canonical environment bundles. Development enables Swagger automatically;
   test factories explicitly set the flag.
2. Deploy/restart affected components through the appropriate environment workflow.
3. Open the UI and select each enabled service; check that its JSON loads and
   describes the expected business paths.
4. Check the original gateway document remains available separately.

In non-Development environments the gateway flag controls its UI, document and
proxy document access. Downstream flags independently control each service's
Swagger generation. Documentation is anonymous when enabled; business endpoints
retain service-owned authorization. Do not place provider or JWT secrets in specs.

## Routing and failures

Only GET requests to the six known v1 document paths are forwarded. YARP uses
existing clusters and rewrites the destination path to `/swagger/v1/swagger.json`.
Unknown services, versions and arbitrary downstream paths are not exposed. UI
URLs are relative to support reverse-proxy path bases. No extra public service
ingress or Nginx rules are needed.

A disabled downstream document returns 404; an unreachable downstream can return
502. Documents load on demand, so one unavailable service does not stop the UI or
other services from loading. Use the [troubleshooting guide](../development/troubleshooting.md)
to distinguish disabled documentation, an old deployment and a routing failure.
