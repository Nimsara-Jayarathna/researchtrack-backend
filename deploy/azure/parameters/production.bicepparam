using '../main.bicep'

// Non-secret Production parameters. Never put passwords or private keys here.

param environmentName = 'prod'
param azureLocation = readEnvironmentVariable('AZURE_LOCATION', 'southeastasia')

param vnetCidr = '10.20.0.0/16'
param infraSubnetCidr = '10.20.10.0/24'
param containerAppsSubnetCidr = '10.20.20.0/23'
param vmPrivateIp = '10.20.10.4'

param vmSize = readEnvironmentVariable('AZURE_VM_SIZE', 'Standard_B2s')
param dataDiskSizeGb = 64
param dataDiskSku = 'StandardSSD_LRS'

// A public key (not secret). Required by Azure for Linux VMs; no NSG rule
// allows SSH, so it is only usable through Azure Serial Console/Bastion.
param vmAdminSshPublicKey = readEnvironmentVariable('AZURE_VM_ADMIN_SSH_PUBLIC_KEY', '')

param enableLogAnalytics = true
