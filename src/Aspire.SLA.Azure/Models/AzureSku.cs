namespace Aspire.SLA.Azure.Models;

/// <summary>
/// Represents the Azure service tier / SKU family used for SLA resolution and pricing.
/// </summary>
public enum AzureSku
{
    Basic,
    Standard,
    Premium,
    Enterprise
}
