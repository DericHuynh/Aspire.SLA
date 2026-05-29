using Aspire.Hosting.ApplicationModel;

namespace Aspire.SLA.Aws.Models;

/// <summary>
/// Annotation carrying AWS-specific configuration used for SLA resolution and cost estimation.
/// </summary>
public class AwsConfigAnnotation : IResourceAnnotation
{
    /// <summary>The AWS service name (e.g., "AmazonRDS", "AmazonECS").</summary>
    public string ServiceName { get; }

    /// <summary>The AWS instance type or SKU identifier.</summary>
    public string InstanceType { get; }

    /// <summary>The number of replicas (for parallel redundancy calculations).</summary>
    public int ReplicaCount { get; }

    /// <summary>The number of regions deployed to.</summary>
    public int RegionsCount { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AwsConfigAnnotation"/> class.
    /// </summary>
    /// <param name="serviceName">The AWS service name (e.g., "AmazonRDS", "AmazonECS").</param>
    /// <param name="instanceType">The AWS instance type or SKU identifier.</param>
    /// <param name="replicaCount">The number of replicas (for parallel redundancy calculations).</param>
    /// <param name="regionsCount">The number of regions deployed to.</param>
    public AwsConfigAnnotation(
        string serviceName,
        string instanceType,
        int replicaCount = 1,
        int regionsCount = 1)
    {
        ServiceName = serviceName;
        InstanceType = instanceType;
        ReplicaCount = replicaCount;
        RegionsCount = regionsCount;
    }
}
