namespace Aspire.SLA.Azure.Models;

/// <summary>
/// Describes the deployment topology for a resource, influencing redundancy SLA calculations.
/// </summary>
public enum DeploymentTopology
{
    /// <summary>Single node with no built-in redundancy.</summary>
    SingleNode,

    /// <summary>Zone-redundant deployment across availability zones.</summary>
    ZoneRedundant,

    /// <summary>Region-redundant deployment across multiple regions.</summary>
    RegionRedundant,

    /// <summary>Active geo-replication across regions.</summary>
    GeoReplicated
}