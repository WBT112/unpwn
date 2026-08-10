using System.Text;
using Unpwn.Automation.Recovery;
using Unpwn.Providers.Workflows;

var checkedOn = DateOnly.FromDateTime(DateTime.UtcNow);
var validation = RepositoryWorkflowCatalog.ValidateAll(checkedOn);
if (!validation.IsValid)
{
    Console.Error.WriteLine("Repository workflow validation failed; live checks were not started.");
    foreach (var diagnostic in validation.Diagnostics)
    {
        Console.Error.WriteLine($"{diagnostic.WorkflowId}: {diagnostic.Rule}");
    }

    return 1;
}

using var smokeChecks = ProviderSmokeCheckService.CreateDefault();
var report = await smokeChecks.CheckAsync(
    RepositoryWorkflowCatalog.Workflows,
    checkedOn,
    CancellationToken.None);
var markdown = ProviderSmokeCheckMarkdownReporter.Render(report);
Console.Write(markdown);

foreach (var result in report.Locations.Where(result => result.RequiresReview))
{
    Console.WriteLine(
        $"::warning title=Provider smoke check::{result.WorkflowId}/{result.LocationId}: {result.DiagnosticCode}");
}

var stepSummary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
if (!string.IsNullOrWhiteSpace(stepSummary))
{
    await File.AppendAllTextAsync(stepSummary, markdown, Encoding.UTF8);
}

return 0;
