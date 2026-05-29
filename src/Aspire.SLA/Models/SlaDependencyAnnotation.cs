using Aspire.Hosting.ApplicationModel;

namespace Aspire.SLA.Models;

public class SlaDependencyAnnotation : IResourceAnnotation
{
    public IResource TargetResource { get; }
    public bool IsCritical { get; } // True = Sequential (Series), False = Redundant (Parallel)

    public SlaDependencyAnnotation(IResource targetResource, bool isCritical = true)
    {
        TargetResource = targetResource;
        IsCritical = isCritical;
    }
}