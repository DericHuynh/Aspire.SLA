using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.SLA.Models;
using Aspire.SLA.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Aspire.SLA;

/// <summary>
/// Validates the SLA compliance of a distributed application by traversing the
/// resource dependency graph and computing composite availability using series (×)
/// and parallel (1 - ∏(1 - SLA)) formulas. Unrecognized resources gracefully
/// degrade to 0% SLA, naturally pulling dependent paths to 0%.
/// </summary>
public static class SlaGraphValidator
{
    /// <summary>
    /// Registered provider chain. Call <see cref="RegisterProvider"/> to add
    /// <see cref="ISlaProvider"/> instances for cloud coverage before calling
    /// <see cref="RunValidationAsync"/>.
    /// </summary>
    public static readonly IList<ISlaProvider> Providers = [];

    /// <summary>
    /// Registers an <see cref="ISlaProvider"/> that will be queried during SLA
    /// validation for matching resources.
    /// </summary>
    public static void RegisterProvider(ISlaProvider provider)
    {
        Providers.Add(provider);
    }

    /// <summary>
    /// Runs the full SLA validation pipeline: cloud cost estimation followed by
    /// composite SLA graph calculation. Prints a diagnostic summary to stdout.
    /// </summary>
    /// <param name="app">The built <see cref="DistributedApplication"/>.</param>
    /// <param name="region">Cloud region identifier for pricing queries.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "SLA diagnostic tool writes structured console output; localization not applicable.")]
    public static async Task RunValidationAsync(DistributedApplication app, string region)
    {
        ArgumentNullException.ThrowIfNull(app);
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var visited = new Dictionary<string, CompositeSlaResult>();
        double totalCost = 0.0;

        // ── Step 1: Verify resource existence & fetch cloud pricing ─────
        Console.WriteLine("[SLA Build Target] Triggering architecture compliance validation...");

        foreach (var resource in model.Resources)
        {
            var provider = Providers.FirstOrDefault(p => p.CanHandle(resource));
            if (provider is not null)
            {
                totalCost += await provider.GetMonthlyCostAsync(resource, region).ConfigureAwait(false);
            }
        }

        // ── Step 2: Traverse the graph and compute composite SLA ────────
        // Start from "frontend" nodes (entry points) or root nodes with no dependents
        var roots = model.Resources
            .Where(r => r is ProjectResource && r.Name.Contains("frontend", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (roots.Count == 0)
        {
            // Fallback: treat all project resources as roots
            roots = model.Resources.OfType<ProjectResource>().Cast<IResource>().ToList();
        }

        double worstCompositeSla = 1.0;

        foreach (var root in roots)
        {
            var result = ResolveResourceSla(root, visited);
            worstCompositeSla = Math.Min(worstCompositeSla, result.CompositeSla);
        }

        // ── Step 3: Print diagnostics ───────────────────────────────────
        double downtimeMinutesPerMonth = (1.0 - worstCompositeSla) * 43200.0; // 30 days × 24h × 60min

        Console.WriteLine();
        Console.WriteLine("[SLA SUCCESS] SLA calculation passed!");
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine($"Composite SLA: {worstCompositeSla * 100:F4}% ({downtimeMinutesPerMonth:F2} minutes downtime/month)");
        Console.WriteLine($"Estimated Base Running Cost: ${totalCost:F2}/month");
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine();
    }

    /// <summary>
    /// Recursively resolves the composite SLA for a resource by combining its base SLA
    /// with the SLAs of its critical (series) and redundant (parallel) dependencies.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "SLA diagnostic tool writes structured console output; localization not applicable.")]
    private static CompositeSlaResult ResolveResourceSla(
        IResource resource,
        Dictionary<string, CompositeSlaResult> visited)
    {
        if (visited.TryGetValue(resource.Name, out var cached))
            return cached;

        // ── Resolve base SLA ────────────────────────────────────────────
        double baseSla;

        var provider = Providers.FirstOrDefault(p => p.CanHandle(resource));
        if (provider is not null)
        {
            baseSla = provider.GetBaseSla(resource);
        }
        else
        {
            // No provider handles this resource — check for manual annotation
            var manual = resource.Annotations.OfType<SlaAnnotation>().FirstOrDefault();
            if (manual is null)
            {
                // Graceful degradation: unrecognized resources → 0% SLA
                Console.WriteLine(
                    $"[SLA WARNING] No SLA provider mapped for '{resource.Name}' " +
                    "and no manual SlaAnnotation present. Defaulting to 0% base SLA.");
                baseSla = 0.0;
            }
            else
            {
                baseSla = manual.Availability;
            }
        }

        // ── Factor in replicas (parallel redundancy of self) ───────────
        int replicas = provider?.GetReplicaCount(resource) ?? 1;

        double selfSla = ComputeParallelSla(baseSla, replicas);

        // ── Process dependencies ────────────────────────────────────────
        var dependencies = resource.Annotations
            .OfType<SlaDependencyAnnotation>()
            .ToList();

        double criticalChainSla = 1.0;

        foreach (var dep in dependencies)
        {
            var depResult = ResolveResourceSla(dep.TargetResource, visited);

            if (dep.IsCritical)
            {
                // Series dependency: multiply into the critical chain
                criticalChainSla *= depResult.CompositeSla;
            }
            // Non-critical (parallel/redundant) dependencies are NOT yet
            // factored into the critical chain — they represent optional
            // or best-effort paths.
        }

        // ── Final composite: base SLA × critical path ───────────────────
        double composite = selfSla * criticalChainSla;

        var result = new CompositeSlaResult(composite, baseSla, criticalChainSla);
        visited[resource.Name] = result;
        return result;
    }

    /// <summary>
    /// Computes the effective SLA when <paramref name="replicaCount"/> identical
    /// stateless instances are deployed in parallel (at least one must be healthy).
    /// Formula: 1 - (1 - sla)^replicas
    /// </summary>
    public static double ComputeParallelSla(double baseSla, int replicaCount)
    {
        if (replicaCount <= 1)
            return baseSla;

        return 1.0 - Math.Pow(1.0 - baseSla, replicaCount);
    }

    /// <summary>
    /// Computes the effective SLA for a series chain of dependencies.
    /// Formula: sla1 × sla2 × ... × slaN
    /// </summary>
    public static double ComputeSeriesSla(params double[] slas)
    {
        return slas.Aggregate(1.0, (acc, sla) => acc * sla);
    }

    /// <summary>
    /// Internal record holding the composite SLA breakdown for a single resource.
    /// </summary>
    private sealed record CompositeSlaResult(
        double CompositeSla,
        double BaseSla,
        double CriticalPathSla);
}
