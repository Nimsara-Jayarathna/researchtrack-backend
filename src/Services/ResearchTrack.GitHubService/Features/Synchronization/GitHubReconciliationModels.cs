namespace ResearchTrack.GitHubService.Features.Synchronization;

public sealed record GitHubDefaultBranchHead(string DefaultBranch, string HeadSha);

public sealed record GitHubReconciliationResult(
    int EligibleLinks,
    int CheckedLinks,
    int QueuedLinks,
    int FailedChecks)
{
    public bool Successful => FailedChecks == 0;
}
