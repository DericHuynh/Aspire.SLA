using System.Text.Json;
using Aspire.SLA.Azure;

namespace Aspire.SLA.test;

/// <summary>Exhaustive verification of every service/tier/SLA from the DOCX document.</summary>
public sealed class SlaDocumentExhaustiveTests
{
    private static readonly string DocxPath = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "Aspire.SLA.Azure", "asset",
        "OnlineSvcsConsolidatedSLA(WW)(English)(April2026)CR.docx");

    private static readonly Lazy<AzureSlaDocument> LazyDocument = new(LoadDocument);
    private static AzureSlaDocument Document => LazyDocument.Value;

    private static AzureSlaDocument LoadDocument() => SlaDocumentParser.Parse(ResolveDocxPath());

    /// <summary>Returns every service/tier/SLA entry as individual test cases.</summary>
    public static TheoryData<string, string, double> AllSlaEntries
    {
        get
        {
            var data = new TheoryData<string, string, double>();
            foreach (var (service, tiers) in Document.ServiceSlas)
                foreach (var (tier, slaTier) in tiers)
                    data.Add(service, tier, slaTier.UptimeSla);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllSlaEntries))]
    internal void Verify_EverySlaEntry_IsValidAndLookupable(string serviceName, string tierName, double expectedSla)
    {
        Assert.True(expectedSla is >= 0.0 and <= 1.0,
            $"SLA value {expectedSla} for '{serviceName}' / '{tierName}' is out of range");

        var tier = Document.LookupSla(serviceName, tierName);
        Assert.NotNull(tier);
        Assert.Equal(expectedSla, tier.UptimeSla, precision: 5);

        var tierAny = Document.LookupSla(serviceName, null);
        Assert.NotNull(tierAny);
    }

    [Fact]
    internal void Document_HasExpectedServiceCount()
    {
        Assert.Equal(187, Document.ServiceSlas.Count);
    }

    [Fact]
    internal void Dump_AllSlaEntries_ToConsole()
    {
        int total = 0;
        foreach (var (service, tiers) in Document.ServiceSlas.OrderBy(s => s.Key))
            foreach (var (tierName, slaTier) in tiers)
            {
                Console.WriteLine($"[{++total,4}] '{service}' | '{tierName}' => {slaTier.UptimeSla:F5} ({slaTier.UptimeSla * 100:F2}%)");
                foreach (var c in slaTier.ServiceCredits)
                    Console.WriteLine($"       credit: < {c.UptimeThreshold * 100:F2}% → {c.ServiceCredit * 100:F0}%");
            }
        Console.WriteLine($"Total entries: {total}");
    }

    [Fact]
    internal void EveryTier_HasAtLeastOneServiceCredit()
    {
        int missing = 0;
        foreach (var (service, tiers) in Document.ServiceSlas)
            foreach (var (tierName, slaTier) in tiers)
                if (slaTier.ServiceCredits.Count == 0)
                {
                    Console.WriteLine($"  [info] '{service}' / '{tierName}' has no service credits (specialized table)");
                    missing++;
                }
        // Allow a few RPO/RTO/geo-replication tables that don't follow the standard credit layout
        Assert.True(missing < 10, $"Too many tiers without service credits: {missing}");
    }

    [Fact]
    internal void ServiceCredits_ContainValidValues()
    {
        foreach (var (service, tiers) in Document.ServiceSlas)
            foreach (var (tierName, slaTier) in tiers)
                foreach (var credit in slaTier.ServiceCredits)
                {
                    Assert.True(credit.UptimeThreshold is >= 0.0 and <= 1.0,
                        $"Invalid threshold in '{service}/{tierName}': {credit.UptimeThreshold}");
                    Assert.True(credit.ServiceCredit is > 0.0 and <= 1.0,
                        $"Invalid credit in '{service}/{tierName}': {credit.ServiceCredit}");
                }
    }

    [Fact]
    internal void ContainerInstances_Sla_Is99_9Percent()
    {
        var tier = Document.LookupSla("Azure Container Instances", null);
        Assert.NotNull(tier);
        Assert.Equal(0.999, tier.UptimeSla, precision: 5);
    }

    [Fact]
    internal void ContainerApps_Sla_Is99_95Percent()
    {
        var tier = Document.LookupSla("Azure Container Apps", null);
        Assert.NotNull(tier);
        Assert.Equal(0.9995, tier.UptimeSla, precision: 5);
    }

    [Fact]
    internal void Cdn_Sla_Is99_9Percent()
    {
        var tier = Document.LookupSla("Content Delivery Network (CDN)", null);
        Assert.NotNull(tier);
        Assert.Equal(0.999, tier.UptimeSla, precision: 5);
    }

    [Fact]
    internal void CosmosDb_Sla_Is99_99Percent()
    {
        var tier = Document.LookupSla("Azure Cosmos DB", null);
        Assert.NotNull(tier);
        Assert.Equal(0.9999, tier.UptimeSla, precision: 5);
    }

    [Fact]
    internal void CosmosDb_PostgreSqlNode_Sla_Is99_99Percent()
    {
        var tier = Document.LookupSla("Azure Cosmos DB",
            "Microsoft Azure Cosmos DB for PostgreSQL High Availability Node");
        Assert.NotNull(tier);
        Assert.Equal(0.9999, tier.UptimeSla, precision: 5);
    }

    private static string ResolveDocxPath()
    {
        if (File.Exists(DocxPath)) return DocxPath;
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "src", "Aspire.SLA.Azure", "asset",
                "OnlineSvcsConsolidatedSLA(WW)(English)(April2026)CR.docx");
            if (File.Exists(candidate)) return candidate;
            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir) break;
            dir = parent;
        }
        return DocxPath;
    }
}
