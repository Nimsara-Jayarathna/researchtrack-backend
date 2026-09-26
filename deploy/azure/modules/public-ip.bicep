// The only public address in Production. DNS for the API and Grafana hostnames
// points here.

param name string
param location string

resource publicIp 'Microsoft.Network/publicIPAddresses@2024-05-01' = {
  name: name
  location: location
  sku: {
    name: 'Standard'
  }
  properties: {
    publicIPAllocationMethod: 'Static'
    publicIPAddressVersion: 'IPv4'
  }
}

output id string = publicIp.id
output ipAddress string = publicIp.properties.ipAddress
