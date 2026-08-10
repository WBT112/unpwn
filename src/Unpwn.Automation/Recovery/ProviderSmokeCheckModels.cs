using Unpwn.Core;

namespace Unpwn.Automation.Recovery;

public enum ProviderLocationSmokeCheckStatus
{
    Reachable,
    Redirected,
    ProviderBlocked,
    Unavailable,
    UnexpectedRedirect,
    Insecure,
}

public sealed record ProviderLocationSmokeCheckResult(
    string WorkflowId,
    string WorkflowVersion,
    DateOnly VerifiedAt,
    bool VerificationIsStale,
    string LocationId,
    string Location,
    ProviderLocationSmokeCheckStatus Status,
    int? HttpStatusCode,
    IReadOnlyList<string> RedirectOrigins,
    string DiagnosticCode)
{
    public bool RequiresReview =>
        VerificationIsStale ||
        Status is not ProviderLocationSmokeCheckStatus.Reachable and
            not ProviderLocationSmokeCheckStatus.Redirected;
}

public sealed record ProviderSmokeCheckReport(
    DateOnly CheckedOn,
    int StaleAfterDays,
    IReadOnlyList<ProviderLocationSmokeCheckResult> Locations)
{
    public bool HasWarnings => Locations.Any(location => location.RequiresReview);
}

public static class ProviderSmokeCheckMarkdownReporter
{
    public static string Render(ProviderSmokeCheckReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var lines = new List<string>
        {
            "# Provider recovery location smoke check",
            string.Empty,
            $"Checked on `{report.CheckedOn:yyyy-MM-dd}`; workflow verification becomes stale after {report.StaleAfterDays} days.",
            string.Empty,
            "Live observations are warnings for review, not proof that a provider workflow is defective.",
            string.Empty,
            "| Workflow | Version | Verified at | Freshness | Location | Reviewed URL | Result | HTTP | Redirect origins | Diagnostic |",
            "| --- | --- | --- | --- | --- | --- | --- | ---: | --- | --- |",
        };

        foreach (var location in report.Locations)
        {
            lines.Add(string.Join(
                " | ",
                "| " + Escape(location.WorkflowId),
                Escape(location.WorkflowVersion),
                location.VerifiedAt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                location.VerificationIsStale ? "stale" : "current",
                Escape(location.LocationId),
                Escape(location.Location),
                location.Status.ToString(),
                location.HttpStatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—",
                Escape(location.RedirectOrigins.Count == 0 ? "—" : string.Join(" → ", location.RedirectOrigins)),
                "`" + Escape(location.DiagnosticCode) + "` |"));
        }

        lines.Add(string.Empty);
        lines.Add(report.HasWarnings
            ? "Review the warning rows manually, accounting for provider bot protection, transient outages, and regional redirects."
            : "No warning conditions were observed.");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}
