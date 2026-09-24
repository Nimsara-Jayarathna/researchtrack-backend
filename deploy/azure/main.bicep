// ResearchTrack Azure Production infrastructure (desired state).
//
// Subscription-scope so the resource group itself is Bicep-owned. Deployed by
// .github/workflows/azure-infrastructure.yml:
//
//   az deployment sub what-if -l <location> -f deploy/azure/main.bicep \
//     -p deploy/azure/parameters/production.bicepparam
//
// The seven Container Apps are declared in modules/container-app.bicep and
// applied by the application workflow, which supplies their images and
// secrets. Redeploying infrastructure therefore never resets a running app.

targetScope = 'subscription'

@description('Short environment name used in resource names.')
param environmentName string = 'prod'

@description('Azure region for all resources.')
param azureLocation string

param vnetCidr string = '10.20.0.0/16'
param infraSubnetCidr string = '10.20.10.0/24'
param containerAppsSubnetCidr string = '10.20.20.0/23'

@description('Static private IP of the infrastructure VM (MySQL/Kafka endpoint for Container Apps).')
param vmPrivateIp string = '10.20.10.4'

@description('2 vCPU / 8 GiB, non-Spot.')
param vmSize string = 'Standard_D2as_v5'

param vmAdminUsername string = 'researchtrack'

@description('SSH public key required by Azure for Linux VMs. SSH is never opened in the NSG; administration uses Run Command.')
param vmAdminSshPublicKey string

param dataDiskSizeGb int = 64

@allowed(['StandardSSD_LRS', 'Premium_LRS'])
param dataDiskSku string = 'StandardSSD_LRS'

@description('Send Container Apps logs to a capped Log Analytics workspace.')
param enableLogAnalytics bool = true

var names = {
  resourceGroup: 'rg-researchtrack-${environmentName}'
  vnet: 'vnet-researchtrack-${environmentName}'
  infraSubnet: 'snet-researchtrack-infra-${environmentName}'
  containerAppsSubnet: 'snet-researchtrack-containerapps-${environmentName}'
  nsg: 'nsg-researchtrack-infra-${environmentName}'
  publicIp: 'pip-researchtrack-${environmentName}'
  nic: 'nic-researchtrack-infra-${environmentName}'
  vm: 'vm-researchtrack-infra-${environmentName}'
  dataDisk: 'disk-researchtrack-data-${environmentName}'
  containerAppsEnvironment: 'cae-researchtrack-${environmentName}'
  logAnalytics: 'log-researchtrack-${environmentName}'
}

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: names.resourceGroup
  location: azureLocation
}

module nsg 'modules/nsg.bicep' = {
  scope: rg
  name: 'researchtrack-nsg'
  params: {
    name: names.nsg
    location: azureLocation
    containerAppsSubnetCidr: containerAppsSubnetCidr
  }
}

module network 'modules/network.bicep' = {
  scope: rg
  name: 'researchtrack-network'
  params: {
    vnetName: names.vnet
    location: azureLocation
    vnetCidr: vnetCidr
    infraSubnetName: names.infraSubnet
    infraSubnetCidr: infraSubnetCidr
    containerAppsSubnetName: names.containerAppsSubnet
    containerAppsSubnetCidr: containerAppsSubnetCidr
    infraNsgId: nsg.outputs.id
  }
}

module publicIp 'modules/public-ip.bicep' = {
  scope: rg
  name: 'researchtrack-public-ip'
  params: {
    name: names.publicIp
    location: azureLocation
  }
}

module dataDisk 'modules/managed-disk.bicep' = {
  scope: rg
  name: 'researchtrack-data-disk'
  params: {
    name: names.dataDisk
    location: azureLocation
    sizeGb: dataDiskSizeGb
    sku: dataDiskSku
  }
}

module vm 'modules/vm.bicep' = {
  scope: rg
  name: 'researchtrack-vm'
  params: {
    vmName: names.vm
    nicName: names.nic
    location: azureLocation
    vmSize: vmSize
    adminUsername: vmAdminUsername
    adminSshPublicKey: vmAdminSshPublicKey
    subnetId: network.outputs.infraSubnetId
    privateIp: vmPrivateIp
    publicIpId: publicIp.outputs.id
    dataDiskId: dataDisk.outputs.id
  }
}

module containerAppsEnvironment 'modules/container-apps-environment.bicep' = {
  scope: rg
  name: 'researchtrack-container-apps-environment'
  params: {
    name: names.containerAppsEnvironment
    location: azureLocation
    subnetId: network.outputs.containerAppsSubnetId
    enableLogAnalytics: enableLogAnalytics
    logAnalyticsName: names.logAnalytics
  }
}

module privateDns 'modules/private-dns.bicep' = {
  scope: rg
  name: 'researchtrack-private-dns'
  params: {
    zoneName: containerAppsEnvironment.outputs.defaultDomain
    staticIp: containerAppsEnvironment.outputs.staticIp
    vnetId: network.outputs.vnetId
    vnetName: names.vnet
  }
}

output resourceGroupName string = rg.name
output vmName string = vm.outputs.name
output vmPrivateIp string = vmPrivateIp
output publicIpAddress string = publicIp.outputs.ipAddress
output containerAppsEnvironmentName string = names.containerAppsEnvironment
output containerAppsEnvironmentId string = containerAppsEnvironment.outputs.id
output containerAppsDefaultDomain string = containerAppsEnvironment.outputs.defaultDomain
output containerAppsStaticIp string = containerAppsEnvironment.outputs.staticIp
