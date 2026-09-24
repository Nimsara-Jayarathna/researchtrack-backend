// Internal (VNet-only) Container Apps environment. Apps with "external" ingress
// are reachable from the VNet through the environment's internal load
// balancer, never from the internet.

param name string
param location string
param subnetId string
param enableLogAnalytics bool
param logAnalyticsName string

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = if (enableLogAnalytics) {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: 1
    }
  }
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: name
  location: location
  properties: {
    vnetConfiguration: {
      infrastructureSubnetId: subnetId
      internal: true
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    appLogsConfiguration: enableLogAnalytics
      ? {
          destination: 'log-analytics'
          logAnalyticsConfiguration: {
            customerId: logAnalytics!.properties.customerId
            sharedKey: logAnalytics!.listKeys().primarySharedKey
          }
        }
      : null
  }
}

output id string = environment.id
output defaultDomain string = environment.properties.defaultDomain
output staticIp string = environment.properties.staticIp
