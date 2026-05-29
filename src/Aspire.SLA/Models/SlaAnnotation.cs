using Aspire.Hosting.ApplicationModel;

namespace Aspire.SLA.Models;

public class SlaAnnotation : IResourceAnnotation
{
    public double Availability { get; }
    public SlaAnnotation(double availability) => Availability = availability;
}