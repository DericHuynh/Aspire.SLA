using Aspire.Hosting;
using Aspire.SLA;
using Aspire.SLA.Azure;
using Aspire.SLA.Aws;

var builder = DistributedApplication.CreateBuilder(args);

// Register cloud providers for SLA validation
SlaGraphValidator.RegisterProvider(new AzureSlaProvider());
SlaGraphValidator.RegisterProvider(new AwsSlaProvider());

var app = builder.Build();

// Build-time SLA validation entry point
if (args.Contains("--validate-sla"))
{
    await SlaGraphValidator.RunValidationAsync(app, region: "eastus").ConfigureAwait(false);
    Environment.Exit(0);
}

await app.RunAsync().ConfigureAwait(false);
