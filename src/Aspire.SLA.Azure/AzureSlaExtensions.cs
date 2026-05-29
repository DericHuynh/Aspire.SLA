using System;
using Aspire.Hosting.ApplicationModel;
using Aspire.SLA.Azure.Models;

namespace Aspire.SLA.Azure;

/// <summary>
/// Fluent extension methods for attaching Azure-specific SLA configuration
/// to .NET Aspire resources.
/// </summary>
public static class AzureSlaExtensions
{
    /// <summary>
    /// Attaches Azure-specific configuration (SKU, topology, replica count,
    /// pricing metadata) used for SLA resolution and cost estimation.
    /// </summary>
    public static IResourceBuilder<T> WithAzureConfig<T>(
        this IResourceBuilder<T> builder,
        AzureSku sku,
        DeploymentTopology topology,
        string serviceName,
        string armSkuName,
        int replicaCount = 1,
        int regionsCount = 1,
        bool activeGeoReplication = false)
        where T : IResource
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WithAnnotation(new AzureConfigAnnotation(
            sku, topology, serviceName, armSkuName,
            replicaCount, regionsCount, activeGeoReplication));
        return builder;
    }
}
