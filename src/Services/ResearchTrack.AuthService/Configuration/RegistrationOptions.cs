using System.Text.RegularExpressions;

namespace ResearchTrack.AuthService.Configuration;

public sealed record RegistrationOptions(
    bool DomainRestrictionEnabled,
    string StudentEmailDomain,
    string SupervisorEmailDomain,
    bool StudentEmailPrefixRestrictionEnabled,
    string StudentIdentifierPattern,
    bool RequireStudentRegistrationNumber,
    bool RequireStudentRegistrationNumberToMatchEmail,
    int MaxFirstNameLength,
    int MaxLastNameLength,
    int MaxEmailLength,
    int MaxRegistrationNumberLength,
    int OtpExpirySeconds,
    int SessionExpirySeconds)
{
    public static RegistrationOptions FromConfiguration(IConfiguration configuration)
    {
        var studentDomain = NormalizeDomain(Require(configuration, "Registration:StudentEmailDomain"));
        var supervisorDomain = NormalizeDomain(Require(configuration, "Registration:SupervisorEmailDomain"));
        if (studentDomain.Equals(supervisorDomain, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Registration student and supervisor email domains must be different.");
        }

        var pattern = Require(configuration, "Registration:StudentIdentifierPattern");
        try
        {
            _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("Registration:StudentIdentifierPattern is not a valid regular expression.", exception);
        }

        return new RegistrationOptions(
            RequireBool(configuration, "Registration:DomainRestrictionEnabled"),
            studentDomain,
            supervisorDomain,
            RequireBool(configuration, "Registration:StudentEmailPrefixRestrictionEnabled"),
            pattern,
            RequireBool(configuration, "Registration:RequireStudentRegistrationNumber"),
            RequireBool(configuration, "Registration:RequireStudentRegistrationNumberToMatchEmail"),
            RequireInt(configuration, "Registration:MaxFirstNameLength", 1, 500),
            RequireInt(configuration, "Registration:MaxLastNameLength", 1, 500),
            RequireInt(configuration, "Registration:MaxEmailLength", 3, 1024),
            RequireInt(configuration, "Registration:MaxRegistrationNumberLength", 1, 100),
            RequireInt(configuration, "Registration:OtpExpirySeconds", 30, 3600),
            RequireInt(configuration, "Registration:SessionExpirySeconds", 30, 3600));
    }

    public bool HasStudentDomain => !string.IsNullOrWhiteSpace(StudentEmailDomain);
    public bool HasSupervisorDomain => !string.IsNullOrWhiteSpace(SupervisorEmailDomain);

    public bool EffectiveStudentEmailPrefixRestrictionEnabled =>
        DomainRestrictionEnabled
        && StudentEmailPrefixRestrictionEnabled
        && HasStudentDomain
        && !string.IsNullOrWhiteSpace(StudentIdentifierPattern);

    public UserRoleResolution InferRole(string normalizedEmail)
    {
        var domain = ExtractDomain(normalizedEmail);
        if (domain is null)
        {
            return UserRoleResolution.None;
        }

        if (HasStudentDomain && domain.Equals(StudentEmailDomain, StringComparison.OrdinalIgnoreCase))
        {
            return UserRoleResolution.Student;
        }

        if (HasSupervisorDomain && domain.Equals(SupervisorEmailDomain, StringComparison.OrdinalIgnoreCase))
        {
            return UserRoleResolution.Supervisor;
        }

        return UserRoleResolution.None;
    }

    public bool IsEmailAllowed(string normalizedEmail) =>
        !DomainRestrictionEnabled || InferRole(normalizedEmail) != UserRoleResolution.None;

    private static string NormalizeDomain(string value) => value.Trim().TrimStart('@').ToLowerInvariant();

    private static string? ExtractDomain(string email)
    {
        var atIndex = email.LastIndexOf('@');
        return atIndex > 0 && atIndex < email.Length - 1
            ? email[(atIndex + 1)..]
            : null;
    }

    private static string Require(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value) || value.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Required configuration '{key}' is missing or still contains a placeholder value.");
        }
        return value.Trim();
    }

    private static bool RequireBool(IConfiguration configuration, string key)
    {
        var raw = Require(configuration, key);
        return bool.TryParse(raw, out var value)
            ? value
            : throw new InvalidOperationException($"Configuration '{key}' must be true or false.");
    }

    private static int RequireInt(IConfiguration configuration, string key, int min, int max)
    {
        var raw = Require(configuration, key);
        if (!int.TryParse(raw, out var value) || value < min || value > max)
        {
            throw new InvalidOperationException($"Configuration '{key}' must be an integer between {min} and {max}.");
        }
        return value;
    }
}

public enum UserRoleResolution
{
    None,
    Student,
    Supervisor
}
