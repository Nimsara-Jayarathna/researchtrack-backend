using Microsoft.Extensions.Configuration;

namespace ResearchTrack.GitHubService.Configuration;

public static class GitHubRepositoryLinkOptionsFactory
{
    public const string MaxLinkedRepositoriesKey =
        "GitHub:RepositoryLinks:MaxLinkedRepositories";
    public const string MaxEnabledRepositoriesKey =
        "GitHub:RepositoryLinks:MaxEnabledRepositories";

    public static GitHubRepositoryLinkOptions Create(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var linked = RequirePositiveInt(configuration, MaxLinkedRepositoriesKey);
        var enabled = RequirePositiveInt(configuration, MaxEnabledRepositoriesKey);
        if (enabled > linked)
        {
            throw new InvalidOperationException(
                $"Required GitHub repository configuration '{MaxEnabledRepositoriesKey}' " +
                $"cannot exceed '{MaxLinkedRepositoriesKey}'.");
        }

        return new GitHubRepositoryLinkOptions(linked, enabled);
    }

    private static int RequirePositiveInt(IConfiguration configuration, string key)
    {
        var raw = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(raw)
            || raw.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(raw, out var value)
            || value < 1)
        {
            throw new InvalidOperationException(
                $"Required configuration '{key}' must be a positive integer.");
        }

        return value;
    }
}
