using Aspire.SLA.Azure;

namespace Aspire.SLA.test;

/// <summary>
/// Tests DOCX SLA document parsing against the actual Microsoft-published
/// SLA document in the asset folder.
/// </summary>
public class SlaDocumentParserTests
{
    private static readonly string DocxPath = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "Aspire.SLA.Azure", "asset",
        "OnlineSvcsConsolidatedSLA(WW)(English)(April2026)CR.docx");

    private readonly AzureSlaDocument _document;

    public SlaDocumentParserTests()
    {
        var parser = new SlaDocumentParser();
        _document = parser.Parse(ResolveDocxPath());
    }

    [Fact]
    public void Parse_Document_HasMinimumServiceCount()
    {
        // The real document has hundreds of services — ensure we got a meaningful number
        Assert.True(_document.ServiceSlas.Count >= 100,
            $"Expected at least 100 services, got {_document.ServiceSlas.Count}");
    }

    [Fact]
    public void Parse_ContainerInstances_ReturnsCorrectSla()
    {
        var sla = _document.LookupSla("Azure Container Instances", null);
        Assert.NotNull(sla);
        Assert.Equal(0.999, sla!.Value, precision: 5); // 99.9%
    }

    [Fact]
    public void Parse_ContainerApps_ReturnsCorrectSla()
    {
        var sla = _document.LookupSla("Azure Container Apps", null);
        Assert.NotNull(sla);
        Assert.Equal(0.9995, sla!.Value, precision: 5); // 99.95%
    }

    [Fact]
    public void Parse_Cdn_ReturnsCorrectSla()
    {
        var sla = _document.LookupSla("Content Delivery Network (CDN)", null);
        Assert.NotNull(sla);
        Assert.Equal(0.999, sla!.Value, precision: 5); // 99.9%
    }

    [Fact]
    public void Parse_CosmosDb_ReturnsCorrectSla()
    {
        // All Cosmos DB APIs share the same 99.99% SLA
        var sla = _document.LookupSla("Azure Cosmos DB", null);
        Assert.NotNull(sla);
        Assert.Equal(0.9999, sla!.Value, precision: 5); // 99.99%
    }

    [Theory]
    [InlineData("Azure Container Instances", "Container Group", 0.999)]
    [InlineData("Azure Container Apps", "Azure Container Apps service", 0.9995)]
    [InlineData("Content Delivery Network (CDN)", "CDN Service", 0.999)]
    [InlineData("Azure Cosmos DB", "Default", 0.9999)]
    public void Parse_SpecificSkuLookup_ReturnsCorrectSla(
        string serviceName, string skuName, double expectedSla)
    {
        var sla = _document.LookupSla(serviceName, skuName);
        Assert.NotNull(sla);
        Assert.Equal(expectedSla, sla!.Value, precision: 5);
    }

    [Fact]
    public void Parse_LookupSla_UnknownService_ReturnsNull()
    {
        var sla = _document.LookupSla("NonExistent Service ZZZ", null);
        Assert.Null(sla);
    }

    [Fact]
    public void Parse_MissingFile_ReturnsEmptyDocument()
    {
        var parser = new SlaDocumentParser();
        var doc = parser.Parse("/nonexistent/path/sla.docx");

        var sla = doc.LookupSla("Azure Container Instances", null);
        Assert.Null(sla);
    }

    [Fact]
    public void Parse_FileNotFound_ReturnsEmptyDocument()
    {
        var parser = new SlaDocumentParser();
        var doc = parser.Parse("/tmp/does_not_exist_12345.docx");

        Assert.NotNull(doc);
        var sla = doc.LookupSla("Anything", null);
        Assert.Null(sla);
    }

    /// <summary>
    /// Resolves the DOCX path by trying multiple relative base directories.
    /// This handles differences between IDE test runners and CLI.
    /// </summary>
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
