param vnetName string
param location string
param vnetCidr string
param infraSubnetName string
param infraSubnetCidr string
param containerAppsSubnetName string
param containerAppsSubnetCidr string
param infraNsgId string

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [vnetCidr]
    }
    // Subnets are declared inline so a redeploy never removes them.
    subnets: [
      {
        name: infraSubnetName
        properties: {
          addressPrefix: infraSubnetCidr
          networkSecurityGroup: {
            id: infraNsgId
          }
        }
      }
      {
        // Dedicated to the Container Apps environment (workload profiles).
        name: containerAppsSubnetName
        properties: {
          addressPrefix: containerAppsSubnetCidr
          delegations: [
            {
              name: 'Microsoft.App.environments'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
    ]
  }
}

output vnetId string = vnet.id
output infraSubnetId string = resourceId('Microsoft.Network/virtualNetworks/subnets', vnetName, infraSubnetName)
output containerAppsSubnetId string = resourceId('Microsoft.Network/virtualNetworks/subnets', vnetName, containerAppsSubnetName)
