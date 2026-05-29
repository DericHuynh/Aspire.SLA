using System.Threading.Tasks;
using Aspire.Hosting.ApplicationModel;

namespace Aspire.SLA.Providers;

/// <summary>
/// Contract for cloud-specific SLA and cost providers.
/// Implementations resolve base SLAs, replica counts, and monthly
/// run costs for resources they handle.
/// </summary>
public interface ISlaProvider
{
    /// <summary>
    /// Evaluates if this provider knows how to calculate SLAs/Prices for this resource type.
    /// </summary>
    /// <param name="resource">The Aspire resource to evaluate.</param>
    /// <returns><c>true</c> if this provider can handle the resource; otherwise <c>false</c>.</returns>
    bool CanHandle(IResource resource);

    /// <summary>
    /// Resolves the nominal base SLA matching the cloud document guidelines.
    /// </summary>
    /// <param name="resource">The Aspire resource to resolve an SLA for.</param>
    /// <returns>The base SLA as a fraction (e.g., 0.999 = 99.9%).</returns>
    double GetBaseSla(IResource resource);

    /// <summary>
    /// Returns the number of replicas for this resource, used in parallel redundancy calculations.
    /// Defaults to 1 (no redundancy).
    /// </summary>
    /// <param name="resource">The Aspire resource to evaluate.</param>
    /// <returns>The replica count for parallel SLA calculations.</returns>
    int GetReplicaCount(IResource resource) => 1;

    /// <summary>
    /// Connects to the respective cloud API to verify existence and return running costs.
    /// </summary>
    /// <param name="resource">The Aspire resource to estimate cost for.</param>
    /// <param name="region">The cloud region for pricing queries.</param>
    /// <returns>The estimated monthly run cost in USD.</returns>
    Task<double> GetMonthlyCostAsync(IResource resource, string region);
}
