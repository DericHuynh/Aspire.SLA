using Aspire.SLA.Azure;

namespace Aspire.SLA.test;

/// <summary>Tests DOCX SLA document parsing against the actual Microsoft-published SLA document.</summary>
public sealed class SlaDocumentParserTests
{
    private static readonly string DocxPath = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "Aspire.SLA.Azure", "asset",
        "OnlineSvcsConsolidatedSLA(WW)(English)(April2026)CR.docx");

    private readonly AzureSlaDocument _document;

    /// <summary>Initializes the test by parsing the SLA DOCX document.</summary>
    public SlaDocumentParserTests()
    {
        _document = SlaDocumentParser.Parse(ResolveDocxPath());
    }

    [Fact]
    internal void Parse_Document_HasMinimumServiceCount()
    {
        Assert.True(_document.ServiceSlas.Count >= 100,
            $"Expected at least 100 services, got {_document.ServiceSlas.Count}");
    }

    [Fact]
    internal void Parse_ContainerInstances_ReturnsCorrectSla()
    {
        var tier = _document.LookupSla("Azure Container Instances", null);
        Assert.NotNull(tier);
        Assert.Equal(0.999, tier.UptimeSla, precision: 5);
    }

    [Fact]
    internal void Parse_ContainerApps_ReturnsCorrectSla()
    {
        var tier = _document.LookupSla("Azure Container Apps", null);
        Assert.NotNull(tier);
        Assert.Equal(0.9995, tier.UptimeSla, precision: 5);
    }

    [Fact]
    internal void Parse_Cdn_ReturnsCorrectSla()
    {
        var tier = _document.LookupSla("Content Delivery Network (CDN)", null);
        Assert.NotNull(tier);
        Assert.Equal(0.999, tier.UptimeSla, precision: 5);
    }

    [Fact]
    internal void Parse_CosmosDb_ReturnsCorrectSla()
    {
        var tier = _document.LookupSla("Azure Cosmos DB", null);
        Assert.NotNull(tier);
        Assert.Equal(0.9999, tier.UptimeSla, precision: 5);
    }

    [Theory]
    [InlineData("Azure Container Instances", "Container Group", 0.999)]
    [InlineData("Azure Container Apps", "Azure Container Apps service", 0.9995)]
    [InlineData("Content Delivery Network (CDN)", "CDN Service", 0.999)]
    [InlineData("Azure Cosmos DB", "Default", 0.9999)]
    internal void Parse_SpecificSkuLookup_ReturnsCorrectSla(string serviceName, string skuName, double expectedSla)
    {
        var tier = _document.LookupSla(serviceName, skuName);
        Assert.NotNull(tier);
        Assert.Equal(expectedSla, tier.UptimeSla, precision: 5);
    }

    [Fact]
    internal void Parse_LookupSla_UnknownService_ReturnsNull()
    {
        var tier = _document.LookupSla("NonExistent Service ZZZ", null);
        Assert.Null(tier);
    }

    [Fact]
    internal void Parse_MissingFile_ReturnsEmptyDocument()
    {
        var doc = SlaDocumentParser.Parse("/nonexistent/path/sla.docx");
        var tier = doc.LookupSla("Azure Container Instances", null);
        Assert.Null(tier);
    }

    [Fact]
    internal void Parse_FileNotFound_ReturnsEmptyDocument()
    {
        var doc = SlaDocumentParser.Parse("/tmp/does_not_exist_12345.docx");
        Assert.NotNull(doc);
        var tier = doc.LookupSla("Anything", null);
        Assert.Null(tier);
    }

    [Fact]
    internal void Parse_ContainerInstances_HasServiceCredits()
    {
        var tier = _document.LookupSla("Azure Container Instances", null);
        Assert.NotNull(tier);
        Assert.NotEmpty(tier.ServiceCredits);
        // Service credits should be in descending threshold order (99.9%, 99%, ...)
        Assert.True(tier.ServiceCredits[0].UptimeThreshold >= tier.ServiceCredits[^1].UptimeThreshold,
            "Service credits should be in descending uptime threshold order");
    }

    [Fact]
    internal void Parse_ServiceCredits_ContainValidValues()
    {
        var tier = _document.LookupSla("Azure Container Instances", null);
        Assert.NotNull(tier);
        foreach (var credit in tier.ServiceCredits)
        {
            Assert.True(credit.UptimeThreshold is >= 0.0 and <= 1.0,
                $"Invalid uptime threshold: {credit.UptimeThreshold}");
            Assert.True(credit.ServiceCredit is > 0.0 and <= 1.0,
                $"Invalid service credit: {credit.ServiceCredit}");
        }
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
