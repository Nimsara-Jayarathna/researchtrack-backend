namespace ResearchTrack.AuthService.Contracts;

public sealed record RegistrationConfigResponse(
    bool DomainRestrictionEnabled,
    string StudentDomain,
    string SupervisorDomain,
    bool StudentEmailPrefixRestrictionEnabled,
    string StudentEmailPrefixRegex,
    bool RequireStudentRegistrationNumber,
    bool RequireStudentRegistrationNumberToMatchEmail,
    PasswordPolicyResponse PasswordPolicy);

public sealed record PasswordPolicyResponse(
    int MinimumLength,
    int MaximumLength,
    bool RequireUppercase,
    bool RequireLowercase,
    bool RequireDigit,
    bool RequireSpecialCharacter);
