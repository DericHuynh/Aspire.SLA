using Aspire.Hosting.ApplicationModel;
using Aspire.SLA.Models;

namespace Aspire.SLA;

/// <summary>
/// Fluent extension methods for attaching SLA annotations and dependency
/// relationships to .NET Aspire resources.
/// </summary>
public static class SlaExtensions
{
    /// <summary>
    /// Attaches a manual base SLA availability to a resource.
    /// </summary>
    /// <param name="availability">The base SLA as a fraction (e.g., 0.999 = 99.9%).</param>
    public static IResourceBuilder<T> WithSla<T>(this IResourceBuilder<T> builder, double availability)
        where T : IResource
    {
        builder.WithAnnotation(new SlaAnnotation(availability));
        return builder;
    }

    /// <summary>
    /// Defines a dependency relationship for SLA graph traversal.
    /// </summary>
    /// <param name="target">The downstream resource this resource depends on.</param>
    /// <param name="isCritical">
    /// When <c>true</c> (default), the dependency is treated as a series/critical
    /// dependency and multiplied into the composite SLA. When <c>false</c>, the
    /// dependency is considered non-critical / parallel / best-effort.
    /// </param>
    public static IResourceBuilder<T> WithSlaDependency<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<IResource> target,
        bool isCritical = true)
        where T : IResource
    {
        builder.WithAnnotation(new SlaDependencyAnnotation(target.Resource, isCritical));
        return builder;
    }
}
