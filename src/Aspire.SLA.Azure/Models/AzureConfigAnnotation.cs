using Aspire.Hosting.ApplicationModel;

namespace Aspire.SLA.Azure.Models;

/// <summary>
/// An annotation that captures Azure resource configuration details used for
/// SLA resolution and Retail Prices API queries.
/// </summary>
public class AzureConfigAnnotation : IResourceAnnotation
{
    /// <summary>The Azure service tier / SKU family.</summary>
    public AzureSku Sku { get; }
    /// <summary>The deployment topology (single-region, multi-region, etc.).</summary>
    public DeploymentTopology Topology { get; }
    /// <summary>The number of replicas.</summary>
    public int ReplicaCount { get; }
    /// <summary>The number of regions the resource is deployed across.</summary>
    public int RegionsCount { get; }
    /// <summary>Whether active geo-replication is enabled.</summary>
    public bool ActiveGeoReplication { get; }

    /// <summary>The service name used as a Retail Prices API query parameter.</summary>
    public string ServiceName { get; }
    /// <summary>The ARM SKU name used as a Retail Prices API query parameter.</summary>
    public string ArmSkuName { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="AzureConfigAnnotation"/>.
    /// </summary>
    /// <param name="sku">The Azure SKU tier.</param>
    /// <param name="topology">The deployment topology.</param>
    /// <param name="serviceName">The Retail Prices API service name.</param>
    /// <param name="armSkuName">The Retail Prices API ARM SKU name.</param>
    /// <param name="replicaCount">The number of replicas (default 1).</param>
    /// <param name="regionsCount">The number of regions (default 1).</param>
    /// <param name="activeGeoReplication">Whether active geo-replication is enabled (default false).</param>
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
