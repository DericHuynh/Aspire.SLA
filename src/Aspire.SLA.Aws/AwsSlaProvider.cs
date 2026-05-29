using Aspire.Hosting.ApplicationModel;
using Aspire.SLA.Models;
using Aspire.SLA.Providers;
using Aspire.SLA.Aws.Models;

namespace Aspire.SLA.Aws;

/// <summary>
/// Resolves AWS service SLAs and estimates monthly run costs.
/// Currently a stub that gracefully degrades to 0% SLA for unrecognized resources.
/// Extend this class with AWS Pricing API integration to enable full AWS SLA support.
/// </summary>
public class AwsSlaProvider : ISlaProvider
{
    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "SLA diagnostic tool writes structured console output; localization not applicable.")]
    public bool CanHandle(IResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        // Check type hierarchy for AWS-specific interfaces/types
        var typeName = resource.GetType().FullName ?? resource.GetType().Name;
        if (typeName.Contains("Aws", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check for AWS-specific configuration annotations (supports test stubs)
        if (resource.Annotations.OfType<AwsConfigAnnotation>().Any())
            return true;

        return false;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "SLA diagnostic tool writes structured console output; localization not applicable.")]
    public double GetBaseSla(IResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        // Check for a manually attached SLA annotation
        var manual = resource.Annotations.OfType<SlaAnnotation>().FirstOrDefault();
        if (manual is not null)
            return manual.Availability;

        // Stub: unrecognized AWS resources degrade to 0% SLA
        Console.WriteLine(
            $"[SLA WARNING] Resource '{resource.Name}' has no SLA annotation and no recognized AWS service mapping. " +
            "Defaulting to 0% base SLA.");

        return 0.0;
    }

    /// <inheritdoc />
    public int GetReplicaCount(IResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var config = resource.Annotations
            .OfType<AwsConfigAnnotation>()
            .FirstOrDefault();

        return config?.ReplicaCount ?? 1;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "SLA diagnostic tool writes structured console output; localization not applicable.")]
    public Task<double> GetMonthlyCostAsync(IResource resource, string region)
    {
        ArgumentNullException.ThrowIfNull(resource);

        // Stub: AWS Pricing API integration not yet implemented
        Console.WriteLine(
            $"[SLA INFO] AWS cost lookup not yet implemented for '{resource.Name}' — returning $0.00.");

        return Task.FromResult(0.0);
    }
}
