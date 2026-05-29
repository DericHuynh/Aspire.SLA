using Aspire.Hosting.ApplicationModel;

namespace Aspire.SLA.Models;

/// <summary>
/// Annotation that defines a dependency relationship between resources
/// for SLA graph traversal. Critical dependencies are treated as series
/// (multiplicative), non-critical as parallel/optional.
/// </summary>
public class SlaDependencyAnnotation : IResourceAnnotation
{
    /// <summary>
    /// Gets the downstream resource that this resource depends on.
    /// </summary>
    public IResource TargetResource { get; }

    /// <summary>
    /// Gets a value indicating whether the dependency is critical (series).
    /// When <c>true</c>, the dependency SLA is multiplied into the composite SLA.
    /// When <c>false</c>, the dependency is considered redundant / best-effort.
    /// </summary>
    public bool IsCritical { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SlaDependencyAnnotation"/> class.
    /// </summary>
    /// <param name="targetResource">The downstream resource this resource depends on.</param>
    /// <param name="isCritical">
    /// When <c>true</c> (default), the dependency is treated as a series/critical
    /// dependency and multiplied into the composite SLA. When <c>false</c>, the
    /// dependency is considered non-critical / parallel / best-effort.
    /// </param>
    public SlaDependencyAnnotation(IResource targetResource, bool isCritical = true)
    {
        TargetResource = targetResource;
        IsCritical = isCritical;
    }
}
