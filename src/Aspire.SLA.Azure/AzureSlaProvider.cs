using Aspire.Hosting.ApplicationModel;
using Aspire.SLA.Azure.Models;
using Aspire.SLA.Models;
using Aspire.SLA.Providers;

namespace Aspire.SLA.Azure;

/// <summary>
/// Resolves Azure service SLAs and estimates monthly run costs by querying the
/// Azure Retail Prices API via <see cref="AzureRetailPricesClient"/> and
/// cross-referencing the Microsoft-published SLA document via
/// <see cref="SlaDocumentParser"/>.
/// </summary>
/// <remarks>
/// <para>
/// The provider prefers the published SLA document when available and falls
/// back to manually attached <see cref="SlaAnnotation"/> values. If neither
/// source yields an SLA, the resource is treated as 0% (graceful degradation).
/// </para>
/// <para>
/// The default constructor creates a self-contained instance with its own
/// <see cref="AzureRetailPricesClient"/> and an empty SLA document.
/// Call <see cref="LoadSlaDocumentAsync"/> to populate the document from a
/// Microsoft SLA DOCX file.
/// </para>
/// </remarks>
public class AzureSlaProvider : ISlaProvider
{
    private readonly AzureRetailPricesClient _pricesClient;
    private AzureSlaDocument _slaDocument;

    /// <summary>
    /// Creates a provider with a default <see cref="AzureRetailPricesClient"/>
    /// and an empty SLA document. Call <see cref="LoadSlaDocumentAsync"/>
    /// to populate SLA mappings from a DOCX file.
    /// </summary>
    public AzureSlaProvider()
        : this(new AzureRetailPricesClient(), AzureSlaDocument.Empty)
    {
    }

    /// <summary>
    /// Creates a provider with the specified retail prices client and SLA document.
    /// </summary>
    /// <param name="pricesClient">Client used to query Azure Retail Prices API.</param>
    /// <param name="slaDocument">Pre-parsed SLA document, or <see cref="AzureSlaDocument.Empty"/>.</param>
    public AzureSlaProvider(AzureRetailPricesClient pricesClient, AzureSlaDocument slaDocument)
    {
        _pricesClient = pricesClient ?? throw new ArgumentNullException(nameof(pricesClient));
        _slaDocument = slaDocument ?? throw new ArgumentNullException(nameof(slaDocument));
    }

    /// <summary>
    /// Parses the SLA DOCX file at <paramref name="docxPath"/> and
    /// replaces the current SLA document with the result.
    /// </summary>
    /// <param name="docxPath">Path to a Microsoft SLA DOCX file.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async Task LoadSlaDocumentAsync(string docxPath, CancellationToken cancellationToken = default)
    {
        var parser = new SlaDocumentParser();
        _slaDocument = await parser.ParseAsync(docxPath, cancellationToken);
    }

    /// <inheritdoc />
    public bool CanHandle(IResource resource)
    {
        // Check type hierarchy for Azure-specific interfaces/types
        var typeName = resource.GetType().FullName ?? resource.GetType().Name;
        if (typeName.Contains("Azure", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check for Azure-specific configuration annotations (supports test stubs)
        if (resource.Annotations.OfType<AzureConfigAnnotation>().Any())
            return true;

        return false;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Resolution order:
    /// <list type="number">
    ///   <item>Published SLA document (via <see cref="AzureSlaDocument.LookupSla"/>)</item>
    ///   <item>Manually attached <see cref="SlaAnnotation"/></item>
    ///   <item>Graceful degradation to 0% SLA</item>
    /// </list>
    /// </remarks>
    public double GetBaseSla(IResource resource)
    {
        // ── 1. Try the published SLA document ───────────────────────────
        var config = resource.Annotations.OfType<AzureConfigAnnotation>().FirstOrDefault();
        if (config is not null &&
            _slaDocument.LookupSla(config.ServiceName, config.ArmSkuName) is { } docSla)
        {
            return docSla;
        }

        // ── 2. Fall back to manually attached SLA annotation ────────────
        var manual = resource.Annotations.OfType<SlaAnnotation>().FirstOrDefault();
        if (manual is not null)
            return manual.Availability;

        // ── 3. Unrecognized resource → 0% SLA ──────────────────────────
        Console.WriteLine(
            $"[SLA WARNING] Resource '{resource.Name}' has no SLA annotation and no recognized Azure SLA mapping. " +
            "Defaulting to 0% base SLA.");

        return 0.0;
    }

    /// <inheritdoc />
    public int GetReplicaCount(IResource resource)
    {
        var config = resource.Annotations
            .OfType<AzureConfigAnnotation>()
            .FirstOrDefault();

        return config?.ReplicaCount ?? 1;
    }

    /// <inheritdoc />
    public async Task<double> GetMonthlyCostAsync(IResource resource, string region)
    {
        var config = resource.Annotations
            .OfType<AzureConfigAnnotation>()
            .FirstOrDefault();

        if (config is null)
        {
            Console.WriteLine(
                $"[SLA INFO] Resource '{resource.Name}' has no AzureConfigAnnotation — skipping cost lookup.");
            return 0.0;
        }

        try
        {
            var prices = await _pricesClient.GetPricesByServiceAndSkuAsync(
                config.ServiceName, config.ArmSkuName, region);

            if (prices.Count > 0)
            {
                // Only count primary meter regions to avoid double-counting
                var primaryPrices = prices
                    .Where(p => p.IsPrimaryMeterRegion)
                    .ToList();

                var items = primaryPrices.Count > 0 ? primaryPrices : prices;

                double monthlyCost = items.Sum(p => p.RetailPrice * 730); // hours in a month
                Console.WriteLine(
                    $"[SLA INFO] Verified '{resource.Name}' -> {config.ServiceName} ({config.ArmSkuName}) " +
                    $"is active in '{region}'. Retail cost: ~${monthlyCost:F2}/mo.");
                return monthlyCost;
            }
            else
            {
                Console.WriteLine(
                    $"[SLA INFO] No pricing found for '{resource.Name}' -> " +
                    $"{config.ServiceName} ({config.ArmSkuName}) in '{region}'.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[SLA WARNING] Failed to query Azure Retail Prices API for '{resource.Name}': {ex.Message}");
        }

        return 0.0;
    }
}
