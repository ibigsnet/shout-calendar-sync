using System.Text;

namespace ShoutCalendar.Core;

public static class GitHubFeedback
{
    public const string Repo = "https://github.com/ibigsnet/shout-calendar";

    public static string FeatureUrl(string? version) =>
        IssueUrl("feature_request.md", "Feature request: " + PluginLine(version), FeatureBody(version));

    public static string ErrorUrl(string? version) =>
        IssueUrl("error_report.md", "Error report: " + PluginLine(version), ErrorBody(version));

    public static string FeaturePrompt(string? version) =>
        $"A GitHub feature request for {PluginLine(version)}. The form is filled in. Edit it, then submit. A GitHub account is required.";

    public static string ErrorPrompt(string? version) =>
        $"A GitHub error report for {PluginLine(version)}. The form is filled in. Edit it, then submit. A GitHub account is required.";

    public static string IssueUrl(string template, string title, string body)
    {
        var builder = new StringBuilder(Repo);
        builder.Append("/issues/new?template=");
        builder.Append(Uri.EscapeDataString(template));
        builder.Append("&title=");
        builder.Append(Uri.EscapeDataString(title));
        builder.Append("&body=");
        builder.Append(Uri.EscapeDataString(body));
        return builder.ToString();
    }

    private static string FeatureBody(string? version) =>
        $"""
        ### Idea

        <!-- What should Shout Calendar do? -->

        ### Why it helps

        ### Plugin
        {PluginLine(version)}
        """;

    private static string ErrorBody(string? version) =>
        $"""
        ### What happened

        ### What you expected

        ### Steps
        1.
        2.

        ### Plugin
        {PluginLine(version)}

        <!-- Leave out character names, Discord invites, and log lines that name other players. -->
        """;

    private static string PluginLine(string? version)
    {
        var trimmed = (version ?? "").Trim();
        return trimmed.Length == 0 ? "Shout Calendar" : "Shout Calendar " + trimmed;
    }
}
