// One ResearchTrack Container App (rt-<service>-prod).
//
// Applied per changed service by deploy/azure/scripts/deploy-container-apps.sh
// with the image and the validated GitHub Environment configuration. Secret
// values arrive as a @secure() parameter and never appear in deployment
// history.

param name string
param location string
param environmentId string

@allowed(['gateway', 'auth', 'project', 'github', 'jira', 'meeting', 'submission'])
param service string

@description('Immutable image reference (tag = git SHA, or @sha256 digest).')
param image string

@description('Plain environment variables: [{ name, value }] or [{ name, secretRef }].')
param env array

@description('{ items: [{ name, value }] } — Container App secrets referenced by env[].secretRef.')
@secure()
param secrets object

param cpu string = '0.5'
param memory string = '1Gi'
param minReplicas int = 1
param maxReplicas int = 1

param registryServer string = 'ghcr.io'
param registryUsername string = ''

@description('Name of the entry in secrets.items holding the registry password; empty for public images.')
param registryPasswordSecretName string = ''

var isGateway = service == 'gateway'

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  tags: {
    application: 'researchtrack'
    service: service
    environment: 'production'
  }
  properties: {
    environmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        // "external" inside an internal environment = reachable from the VNet
        // (Nginx, Prometheus), not from the internet.
        external: true
        targetPort: 8080
        transport: 'auto'
        // Service-to-service calls use http://rt-<svc>-prod inside the
        // environment; redirecting them to HTTPS would surface 301s through
        // YARP. Only the Gateway is reached by Nginx, and always over HTTPS.
        allowInsecure: !isGateway
        additionalPortMappings: isGateway
          ? [
              {
                // Gateway metrics stay off the API port (Program.cs), as in Test.
                external: true
                targetPort: 9100
                exposedPort: 9100
              }
            ]
          : null
      }
      secrets: secrets.items
      registries: empty(registryPasswordSecretName)
        ? []
        : [
            {
              server: registryServer
              username: registryUsername
              passwordSecretRef: registryPasswordSecretName
            }
          ]
    }
    template: {
      containers: [
        {
          name: service
          image: image
          env: env
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          probes: [
            {
              type: 'Startup'
              httpGet: {
                path: '/health/live'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 5
              timeoutSeconds: 4
              failureThreshold: 36
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
              }
              periodSeconds: 15
              timeoutSeconds: 4
              failureThreshold: 4
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
              }
              periodSeconds: 10
              timeoutSeconds: 4
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
}

output latestRevisionName string = app.properties.latestRevisionName
output fqdn string = app.properties.configuration.ingress.fqdn
