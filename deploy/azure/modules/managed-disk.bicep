// Persistent data disk mounted at /data/researchtrack (MySQL, Kafka,
// Prometheus, Grafana). A separate resource so it outlives VM replacement.

param name string
param location string
param sizeGb int
param sku string

resource disk 'Microsoft.Compute/disks@2024-03-02' = {
  name: name
  location: location
  sku: {
    name: sku
  }
  properties: {
    creationData: {
      createOption: 'Empty'
    }
    diskSizeGB: sizeGb
  }
}

output id string = disk.id
