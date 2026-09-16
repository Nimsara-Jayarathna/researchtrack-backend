namespace ResearchTrack.GitHubService.Configuration;

public sealed record GitHubAppOptions(
    long AppId,
    string AppSlug,
    string ClientId,
    string ClientSecret,
    string PrivateKeyPath,
    Uri SetupCallbackUrl,
    Uri FrontendReturnOrigin,
    TimeSpan StateLifetime,
    TimeSpan AccessRequestLifetime)
{
    public const string SectionName = "GitHub";

    public string? PrivateKeyBase64 { get; init; }
}
