Here is the updated `README.md` reflecting the new design pattern: **non-covered or unrecognized SKUs gracefully degrade to a 0% SLA (0.00)**, naturally pulling down any dependent critical paths, without causing build-time compilation failures or halting local developer workflows.

***

# .NET Aspire SLA & Cost Compliance Checker

A modular, code-first compliance framework designed to programmatically evaluate composite Service Level Agreements (SLAs) and compute estimated cloud run costs directly inside your .NET Aspire orchestration graph [31]. 

By intercepting your strongly-typed infrastructure models during compilation (using `Azure.Provisioning` resolvers) [31] and checking the public **Azure Retail Prices API** [31], this library mathematically computes your application’s availability limits and estimated run-rate at build-time.

---

## Key Capabilities

* **Graceful 0% SLA Degradation:** Unrecognized services or non-covered resource tiers (such as Azure Cache for Redis Basic tier [38]) are assigned a **0% base SLA (0.00)**. Because critical dependencies multiply sequential SLAs, a single non-covered resource on a critical path naturally degrades the composite SLA of the entire service chain to **0%**, making architectural weaknesses immediately obvious in your logs.
* **Non-Disruptive Compilation:** Evaluates compliance without breaking local build workflows or throwing blocking compiler errors.
* **Infrastructure as Code (IaC) Inspection:** Eliminates duplicate configurations. By leveraging native `Azure.Provisioning` `InfrastructureResolver` hooks [31], the library extracts SKUs, replicas, and replication topologies directly from your C# infrastructure definitions [31, 38, 55, 103, 107].
* **Live Retail Price Matching:** Connects to the public Azure Retail Prices API during build validation to verify SKU existence in your target region and aggregate monthly runtime costs [31].
* **Composite SLA Graph Mathematics:** Automatically traverses your microservice topology using graph theory to compute combined sequential (series) and redundant (parallel) availability formulas.
* **Extensible Cloud Architecture:** Decoupled interfaces (`ISlaProvider`) let you extend the compliance engine to cover other public clouds like AWS or GCP.

---

## Directory Structure

This repository uses the modern `.slnx` solution format, dividing the core code-first verification engine from tests and multi-cloud reference configurations:

```text
MySlaWorkspace/
│
├── DistributedSla.slnx             # Main solution file referencing src, tests, and examples
│
├── src/
│   └── Aspire.SLA/         # Core SLA & Cost Verification Library
│       ├── Aspire.SLA.csproj
│       ├── SlaExtensions.cs        # Fluent orchestration extension methods
│       ├── SlaGraphValidator.cs    # Graph math execution engine (Series/Parallel calculation)
│       │
│       ├── Providers/              # Pluggable provider system
│       │   ├── ISlaProvider.cs     # Provider interface definition
│       │   ├── AzureSlaProvider.cs # Azure Pricing API client & PDF-matched SLA rules [38, 55, 103]
│       │   └── AwsSlaProvider.cs   # AWS Pricing API client & AWS SLA rules
│       │
│       ├── Models/                 # Configuration schemas
│       │   ├── SlaAnnotation.cs
│       │   └── SlaDependencyAnnotation.cs
│       │
│       └── build/                  # Automatic build-time target injection
│           └── Aspire.SLA.targets
│
├── tests/
│   └── Aspire.SLA.Tests/   # Unit & Integration Tests
│       ├── GraphMathTests.cs       # Asserts combined series/parallel equations
│       └── ProviderTests.cs        # Mock testing for Cloud Retail Pricing APIs
│
└── examples/                       # Reference workspaces
    ├── Sla-AzureExample/           # Azure Integration Example
    │   ├── Sla-AzureExample.AppHost/  # .NET Aspire Orchestrator using Azure.Provisioning [31]
    │   └── ...
    │
    └── Sla-AwsExample/             # AWS Integration Example
        ├── Sla-AwsExample.AppHost/    # .NET Aspire Orchestrator using AWS CDK
        └── ...
```

---

## Mathematical Foundations

When calculating the composite availability of your microservices, the engine applies the following calculations to your resource dependency graph:

### Series Dependencies
If Service A requires Service B to function (e.g., your Web API relies on a PostgreSQL database [55]), both must be online. A failure of any downstream node degrades the parent:
$$\text{Composite SLA} = \text{SLA}_1 \times \text{SLA}_2 \times \dots \times \text{SLA}_n$$

*If any dependency (like a basic-tier database [38]) resolves to a `0.0` SLA, the multiplicative nature of the series formula gracefully reduces the parent service's SLA to `0.0` (0%):*
$$\text{Composite SLA} = \text{SLA}_{API} \times 0.0 = 0.0$$

### Parallel Redundancy
If you configure multiple stateless compute replicas behind a load balancer (e.g., three instances of your API container), the system is healthy if *at least one* instance is active:
$$\text{Redundant SLA} = 1 - (1 - \text{SLA})^\text{replicas}$$

---

## Quick Start

### 1. Configure the SLA Hook in Your AppHost

Register the custom `SlaInfrastructureResolver` to intercept your infrastructure definitions. Then, define your resources and outline critical dependencies using `.WithSlaDependency(...)`:

```csharp
using Aspire.Hosting;
using Aspire.Hosting.Azure;
using AspireSlaCalculator;
using Microsoft.Extensions.DependencyInjection;

var builder = DistributedApplication.CreateBuilder(args);

// Register the SLA resolver to automatically read Azure.Provisioning models [31]
builder.Services.Configure<AzureProvisioningOptions>(options =>
{
    options.ProvisioningBuildOptions.InfrastructureResolvers.Add(
        new SlaInfrastructureResolver(builder.Resources)
    );
});

// Configure Azure Redis Cache (Changing Sku to Basic defaults SLA to 0.0) [38]
var redis = builder.AddAzureRedis("cache")
                   .ConfigureInfrastructure(infra =>
                   {
                       var cache = infra.GetProvisionableResources().OfType<RedisCache>().Single();
                       cache.Sku = new RedisCacheSku { Name = RedisCacheSkuName.Premium, Family = RedisCacheSkuFamily.P, Capacity = 1 };
                       
                       // Adding Availability Zones elevates Redis base SLA from 99.9% to 99.99% [38]
                       cache.Zones.Add("1"); cache.Zones.Add("2"); cache.Zones.Add("3");
                   });

// Configure standard Azure PostgreSQL Flexible Server (Resolves to 99.9% base SLA) [55]
var database = builder.AddAzurePostgres("postgres").AddDatabase("db");

// Configure API (Stateless compute has 3 replicas, scaling base container availability)
var api = builder.AddProject<Projects.MyApi>("api")
                 .WithAzureConfig(AzureSku.Standard, DeploymentTopology.SingleNode, "Virtual Machines", "Standard_B2s", replicaCount: 3)
                 .WithSlaDependency(redis, isCritical: true)
                 .WithSlaDependency(database, isCritical: true);

var frontend = builder.AddProject<Projects.MyFrontend>("frontend")
                      .WithAzureConfig(AzureSku.Standard, DeploymentTopology.SingleNode, "Virtual Machines", "Standard_B2s", replicaCount: 2)
                      .WithSlaDependency(api, isCritical: true);

var app = builder.Build();

// Build-time execution interceptor
if (args.Contains("--validate-sla"))
{
    // Executes assessment and prints diagnostic results without halting build compilation
    await SlaGraphValidator.RunValidationAsync(app, region: "eastus");
    Environment.Exit(0);
}

app.Run();
```

### 2. Add Local Fallback Target to Your AppHost `.csproj`

To run verification in your local workspace during development (before packaging your library as a NuGet), add the fallback target to your AppHost’s `.csproj` file:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <IsAspireHost>true</IsAspireHost>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\src\Aspire.SLA\Aspire.SLA.csproj" />
  </ItemGroup>

  <!-- Local Dev Fallback Import (Executes targets file directly from source code) -->
  <Import Project="..\..\..\src\Aspire.SLA\build\Aspire.SLA.targets" 
          Condition="Exists('..\..\..\src\Aspire.SLA\build\Aspire.SLA.targets') And '$(SlaTargetsImported)' != 'true'" />

</Project>
```

---

## Running the Verification

The build target triggers on `Release` builds or when explicitly instructed using the on-demand MSBuild property.

Run the build pipeline:
```bash
dotnet build -p:ValidateSla=true
```

### SLA Compliant Scenario
If your architecture resolves successfully with covered SKU selections [38, 55, 103]:

```text
[SLA Build Target] Triggering architecture compliance validation...
[SLA INFO] Verified 'cache' -> Azure Cache for Redis (Standard_C1) is active in 'eastus'. Retail cost: ~$109.50/mo. [38]
[SLA INFO] Verified 'postgres' -> Azure Database for PostgreSQL (Standard_D2s_v3) is active in 'eastus'. Retail cost: ~$146.00/mo. [55]

[SLA SUCCESS] SLA calculation passed!
--------------------------------------------------
Composite SLA: 99.8800% (51.84 minutes downtime/month)
Estimated Base Running Cost: $255.50/month
--------------------------------------------------
```

### Non-Compliant Scenario (No Build Failures)
If a developer configures an unrecognized service or introduces a non-covered resource tier (such as selecting a `Basic` SKU for Redis [38]):

```text
[SLA Build Target] Triggering architecture compliance validation...
[SLA WARNING] Resource 'cache' is configured with 'Basic' SKU, which is not covered by the Microsoft SLA. Defaulting to 0% base SLA. [38]
[SLA INFO] Verified 'postgres' -> Azure Database for PostgreSQL (Standard_D2s_v3) is active in 'eastus'. Retail cost: ~$146.00/mo. [55]

[SLA SUCCESS] SLA calculation passed!
--------------------------------------------------
Composite SLA: 0.0000% (43200.00 minutes downtime/month)
Estimated Base Running Cost: $146.00/month
--------------------------------------------------
```

The build compiles successfully, but the resulting `0.0000%` SLA metrics explicitly identify the exact component introducing availability vulnerabilities.

---

## Extensibility: Adding Cloud Providers

To add support for other services, implement the `ISlaProvider` interface. Unmapped structures will naturally fall back to the 0% SLA safety net.

```csharp
public class AwsSlaProvider : ISlaProvider
{
    public bool CanHandle(IResource resource) => resource.GetType().Name.Contains("Aws");

    public double GetBaseSla(IResource resource)
    {
        // Extract AWS-specific metadata or default to SLA standards
        return 0.999;
    }

    public async Task<double> GetMonthlyCostAsync(IResource resource, string region)
    {
        // Perform an asynchronous lookup using the AWS Price List API
        return 42.00;
    }
}
```

Register your newly created class in the `SlaGraphValidator.Providers` collection, and the engine will seamlessly blend AWS and Azure resources into a singular hybrid composite SLA assessment.
