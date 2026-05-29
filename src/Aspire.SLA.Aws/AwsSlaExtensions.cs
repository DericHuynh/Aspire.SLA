using Aspire.Hosting.ApplicationModel;
using Aspire.SLA.Aws.Models;

namespace Aspire.SLA.Aws;

/// <summary>
/// Fluent extension methods for attaching AWS-specific SLA configuration
/// to .NET Aspire resources.
/// </summary>
public static class AwsSlaExtensions
{
    /// <summary>
    /// Attaches AWS-specific configuration (service name, instance type,
    /// replicas) used for SLA resolution and cost estimation.
    /// </summary>
    public static IResourceBuilder<T> WithAwsConfig<T>(
        this IResourceBuilder<T> builder,
        string serviceName,
        string instanceType,
        int replicaCount = 1,
        int regionsCount = 1)
        where T : IResource
    {
        builder.WithAnnotation(new AwsConfigAnnotation(
            serviceName, instanceType, replicaCount, regionsCount));
        return builder;
    }
}
