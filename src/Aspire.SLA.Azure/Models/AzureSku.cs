namespace Aspire.SLA.Azure.Models;

/// <summary>
/// Represents the Azure service tier / SKU family used for SLA resolution and pricing.
/// </summary>
public enum AzureSku
{
    /// <summary>Basic tier.</summary>
    Basic,
    /// <summary>Standard tier.</summary>
    Standard,
    /// <summary>Premium tier.</summary>
    Premium,
    /// <summary>Enterprise tier.</summary>
    Enterprise
}
