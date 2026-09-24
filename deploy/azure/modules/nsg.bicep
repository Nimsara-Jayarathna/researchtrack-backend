// Infrastructure subnet NSG. Public inbound: 80/443 only. No SSH rule exists;
// the VM is administered through Azure Run Command.

param name string
param location string
param containerAppsSubnetCidr string

resource nsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: name
  location: location
  properties: {
    securityRules: [
      {
        name: 'Allow-HTTP-HTTPS-Internet'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: 'Internet'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRanges: ['80', '443']
        }
      }
      {
        name: 'Allow-MySQL-Kafka-From-ContainerApps'
        properties: {
          priority: 200
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: containerAppsSubnetCidr
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRanges: ['3306', '9092']
        }
      }
      {
        // The default AllowVnetInBound rule would otherwise open these to the
        // whole VNet; the default DenyAllInBound already covers the internet.
        name: 'Deny-Private-Ports-Other-Sources'
        properties: {
          priority: 300
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRanges: ['22', '3000', '3306', '9090', '9092', '9093', '29092']
        }
      }
    ]
  }
}

output id string = nsg.id
