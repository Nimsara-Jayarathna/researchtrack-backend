namespace ResearchTrack.BuildingBlocks.Api.Security;

public static class AuthSecurityConstants
{
    public const string AccessCookieName = "ss_access_token";
    public const string RefreshCookieName = "ss_refresh_token";
    public const string RoleClaim = "role";
    public const string SubjectClaim = "sub";

    public static class Roles
    {
        public const string Student = "STUDENT";
        public const string Supervisor = "SUPERVISOR";
    }

    public static class Policies
    {
        public const string Authenticated = "ResearchTrack.Authenticated";
        public const string StudentOnly = "ResearchTrack.StudentOnly";
        public const string SupervisorOnly = "ResearchTrack.SupervisorOnly";
    }
}
