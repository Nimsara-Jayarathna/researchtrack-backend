// Infrastructure VM: Nginx, MySQL, Kafka, Prometheus, Grafana (Docker Compose).
// OS configuration is applied idempotently by scripts/configure-vm.sh through
// Azure Run Command, not customData, so it can be re-run on every deployment.

param vmName string
param nicName string
param location string
param vmSize string
param adminUsername string
param adminSshPublicKey string
param subnetId string
param privateIp string
param publicIpId string
param dataDiskId string

resource nic 'Microsoft.Network/networkInterfaces@2024-05-01' = {
  name: nicName
  location: location
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          subnet: {
            id: subnetId
          }
          privateIPAllocationMethod: 'Static'
          privateIPAddress: privateIp
          publicIPAddress: {
            id: publicIpId
          }
        }
      }
    ]
  }
}

resource vm 'Microsoft.Compute/virtualMachines@2024-07-01' = {
  name: vmName
  location: location
  properties: {
    hardwareProfile: {
      vmSize: vmSize
    }
    priority: 'Regular'
    osProfile: {
      computerName: vmName
      adminUsername: adminUsername
      linuxConfiguration: {
        disablePasswordAuthentication: true
        ssh: {
          publicKeys: [
            {
              path: '/home/${adminUsername}/.ssh/authorized_keys'
              keyData: adminSshPublicKey
            }
          ]
        }
        patchSettings: {
          patchMode: 'ImageDefault'
        }
      }
    }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: 'ubuntu-24_04-lts'
        sku: 'server'
        version: 'latest'
      }
      osDisk: {
        createOption: 'FromImage'
        deleteOption: 'Delete'
        managedDisk: {
          storageAccountType: 'StandardSSD_LRS'
        }
      }
      dataDisks: [
        {
          // configure-vm.sh finds the disk at /dev/disk/azure/scsi1/lun0.
          lun: 0
          createOption: 'Attach'
          deleteOption: 'Detach'
          caching: 'ReadOnly'
          managedDisk: {
            id: dataDiskId
          }
        }
      ]
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: nic.id
        }
      ]
    }
  }
}

output name string = vm.name
