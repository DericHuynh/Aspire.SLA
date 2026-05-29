using Aspire.Hosting.ApplicationModel;

namespace Aspire.SLA.Azure.Models;

public class AzureConfigAnnotation : IResourceAnnotation
{
    public AzureSku Sku { get; }
    public DeploymentTopology Topology { get; }
    public int ReplicaCount { get; }
    public int RegionsCount { get; }
    public bool ActiveGeoReplication { get; }

    // Retail Prices API query parameters
    public string ServiceName { get; }
    public string ArmSkuName { get; }

    public AzureConfigAnnotation(
        AzureSku sku,
        DeploymentTopology topology,
        string serviceName,
        string armSkuName,
        int replicaCount = 1,
        int regionsCount = 1,
        bool activeGeoReplication = false)
    {
        Sku = sku;
        Topology = topology;
        ServiceName = serviceName;
        ArmSkuName = armSkuName;
        ReplicaCount = replicaCount;
        RegionsCount = regionsCount;
        ActiveGeoReplication = activeGeoReplication;
    }
}
