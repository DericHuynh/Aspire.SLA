using Aspire.Hosting.ApplicationModel;

namespace Aspire.SLA.Models;

/// <summary>
/// Annotation that attaches a manual base SLA availability to a resource.
/// Used when no cloud provider can automatically resolve the SLA.
/// </summary>
public class SlaAnnotation : IResourceAnnotation
{
    /// <summary>
    /// Gets the base SLA availability as a fraction (e.g., 0.999 = 99.9%).
    /// </summary>
    public double Availability { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SlaAnnotation"/> class.
    /// </summary>
    /// <param name="availability">The base SLA as a fraction (e.g., 0.999 = 99.9%).</param>
    public SlaAnnotation(double availability) => Availability = availability;
}
