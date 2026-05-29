using System.Threading.Tasks;
using Aspire.Hosting.ApplicationModel;

namespace Aspire.SLA.Providers;

public interface ISlaProvider
{
    /// <summary>
    /// Evaluates if this provider knows how to calculate SLAs/Prices for this resource type.
    /// </summary>
    bool CanHandle(IResource resource);

    /// <summary>
    /// Resolves the nominal base SLA matching the cloud document guidelines.
    /// </summary>
    double GetBaseSla(IResource resource);

    /// <summary>
    /// Returns the number of replicas for this resource, used in parallel redundancy calculations.
    /// Defaults to 1 (no redundancy).
    /// </summary>
    int GetReplicaCount(IResource resource) => 1;

    /// <summary>
    /// Connects to the respective cloud API to verify existence and return running costs.
    /// </summary>
    Task<double> GetMonthlyCostAsync(IResource resource, string region);
}
