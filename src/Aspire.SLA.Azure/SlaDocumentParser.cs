using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Aspire.SLA.Azure;

/// <summary>
/// Parses the Microsoft-published Azure SLA DOCX document to extract
/// service-level agreement percentages per service and tier/SKU.
/// </summary>
/// <remarks>
/// <para>
/// The parser uses a two-phase approach:
/// </para>
/// <list type="number">
///   <item>
///     <b>Phase 1 – Section headings</b>: Walk every paragraph looking for
///     service heading styles (<c>ProductList-Offering2Heading</c>) or known
///     Azure service name keywords. Each such paragraph sets the "current
///     service name" for tables that follow.
///   </item>
///   <item>
///     <b>Phase 2 – Table parsing</b>: For each table, find columns whose
///     headers contain SLA-related terms ("Uptime Percentage",
///     "Availability Percentage", etc.). The <em>first data row</em> in each
///     SLA column carries the guaranteed SLA threshold (e.g.,
///     <c>&lt; 99.95%</c>) — the parser strips the <c>&lt;</c> prefix and
///     converts it to a decimal fraction.  Service-credit columns (10%, 25%,
///     50%) are ignored because they fall below the 90% acceptance floor.
///   </item>
/// </list>
/// <para>
/// Tier / SKU names are inferred from the column header (for multi-tier
/// tables) or from the preceding <c>ProductList-Body</c> context paragraph
/// that describes the deployment configuration.
/// </para>
/// </remarks>
public sealed class SlaDocumentParser
{
    // ------------------------------------------------------------------
    //  Known Azure service keywords used to recognise section headings
    //  when the style-based heuristic doesn't fire.
    // ------------------------------------------------------------------
    private static readonly HashSet<string> AzureServiceKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Container Instances", "Container Apps", "Container Registry",
        "Cosmos DB", "CDN", "Content Delivery Network",
        "SQL Database", "SQL Managed Instance", "SQL Server",
        "Storage", "Storage Account", "Blob Storage",
        "Redis", "Cache for Redis", "Managed Redis",
        "Virtual Machines", "Virtual Machine",
        "App Service", "Functions", "Azure Functions",
        "Service Bus", "Event Hubs", "Event Grid",
        "Key Vault", "Managed HSM",
        "Application Gateway", "Load Balancer", "Front Door",
        "API Management", "API Center",
        "Cognitive Search", "AI Search", "AI Services",
        "Entra ID", "Entra Domain Services",
        "Active Directory", "Azure AD",
        "Cosmos DB for NoSQL", "Cosmos DB for MongoDB",
        "Cosmos DB for PostgreSQL", "Cosmos DB for Apache",
        "Cosmos DB for Table",
        "ExpressRoute", "VPN Gateway",
        "Data Factory", "Data Explorer", "Kusto",
        "Database for MySQL", "Database for MariaDB",
        "Database for PostgreSQL", "Cosmos DB",
        "Synapse", "Databricks", "HDInsight",
        "Analysis Services", "Azure Arc",
        "Backup", "Site Recovery", "Bastion",
        "Firewall", "DDoS Protection",
        "Monitor", "Application Insights", "Log Analytics",
        "Automation", "Batch", "Logic Apps",
        "Notification Hubs", "SignalR",
        "Communication Services", "Bot Service",
        "Cognitive Services", "Machine Learning",
        "IoT Hub", "Digital Twins",
        "Stream Analytics", "Event Hubs",
        "NetApp Files", "Azure Files", "Disk Storage", "Managed Disks",
        "DevOps", "Dev Center",
        "Container Apps", "Azure Kubernetes", "AKS",
    };

    /// <summary>
    /// Matches an SLA percentage like "99.9%", "99.99%", "99.999%", or
    /// "100%", optionally preceded by "&lt;" (threshold marker).
    /// Only accepts values in [90%, 100%] to exclude service-credit
    /// percentages (10%, 25%, 50%, etc.).
    /// </summary>
    private static readonly Regex SlaPercentRegex = new(
        @"(?:<\s*)?(?<percent>\d{2,3}(?:\.\d+)?)\s*%",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Paragraph styles that identify a service-level heading.
    /// </summary>
    private static readonly HashSet<string> HeadingStyles = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProductList-Offering2Heading",
        "ProductList-OfferingGroupHeading",
        "ProductList-SectionHeading",
        "Heading1", "Heading2", "Heading3",
    };

    // ------------------------------------------------------------------
    //  Public API
    // ------------------------------------------------------------------

    /// <summary>
    /// Parses the DOCX file at <paramref name="docxPath"/> and returns a
    /// structured <see cref="AzureSlaDocument"/> containing all extracted
    /// SLA mappings.
    /// </summary>
    public static Task<AzureSlaDocument> ParseAsync(
        string docxPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(docxPath))
            throw new ArgumentException("A non-empty DOCX path is required.", nameof(docxPath));

        return Task.Run(() => ParseInternal(docxPath), cancellationToken);
    }

    /// <summary>
    /// Synchronously parses the DOCX file.
    /// </summary>
    public static AzureSlaDocument Parse(string docxPath)
    {
        if (string.IsNullOrWhiteSpace(docxPath))
            throw new ArgumentException("A non-empty DOCX path is required.", nameof(docxPath));

        return ParseInternal(docxPath);
    }

    // ------------------------------------------------------------------
    //  Internal parsing
    // ------------------------------------------------------------------

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "SLA diagnostic tool writes structured console output; localization not applicable.")]
    private static AzureSlaDocument ParseInternal(string docxPath)
    {
        if (!File.Exists(docxPath))
        {
            Console.WriteLine(
                $"[SLA WARNING] SLA DOCX file not found at '{docxPath}'. " +
                "SLA document lookups will fall back to annotations.");
            return AzureSlaDocument.Empty;
        }

        var serviceSlas = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var document = WordprocessingDocument.Open(docxPath, false);
            var mainPart = document.MainDocumentPart;
            if (mainPart?.Document?.Body is not { } body)
            {
                Console.WriteLine("[SLA WARNING] SLA DOCX document body is empty.");
                return AzureSlaDocument.Empty;
            }

            // Resolve style definitions for heading detection by style name
            var styleNames = BuildStyleNameLookup(mainPart.StyleDefinitionsPart);

            // Phase 1 & 2: walk all body children in document order.
            string? currentService = null;
            string? lastContextParagraph = null;

            foreach (var child in body.Elements())
            {
                if (child is Paragraph para)
                {
                    var style = GetParagraphStyle(para);
                    var text = GetParagraphText(para).Trim();
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    // Resolve style name for richer heading detection
                    var styleName = style is not null && styleNames.TryGetValue(style, out var sn) ? sn : null;

                    // Detect service heading by style name/ID OR by Azure keyword match
                    if (IsServiceHeading(style, styleName, text))
                    {
                        currentService = NormalizeServiceName(text);
                        lastContextParagraph = null;
                    }
                    else
                    {
                        // Accumulate body-like paragraphs (including those
                        // without an explicit style) as potential context for
                        // the next table (tier / deployment description).
                        if (style is null ||
                            style.Length == 0 ||
                            style.Contains("Body", StringComparison.OrdinalIgnoreCase) ||
                            style.Contains("Normal", StringComparison.OrdinalIgnoreCase) ||
                            style.Contains("Clause", StringComparison.OrdinalIgnoreCase))
                        {
                            lastContextParagraph = text;
                        }
                    }
                }
                else if (child is Table table)
                {
                    if (currentService is null)
                        continue; // No service heading seen yet – skip TOC tables, etc.

                    var rows = table.Elements<TableRow>().ToList();
                    if (rows.Count < 2)
                        continue;

                    // --- Analyse header row ---
                    var headerCells = rows[0].Elements<TableCell>().ToList();
                    var headerTexts = headerCells
                        .Select(GetCellText)
                        .Select(t => t.Trim())
                        .ToList();

                    // Find SLA columns and service-credit columns
                    var slaColumns = new List<(int index, string header)>();
                    int? creditCol = null;

                    for (int i = 0; i < headerTexts.Count; i++)
                    {
                        var h = headerTexts[i];
                        if (IsSlaColumnHeader(h))
                            slaColumns.Add((i, h));
                        else if (IsCreditColumnHeader(h))
                            creditCol = i;
                    }

                    if (slaColumns.Count == 0)
                        continue; // No SLA data in this table

                    // For each SLA column, extract the SLA from the FIRST
                    // data row (the highest threshold = the actual SLA).
                    foreach (var (colIdx, colHeader) in slaColumns)
                    {
                        var firstDataCells = rows[1].Elements<TableCell>().ToList();
                        if (colIdx >= firstDataCells.Count)
                            continue;

                        var cellText = GetCellText(firstDataCells[colIdx]);
                        var slaValue = ExtractSlaPercentage(cellText);
                        if (slaValue is null)
                            continue;

                        // Determine the tier / SKU name
                        string tierName;
                        if (slaColumns.Count > 1)
                        {
                            // Multi-tier table – use column header as tier
                            tierName = NormalizeTierName(colHeader);
                        }
                        else
                        {
                            // Single-tier table – use preceding context paragraph
                            tierName = ExtractTierFromContext(lastContextParagraph);
                        }

                        if (!serviceSlas.TryGetValue(currentService, out var tiers))
                        {
                            tiers = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                            serviceSlas[currentService] = tiers;
                        }

                        // Prefer the first entry; multi-tier tables may have same service
                        tiers.TryAdd(tierName, slaValue.Value);
                    }
                }
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine(
                $"[SLA WARNING] Failed to read SLA DOCX file: {ex.Message}");
            return AzureSlaDocument.Empty;
        }
        catch (OpenXmlPackageException ex)
        {
            Console.WriteLine(
                $"[SLA WARNING] Failed to parse SLA DOCX file: {ex.Message}");
            return AzureSlaDocument.Empty;
        }

        // Freeze the inner dictionaries
        var frozen = serviceSlas.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyDictionary<string, double>)kvp.Value
                .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        Console.WriteLine(
            $"[SLA INFO] Parsed SLA document: {frozen.Count} services with SLA entries.");

        return new AzureSlaDocument(frozen);
    }

    // ------------------------------------------------------------------
    //  Paragraph helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Extracts the full text of a paragraph including nested fields and
    /// complex runs.
    /// </summary>
    private static string GetParagraphText(Paragraph para)
    {
        // Descendants<Text> covers runs inside fields, hyperlinks, etc.
        return string.Concat(para.Descendants<Text>().Select(t => t.Text));
    }

    /// <summary>
    /// Returns the style ID of the paragraph, or null if none.
    /// </summary>
    private static string? GetParagraphStyle(Paragraph para)
    {
        return para.ParagraphProperties?
            .ParagraphStyleId?
            .Val?
            .Value;
    }

    /// <summary>
    /// Builds a lookup from style ID to human-readable style name by reading
    /// the document's StyleDefinitionsPart via the Open XML SDK.
    /// </summary>
    private static Dictionary<string, string> BuildStyleNameLookup(StyleDefinitionsPart? stylesPart)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (stylesPart?.Styles is not { } styles)
            return lookup;

        foreach (var style in styles.Elements<Style>())
        {
            var id = style.StyleId?.Value;
            var name = style.StyleName?.Val?.Value;
            if (id is not null)
                lookup[id] = name ?? id;
        }

        return lookup;
    }

    /// <summary>
    /// Determines whether a paragraph represents a service-level heading.
    /// Matches by resolved style name (from the document's StyleDefinitionsPart),
    /// raw style ID, or by the presence of a known Azure service name keyword.
    /// </summary>
    private static bool IsServiceHeading(string? styleId, string? styleName, string text)
    {
        // Style-based detection — prefer the resolved style name from the document
        if (styleName is not null &&
            (styleName.Contains("Heading", StringComparison.OrdinalIgnoreCase) ||
             styleName.Contains("TOC", StringComparison.OrdinalIgnoreCase)))
        {
            return styleName.Contains("Heading", StringComparison.OrdinalIgnoreCase);
        }

        // Fall back to raw style ID detection
        if (styleId is not null && HeadingStyles.Contains(styleId))
            return true;

        // Keyword-based fallback – only for paragraphs that don't have a
        // known body/list style, to avoid matching definition sentences that
        // happen to mention an Azure service name.
        if (styleId is not null &&
            (styleId.Contains("Body", StringComparison.OrdinalIgnoreCase) ||
             styleId.Contains("List", StringComparison.OrdinalIgnoreCase) ||
             styleId.Contains("Normal", StringComparison.OrdinalIgnoreCase) ||
             styleId.Contains("Clause", StringComparison.OrdinalIgnoreCase)))
            return false;

        if (text.Length < 120 &&
            !text.StartsWith("Table of Contents", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("PAGEREF", StringComparison.Ordinal) &&
            !text.StartsWith('"') &&     // definition quotes
            !text.StartsWith('\u201c') && // left double quote
            !text.StartsWith('\u2018') && // left single quote
            !Regex.IsMatch(text,
                @"^\s*(?:Uptime\s+Calculation|Service\s+(?:Level|Credit)|The\s+following|There\s+are|This\s+SLA|Additional|Downtime)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            // Exclude sentences with explanatory colons like
            // "Open Container: Open and view a Cosmos DB container..."
            !Regex.IsMatch(text,
                @":\s+\w+\s+(?:and|or|to)\s",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            foreach (var kw in AzureServiceKeywords)
            {
                var idx = text.IndexOf(kw, StringComparison.OrdinalIgnoreCase);
                // Keyword must appear near the start of the text to be
                // considered a heading (not just mentioned in a sentence).
                if (idx >= 0 && idx < Math.Max(50, text.Length / 2))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Cleans up a service name extracted from a heading.
    /// </summary>
    private static string NormalizeServiceName(string raw)
    {
        // Remove trailing page-ref artefacts sometimes left by the TOC
        var cleaned = Regex.Replace(raw, @"\s*PAGEREF\s+.*$", "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return cleaned.Trim();
    }

    // ------------------------------------------------------------------
    //  Table / cell helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Extracts the plain text from a table cell, recursively traversing
    /// all paragraph and run elements including those inside nested
    /// structures.
    /// </summary>
    private static string GetCellText(TableCell cell)
    {
        return string.Concat(
            cell.Descendants<Text>().Select(t => t.Text));
    }

    /// <summary>
    /// Returns <c>true</c> when the column header text looks like it
    /// contains an SLA / uptime / availability percentage.
    /// </summary>
    private static bool IsSlaColumnHeader(string header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return false;

        return
            (header.Contains("Uptime", StringComparison.OrdinalIgnoreCase) ||
             header.Contains("Availability", StringComparison.OrdinalIgnoreCase) ||
             header.Contains("Attainment", StringComparison.OrdinalIgnoreCase) ||
             header.Contains("Conformance", StringComparison.OrdinalIgnoreCase) ||
             header.Contains("Throughput", StringComparison.OrdinalIgnoreCase) ||
             header.Contains("Readiness", StringComparison.OrdinalIgnoreCase) ||
             header.Contains("Successful", StringComparison.OrdinalIgnoreCase) ||
             header.Contains("Good Call", StringComparison.OrdinalIgnoreCase)) &&
            header.Contains("Percentage", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns <c>true</c> when the column header looks like a service
    /// credit column.
    /// </summary>
    private static bool IsCreditColumnHeader(string header)
    {
        return header.Contains("Credit", StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    //  SLA extraction
    // ------------------------------------------------------------------

    /// <summary>
    /// Attempts to extract an SLA percentage from cell text and convert it
    /// to a decimal fraction (e.g., "&lt; 99.9%" → 0.999).
    /// Only values in [90%, 100%] are accepted — this naturally filters out
    /// service-credit percentages (10%, 25%, 50%).
    /// </summary>
    /// <returns>The SLA as a decimal, or <c>null</c> if no valid percentage.</returns>
    private static double? ExtractSlaPercentage(string text)
    {
        // Find ALL percentage matches in the text and take the first that
        // satisfies the [90%, 100%] range.  The regex already strips the
        // optional "<" prefix.
        foreach (Match m in SlaPercentRegex.Matches(text))
        {
            if (!m.Success)
                continue;

            var raw = m.Groups["percent"].Value;
            if (!double.TryParse(raw,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var percent))
                continue;

            // Reject service-credit values (10%, 25%, 50%) and nonsensical values
            if (percent < 90.0 || percent > 100.0)
                continue;

            // Reject when the match is immediately preceded by "below" or ">"
            var prefix = text[..Math.Min(m.Index, text.Length)].TrimEnd();
            if (prefix.EndsWith("below", StringComparison.OrdinalIgnoreCase) ||
                prefix.EndsWith("above", StringComparison.OrdinalIgnoreCase) ||
                prefix.EndsWith('>'))
                continue;

            return percent / 100.0;
        }

        return null;
    }

    // ------------------------------------------------------------------
    //  Tier / SKU name extraction
    // ------------------------------------------------------------------

    /// <summary>
    /// Extracts a human-readable tier / SKU name from a context paragraph
    /// (the <c>ProductList-Body</c> paragraph that precedes the table and
    /// describes which deployment configuration the SLA applies to).
    /// </summary>
    private static string ExtractTierFromContext(string? context)
    {
        if (string.IsNullOrWhiteSpace(context))
            return "Default";

        // Common patterns in the Microsoft SLA document:
        //
        //   "... for the Basic and Standard tiers:"
        //   "... for the Premium or Dedicated tiers:"
        //   "... for Apps deployed across two or more Availability Zones..."
        //   "... for Cache deployed to three or more Availability Zones..."
        //   "... for Function App on the Consumption Plan"
        //   "... for the General Purpose, Business Critical, ... tiers..."
        //   "... for Apps that don't use Availability Zones:"

        // Pattern 1: "for <description> tier[s]:"
        var m1 = Regex.Match(context,
            @"\bfor\s+(.+?)\s+tiers?:",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (m1.Success)
            return NormalizeTierName(m1.Groups[1].Value);

        // Pattern 2: "for <description> deployed / configured"
        var m2 = Regex.Match(context,
            @"\bfor\s+(.+?)\s+(?:deployed|configured|scaled)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (m2.Success)
            return NormalizeTierName(m2.Groups[1].Value);

        // Pattern 3: "for <Function App|App Service|etc> on the <Plan>"
        var m3 = Regex.Match(context,
            @"\bfor\s+(?:Function\s*App|App\s*Service|Azure\s+)?\s*(?:Apps?\s+)?on\s+the\s+(.+?)(?:\s*\.|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (m3.Success)
            return NormalizeTierName(m3.Groups[1].Value);

        // Pattern 4: "Service Levels ... applicable to Customer's use of ..."
        var m4 = Regex.Match(context,
            @"applicable\s+to\s+Customer[’']s\s+use\s+of\s+(.+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (m4.Success)
        {
            var extracted = m4.Groups[1].Value.Trim().TrimEnd(':', '.');
            // If the extracted text refers to multiple tiers like
            // "the Basic, Standard, or Premium tiers", clean it up
            extracted = Regex.Replace(extracted,
                @"\bthe\s+", "", RegexOptions.IgnoreCase);
            extracted = Regex.Replace(extracted,
                @"\s+tiers?\s*$", "", RegexOptions.IgnoreCase);
            if (extracted.Length > 0)
                return NormalizeTierName(extracted);
        }

        // Pattern 5: "Uptime Calculation and Service Levels for ..."
        var m5 = Regex.Match(context,
            @"\b(?:Uptime\s+Calculation\s+and\s+)?Service\s+Levels?\s+for\s+(.+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (m5.Success)
            return NormalizeTierName(m5.Groups[1].Value.TrimEnd(':', '.'));

        return "Default";
    }

    /// <summary>
    /// Trims and normalises a tier name to a short, readable form.
    /// </summary>
    private static string NormalizeTierName(string raw)
    {
        var cleaned = raw.Trim().TrimEnd(':', '.');

        // Collapse whitespace
        cleaned = Regex.Replace(cleaned, @"\s+", " ");

        // If it's very long, just keep it as-is (callers have already
        // extracted the most relevant portion).
        if (cleaned.Length > 80)
            cleaned = cleaned[..80];

        // Fallback for empty after trimming
        if (string.IsNullOrWhiteSpace(cleaned))
            return "Default";

        return cleaned;
    }
}

/// <summary>
/// Holds parsed SLA information extracted from a Microsoft Azure SLA DOCX
/// document, keyed by service name and tier/SKU.
/// </summary>
public sealed class AzureSlaDocument
{
    /// <summary>
    /// A singleton empty document with no SLA entries.
    /// </summary>
    public static readonly AzureSlaDocument Empty = new(
        new Dictionary<string, IReadOnlyDictionary<string, double>>());

    /// <summary>
    /// Initializes a new instance with the given SLA mappings.
    /// </summary>
    /// <param name="serviceSlas">
    /// A dictionary where the outer key is the service name and the inner
    /// key is the tier/SKU name, with the value being the SLA as a decimal
    /// fraction (e.g., 0.999 for 99.9%).
    /// </param>
    public AzureSlaDocument(IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> serviceSlas)
    {
        ServiceSlas = serviceSlas ?? throw new ArgumentNullException(nameof(serviceSlas));
    }

    /// <summary>
    /// Nested SLA map: outer key = service name,
    /// inner key = tier/SKU name, value = SLA as a decimal (e.g., 0.999).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> ServiceSlas { get; }

    /// <summary>
    /// Looks up the SLA for a given service and optional tier/SKU.
    /// Returns <c>null</c> when no match is found.
    /// </summary>
    /// <param name="serviceName">The Azure service name.</param>
    /// <param name="skuOrTier">
    /// The tier or SKU name. Pass <c>"Default"</c> or <c>null</c> to match
    /// the fallback tier.
    /// </param>
    /// <returns>The SLA as a decimal fraction, or <c>null</c>.</returns>
    public double? LookupSla(string serviceName, string? skuOrTier)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return null;

        if (!ServiceSlas.TryGetValue(serviceName, out var skus))
            return null;

        // Null or empty tier → return the first available SLA
        if (string.IsNullOrWhiteSpace(skuOrTier))
            return skus.Count > 0 ? skus.First().Value : null;

        var key = skuOrTier;

        if (skus.TryGetValue(key, out var sla))
            return sla;

        // Specific tier not found — fall back to first available SLA
        return skus.First().Value;
    }
}
