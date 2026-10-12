namespace ResearchTrack.GitHubService.Features.Synchronization;

/// <summary>
/// Pure reconciliation decisions kept separate from transport and persistence so
/// mutation testing can measure the business rule without compiler-noise from
/// async/EF orchestration.
/// </summary>
internal static class GitHubReconciliationPolicy
{
    internal static bool HasRemoteChange(
        string? localDefaultBranch,
        string? localHeadSha,
        string remoteDefaultBranch,
        string remoteHeadSha
    ) =>
        !string.Equals(localDefaultBranch, remoteDefaultBranch, StringComparison.Ordinal)
        || !string.Equals(localHeadSha, remoteHeadSha, StringComparison.OrdinalIgnoreCase);
}
