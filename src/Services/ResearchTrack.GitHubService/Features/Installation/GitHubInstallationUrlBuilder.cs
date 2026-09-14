namespace ResearchTrack.GitHubService.Features.Installation;

internal static class GitHubInstallationUrlBuilder
{
    public static Uri Build(string appSlug, string state)
    {
        if (string.IsNullOrWhiteSpace(appSlug))
        {
            throw new ArgumentException("GitHub App slug is required.", nameof(appSlug));
        }
        if (string.IsNullOrWhiteSpace(state))
        {
            throw new ArgumentException("GitHub installation state is required.", nameof(state));
        }

        var builder = new UriBuilder(Uri.UriSchemeHttps, "github.com")
        {
            Path = $"apps/{Uri.EscapeDataString(appSlug)}/installations/new",
            Query = $"state={Uri.EscapeDataString(state)}"
        };
        return builder.Uri;
    }
}
