namespace ResearchTrack.GitHubService.Configuration;

public sealed record GitHubAppOptions(
    long AppId,
    string AppSlug,
    string ClientId,
    string ClientSecret,
    string PrivateKeyPath,
    Uri SetupCallbackUrl,
    Uri FrontendReturnOrigin,
    TimeSpan StateLifetime)
{
    public const string SectionName = "GitHub";
}
