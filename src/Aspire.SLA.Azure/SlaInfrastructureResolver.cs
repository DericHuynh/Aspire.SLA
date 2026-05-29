using System;
using System.Linq;
using Aspire.Hosting.ApplicationModel;
using Aspire.SLA.Models;
using Azure.Provisioning;
using Azure.Provisioning.Primitives;
using Azure.Provisioning.Redis;
using Azure.Provisioning.Storage;

namespace Aspire.SLA.Azure;

/// <summary>
/// An <see cref="InfrastructureResolver"/> that inspects Azure.Provisioning constructs
/// during compilation and attaches resolved SLA annotations to corresponding .NET Aspire
/// resources based on Microsoft-published SLA documentation.
/// </summary>
public class SlaInfrastructureResolver : InfrastructureResolver
{
    private readonly IResourceCollection _aspireResources;

    /// <summary>
    /// Initializes a new instance of <see cref="SlaInfrastructureResolver"/>.
    /// </summary>
    /// <param name="aspireResources">The collection of .NET Aspire resources to annotate with resolved SLAs.</param>
    public SlaInfrastructureResolver(IResourceCollection aspireResources)
    {
        _aspireResources = aspireResources;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "SLA diagnostic tool writes structured console output; localization not applicable.")]
    public override void ResolveProperties(ProvisionableConstruct construct, ProvisioningBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(construct);

        // Let the base resolver populate default properties and custom configurations first
        base.ResolveProperties(construct, options);

        double resolvedSla = 1.0; // Default: 100% base (no recognized SLA reduction yet)
        bool hasSla = false;
        string? constructName = null;

        // ── Azure Cache for Redis ──────────────────────────────────────
        if (construct is RedisResource cache)
        {
            constructName = cache.Name?.Value;

            var skuName = cache.Sku?.Name?.Value;
            if (skuName is not null)
            {
                var skuStr = skuName.Value.ToString();

                if (skuStr.Equals("Basic", StringComparison.OrdinalIgnoreCase))
                {
                    // Basic tier is not covered by Microsoft SLA → 0% SLA
                    resolvedSla = 0.0;
                    hasSla = true;

                    Console.WriteLine(
                        $"[SLA WARNING] Redis resource '{constructName}' is configured with 'Basic' SKU, " +
                        "which is not covered by the Microsoft SLA. Defaulting to 0% base SLA.");
                }
                else
                {
                    // Standard / Premium / Enterprise defaults to 99.9%
                    resolvedSla = 0.999;
                    hasSla = true;

                    // Premium with 3+ availability zones → 99.99%
                    if (skuStr.Equals("Premium", StringComparison.OrdinalIgnoreCase) && cache.Zones.Count >= 3)
                    {
                        resolvedSla = 0.9999;
                    }
                }
            }
        }

        // ── Azure Storage ──────────────────────────────────────────────
        else if (construct is StorageAccount storage)
        {
            constructName = storage.Name?.Value;

            var skuName = storage.Sku?.Name?.Value;
            if (skuName is not null)
            {
                var skuStr = skuName.Value.ToString();

                // Read Access Geo-Redundant Storage → 99.99% read SLA
                if (skuStr.Contains("RAGRS", StringComparison.OrdinalIgnoreCase) ||
                    skuStr.Contains("RAGZRS", StringComparison.OrdinalIgnoreCase))
                {
                    resolvedSla = 0.9999;
                }
                else
                {
                    resolvedSla = 0.999; // Standard LRS/GRS/ZRS write SLA → 99.9%
                }

                hasSla = true;
            }
        }

        // ── Azure SQL Database (type-name fallback for version compat) ─
        else if (construct.GetType().Name == "AzureSqlDatabase")
        {
            // Access Name via reflection since the type varies by Azure.Provisioning.Sql version
            var nameProp = construct.GetType().GetProperty("Name");
            if (nameProp is not null)
            {
                var nameValue = nameProp.GetValue(construct);
                if (nameValue is BicepValue<string> bicepName)
                    constructName = bicepName.Value;
            }

            resolvedSla = 0.9999; // Standard SQL DB SLA (99.99%)
            hasSla = true;
        }

        // ── Attach resolved SLA to the matching Aspire resource ────────
        if (hasSla && !string.IsNullOrEmpty(constructName))
        {
            var aspireResource = _aspireResources.FirstOrDefault(r =>
                r.Name.Equals(constructName, StringComparison.OrdinalIgnoreCase));

            if (aspireResource is not null)
            {
                // Remove any previous SLA annotations to avoid duplication
                var existing = aspireResource.Annotations.OfType<SlaAnnotation>().ToList();
                foreach (var old in existing)
                    aspireResource.Annotations.Remove(old);

                aspireResource.Annotations.Add(new SlaAnnotation(resolvedSla));
            }
        }
    }
}
