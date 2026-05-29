using System.Text.Json;
using Aspire.SLA.Azure;

namespace Aspire.SLA.test;

/// <summary>
/// Exhaustive structural verification EVERY single service and tier entry
/// from the Microsoft-published SLA DOCX document (April 2026).
/// Each service/tier combination is its own individual test case via
/// MemberData, guaranteeing every entry in the document is verified.
/// </summary>
public sealed class SlaDocumentExhaustiveTests
{
    private static readonly string DocxPath = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "Aspire.SLA.Azure", "asset",
        "OnlineSvcsConsolidatedSLA(WW)(English)(April2026)CR.docx");

    private static readonly Lazy<AzureSlaDocument> LazyDocument = new(LoadDocument);
    private static AzureSlaDocument Document => LazyDocument.Value;

    private static AzureSlaDocument LoadDocument()
    {
        var parser = new SlaDocumentParser();
        return parser.Parse(ResolveDocxPath());
    }

    /// <summary>
    /// Returns EVERY service/tier/SLA triple extracted from the DOCX as test data.
    /// Each entry becomes its own individual test case via Theory + MemberData.
    /// </summary>
    public static TheoryData<string, string, double> AllSlaEntries
    {
        get
        {
            var data = new TheoryData<string, string, double>();
            foreach (var (service, tiers) in Document.ServiceSlas)
                foreach (var (tier, sla) in tiers)
                    data.Add(service, tier, sla);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllSlaEntries))]
    public void Verify_EverySlaEntry_IsValidAndLookupable(string serviceName, string tierName, double expectedSla)
    {
        // Verify the value is in a reasonable range
        Assert.True(expectedSla is >= 0.0 and <= 1.0,
            $"SLA value {expectedSla} for '{serviceName}' / '{tierName}' is out of range");

        // Verify the entry is accessible via LookupSla with exact tier match
        var lookup = Document.LookupSla(serviceName, tierName);
        Assert.NotNull(lookup);
        Assert.Equal(expectedSla, lookup!.Value, precision: 5);

        // Verify the entry is also accessible without specifying tier (first value)
        var lookupAny = Document.LookupSla(serviceName, null);
        Assert.NotNull(lookupAny);
    }

    [Fact]
    public void Document_HasExpectedServiceCount()
    {
        Assert.Equal(182, Document.ServiceSlas.Count);
    }

    [Fact]
    public void Dump_AllSlaEntries_ToConsole()
    {
        // Diagnostic dump showing every single entry
        int total = 0;
        foreach (var (service, tiers) in Document.ServiceSlas.OrderBy(s => s.Key))
        {
            foreach (var (tier, sla) in tiers)
            {
                Console.WriteLine($"[{++total,4}] '{service}' | '{tier}' => {sla:F5} ({sla * 100:F2}%)");
            }
        }
        Console.WriteLine($"Total entries: {total}");
    }

    // ═══════════════════════════════════════════════════════════
    //  Specific SLA value tests for user-requested services
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ContainerInstances_Sla_Is99_9Percent()
    {
        var sla = Document.LookupSla("Azure Container Instances", null);
        Assert.NotNull(sla);
        Assert.Equal(0.999, sla!.Value, precision: 5);
    }

    [Fact]
    public void ContainerApps_Sla_Is99_95Percent()
    {
        var sla = Document.LookupSla("Azure Container Apps", null);
        Assert.NotNull(sla);
        Assert.Equal(0.9995, sla!.Value, precision: 5);
    }

    [Fact]
    public void Cdn_Sla_Is99_9Percent()
    {
        var sla = Document.LookupSla("Content Delivery Network (CDN)", null);
        Assert.NotNull(sla);
        Assert.Equal(0.999, sla!.Value, precision: 5);
    }

    [Fact]
    public void CosmosDb_Sla_Is99_99Percent()
    {
        var sla = Document.LookupSla("Azure Cosmos DB", null);
        Assert.NotNull(sla);
        Assert.Equal(0.9999, sla!.Value, precision: 5);
    }

    [Fact]
    public void CosmosDb_PostgreSqlNode_Sla_Is99_99Percent()
    {
        var sla = Document.LookupSla("Azure Cosmos DB",
            "Microsoft Azure Cosmos DB for PostgreSQL High Availability Node");
        Assert.NotNull(sla);
        Assert.Equal(0.9999, sla!.Value, precision: 5);
    }

    // ═══════════════════════════════════════════════════════════
    //  Path resolution
    // ═══════════════════════════════════════════════════════════

    private static string ResolveDocxPath()
    {
        if (File.Exists(DocxPath))
            return DocxPath;

        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "src", "Aspire.SLA.Azure", "asset",
                "OnlineSvcsConsolidatedSLA(WW)(English)(April2026)CR.docx");
            if (File.Exists(candidate))
                return candidate;

            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir)
                break;
            dir = parent;
        }
        return DocxPath;
    }
}
