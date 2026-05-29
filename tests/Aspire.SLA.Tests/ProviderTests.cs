using Aspire.SLA.Azure;
using Aspire.SLA.Azure.Models;
using Aspire.SLA.Aws;
using Aspire.SLA.Aws.Models;
using Aspire.SLA.Models;

namespace Aspire.SLA.test;

/// <summary>Tests for Azure and AWS SLA providers.</summary>
public sealed class ProviderTests
{
    [Fact]
    internal void AzureSlaProvider_CanHandle_ResourceWithAzureAnnotation_ReturnsTrue()
    {
        var provider = new AzureSlaProvider();
        var resource = new StubResource("my-redis");
        resource.AddAnnotation(new AzureConfigAnnotation(
            AzureSku.Standard, DeploymentTopology.SingleNode, "Redis", "Standard_C1"));
        Assert.True(provider.CanHandle(resource));
    }

    [Fact]
    internal void AzureSlaProvider_CanHandle_ResourceWithoutAzureAnnotation_ReturnsFalse()
    {
        var provider = new AzureSlaProvider();
        var resource = new StubResource("my-container");
        Assert.False(provider.CanHandle(resource));
    }

    [Fact]
    internal void AwsSlaProvider_CanHandle_ResourceWithAwsAnnotation_ReturnsTrue()
    {
        var provider = new AwsSlaProvider();
        var resource = new StubResource("my-aws-rds");
        resource.AddAnnotation(new AwsConfigAnnotation("AmazonRDS", "db.t3.micro"));
        Assert.True(provider.CanHandle(resource));
    }

    [Fact]
    internal void AwsSlaProvider_CanHandle_ResourceWithoutAwsAnnotation_ReturnsFalse()
    {
        var provider = new AwsSlaProvider();
        var resource = new StubResource("my-container");
        Assert.False(provider.CanHandle(resource));
    }

    [Fact]
    internal void AzureSlaProvider_GetBaseSla_WithoutAnnotation_ReturnsZero()
    {
        var provider = new AzureSlaProvider();
        var resource = new StubResource("unrecognized-service");
        Assert.Equal(0.0, provider.GetBaseSla(resource));
    }

    [Fact]
    internal void AzureSlaProvider_GetBaseSla_WithManualAnnotation_ReturnsAnnotationValue()
    {
        var provider = new AzureSlaProvider();
        var resource = new StubResource("my-redis");
        resource.AddAnnotation(new SlaAnnotation(0.9995));
        Assert.Equal(0.9995, provider.GetBaseSla(resource));
    }

    [Fact]
    internal void AwsSlaProvider_GetBaseSla_WithoutAnnotation_ReturnsZero()
    {
        var provider = new AwsSlaProvider();
        var resource = new StubResource("unrecognized-service");
        Assert.Equal(0.0, provider.GetBaseSla(resource));
    }
}
