namespace ResearchTrack.GitHubService.Domain;

public static class GitHubRepositoryAccessFailureCodes
{
    public const string RequestNotFound = "request_not_found";
    public const string RequestContextMismatch = "request_context_mismatch";
    public const string RequestNotPending = "request_not_pending";
    public const string RequestFailed = "request_failed";
    public const string RequestCompleted = "request_completed";
    public const string RequestExpired = "request_expired";
    public const string UnexpectedUserOAuth = "unexpected_user_oauth";
    public const string Cancelled = "cancelled";
    public const string GitHubAuthorizationFailed = "github_authorization_failed";
    public const string MissingInstallation = "missing_installation";
    public const string InvalidSetupAction = "invalid_setup_action";
    public const string InvalidInstallation = "invalid_installation";
    public const string GitHubUnavailable = "github_unavailable";
    public const string RepositoryNotAccessible = "repository_not_accessible";
    public const string RepositoryMismatch = "repository_mismatch";
    public const string RepositoryIdentityChanged = "repository_identity_changed";
    public const string RepositoryAlreadyLinked = "repository_already_linked";
    public const string ProjectRepositoryLimitReached = "project_repository_limit_reached";
    public const string ProjectEnabledRepositoryLimitReached = "project_enabled_repository_limit_reached";
    public const string InstallationSourceConflict = "installation_source_conflict";
    public const string InstallationMismatch = "installation_mismatch";
    public const string CompletionInProgress = "completion_in_progress";
    public const string CompletionFailed = "completion_failed";
    public const string CompletionInconsistent = "completion_inconsistent";

    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        RequestNotFound,
        RequestContextMismatch,
        RequestNotPending,
        RequestFailed,
        RequestCompleted,
        RequestExpired,
        UnexpectedUserOAuth,
        Cancelled,
        GitHubAuthorizationFailed,
        MissingInstallation,
        InvalidSetupAction,
        InvalidInstallation,
        GitHubUnavailable,
        RepositoryNotAccessible,
        RepositoryMismatch,
        RepositoryIdentityChanged,
        RepositoryAlreadyLinked,
        ProjectRepositoryLimitReached,
        ProjectEnabledRepositoryLimitReached,
        InstallationSourceConflict,
        InstallationMismatch,
        CompletionInProgress,
        CompletionFailed,
        CompletionInconsistent
    };

    public static bool IsSafe(string? value) =>
        value is not null && Allowed.Contains(value);

    public static string RequireSafe(string value)
    {
        if (!IsSafe(value))
        {
            throw new ArgumentException("Owner-granted repository failure code is not recognized.", nameof(value));
        }

        return value;
    }
}
