using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Aspire.SLA.Azure;

/// <summary>
/// OData client for querying the Azure Retail Prices API
/// (<c>https://prices.azure.com/api/retail/prices</c>).
/// Supports filter expressions and transparent pagination via
/// <c>NextPageLink</c>.
/// </summary>
public sealed class AzureRetailPricesClient : IDisposable
{
    private const string BaseUrl = "https://prices.azure.com/api/retail/prices";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Creates a new client using an internal <see cref="HttpClient"/>.
    /// </summary>
    public AzureRetailPricesClient()
    {
        _httpClient = new HttpClient();
        _ownsHttpClient = true;
    }

    /// <summary>
    /// Creates a new client using the supplied <see cref="HttpClient"/>.
    /// The caller retains ownership and is responsible for disposal.
    /// </summary>
    /// <param name="httpClient">An externally managed <see cref="HttpClient"/>.</param>
    public AzureRetailPricesClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = false;
    }

    /// <summary>
    /// Queries all pages of retail prices matching the given OData
    /// <paramref name="filter"/> expression.
    /// </summary>
    /// <param name="filter">
    /// A valid OData <c>$filter</c> expression (e.g.,
    /// <c>serviceName eq 'Virtual Machines' and armSkuName eq 'Standard_D14' and armRegionName eq 'eastus'</c>).
    /// The expression will be URL-encoded.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// A read-only list of all matching <see cref="RetailPriceItem"/> records
    /// across every page of results. Returns an empty list when the API is
    /// unavailable or returns no items.
    /// </returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "SLA diagnostic tool writes structured console output; localization not applicable.")]
    public async Task<IReadOnlyList<RetailPriceItem>> QueryPricesAsync(
        string filter,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filter))
            throw new ArgumentException("A non-empty OData $filter is required.", nameof(filter));

        var allItems = new List<RetailPriceItem>();
        string? nextPageLink = BuildInitialUrl(filter);

        while (nextPageLink is not null && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var response = await _httpClient.GetAsync(new Uri(nextPageLink), cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine(
                        $"[SLA WARNING] Azure Retail Prices API returned {response.StatusCode} " +
                        $"for filter '{filter}'. Stopping pagination.");
                    break;
                }

                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var page = JsonSerializer.Deserialize<RetailPricesResponse>(content, JsonOptions);

                if (page?.Items is { Count: > 0 })
                    allItems.AddRange(page.Items);

                nextPageLink = page?.NextPageLink;
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine(
                    $"[SLA WARNING] Failed to query Azure Retail Prices API: {ex.Message}");
                break;
            }
            catch (JsonException ex)
            {
                Console.WriteLine(
                    $"[SLA WARNING] Failed to deserialize Azure Retail Prices API response: {ex.Message}");
                break;
            }
        }

        return allItems.AsReadOnly();
    }

    /// <summary>
    /// Convenience method that builds an OData filter for a specific
    /// service, SKU, and region, then returns all matching retail prices.
    /// </summary>
    /// <param name="serviceName">The Azure service name (e.g., "Virtual Machines").</param>
    /// <param name="armSkuName">The ARM SKU name (e.g., "Standard_D14").</param>
    /// <param name="region">The Azure region (e.g., "eastus").</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// A read-only list of matching <see cref="RetailPriceItem"/> records.
    /// </returns>
    public Task<IReadOnlyList<RetailPriceItem>> GetPricesByServiceAndSkuAsync(
        string serviceName,
        string armSkuName,
        string region,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name is required.", nameof(serviceName));
        if (string.IsNullOrWhiteSpace(armSkuName))
            throw new ArgumentException("ARM SKU name is required.", nameof(armSkuName));
        if (string.IsNullOrWhiteSpace(region))
            throw new ArgumentException("Region is required.", nameof(region));

        var filter = $"serviceName eq '{EscapeODataString(serviceName)}' and armSkuName eq '{EscapeODataString(armSkuName)}' and armRegionName eq '{EscapeODataString(region)}'";
        return QueryPricesAsync(filter, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private static string BuildInitialUrl(string filter)
    {
        return $"{BaseUrl}?$filter={Uri.EscapeDataString(filter)}";
    }

    /// <summary>
    /// Escapes single quotes in a string value for safe embedding in an
    /// OData filter expression.
    /// </summary>
    private static string EscapeODataString(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}

/// <summary>
/// Top-level response envelope from the Azure Retail Prices API.
/// </summary>
public sealed class RetailPricesResponse
{
    /// <summary>Billing currency (e.g., "USD").</summary>
    [JsonPropertyName("BillingCurrency")]
    public string? BillingCurrency { get; set; }

    /// <summary>Customer entity identifier.</summary>
    [JsonPropertyName("CustomerEntityId")]
    public string? CustomerEntityId { get; set; }

    /// <summary>Customer entity type (e.g., "Retail").</summary>
    [JsonPropertyName("CustomerEntityType")]
    public string? CustomerEntityType { get; set; }

    /// <summary>The array of retail price items in the current page.</summary>
    [JsonPropertyName("Items")]
    public Collection<RetailPriceItem> Items { get; } = [];

    /// <summary>
    /// Absolute URL for the next page of results, or <c>null</c> when
    /// the final page has been reached.
    /// </summary>
    [JsonPropertyName("NextPageLink")]
    public string? NextPageLink { get; set; }

    /// <summary>Total number of items across all pages.</summary>
    [JsonPropertyName("Count")]
    public int Count { get; set; }
}

/// <summary>
/// Represents a single retail price entry from the Azure Retail Prices API.
/// </summary>
public sealed class RetailPriceItem
{
    /// <summary>Currency code (e.g., "USD").</summary>
    [JsonPropertyName("currencyCode")]
    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>Minimum units required for this pricing tier.</summary>
    [JsonPropertyName("tierMinimumUnits")]
    public double TierMinimumUnits { get; set; }

    /// <summary>Retail price per unit.</summary>
    [JsonPropertyName("retailPrice")]
    public double RetailPrice { get; set; }

    /// <summary>Unit price.</summary>
    [JsonPropertyName("unitPrice")]
    public double UnitPrice { get; set; }

    /// <summary>Azure region ARM name (e.g., "eastus").</summary>
    [JsonPropertyName("armRegionName")]
    public string ArmRegionName { get; set; } = string.Empty;

    /// <summary>Human-readable location name (e.g., "East US").</summary>
    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;

    /// <summary>Date the pricing became effective.</summary>
    [JsonPropertyName("effectiveStartDate")]
    public DateTime EffectiveStartDate { get; set; }

    /// <summary>Unique meter identifier.</summary>
    [JsonPropertyName("meterId")]
    public string MeterId { get; set; } = string.Empty;

    /// <summary>Human-readable meter name.</summary>
    [JsonPropertyName("meterName")]
    public string MeterName { get; set; } = string.Empty;

    /// <summary>Product identifier.</summary>
    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = string.Empty;

    /// <summary>SKU identifier.</summary>
    [JsonPropertyName("skuId")]
    public string SkuId { get; set; } = string.Empty;

    /// <summary>Human-readable product name.</summary>
    [JsonPropertyName("productName")]
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Human-readable SKU name.</summary>
    [JsonPropertyName("skuName")]
    public string SkuName { get; set; } = string.Empty;

    /// <summary>Azure service name (e.g., "Virtual Machines").</summary>
    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Service identifier.</summary>
    [JsonPropertyName("serviceId")]
    public string ServiceId { get; set; } = string.Empty;

    /// <summary>Service family classification (e.g., "Compute").</summary>
    [JsonPropertyName("serviceFamily")]
    public string ServiceFamily { get; set; } = string.Empty;

    /// <summary>Unit of measure (e.g., "1 Hour").</summary>
    [JsonPropertyName("unitOfMeasure")]
    public string UnitOfMeasure { get; set; } = string.Empty;

    /// <summary>Price type (e.g., "Consumption", "DevTestConsumption").</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>Whether this is the primary meter for the region.</summary>
    [JsonPropertyName("isPrimaryMeterRegion")]
    public bool IsPrimaryMeterRegion { get; set; }

    /// <summary>ARM SKU name (e.g., "Standard_D14").</summary>
    [JsonPropertyName("armSkuName")]
    public string ArmSkuName { get; set; } = string.Empty;

    /// <summary>
    /// Reservation term duration, if applicable (e.g., "1 Year", "3 Years").
    /// </summary>
    [JsonPropertyName("reservationTerm")]
    public string? ReservationTerm { get; set; }

    /// <summary>
    /// Date the pricing is effective until, if the price has a known end date.
    /// </summary>
    [JsonPropertyName("effectiveEndDate")]
    public DateTime? EffectiveEndDate { get; set; }
}
