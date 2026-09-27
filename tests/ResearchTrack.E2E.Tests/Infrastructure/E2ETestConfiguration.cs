namespace ResearchTrack.E2E.Tests.Infrastructure;

public sealed class E2ETestConfiguration
{
    private readonly IReadOnlyDictionary<string, string> _fileValues;

    private E2ETestConfiguration(IReadOnlyDictionary<string, string> fileValues)
    {
        _fileValues = fileValues;
        BaseUrl = Required("E2E_BASE_URL").TrimEnd('/');
        SupervisorEmail = Required("E2E_SUPERVISOR_EMAIL");
        SupervisorPassword = Required("E2E_SUPERVISOR_PASSWORD");
        StudentEmail = Required("E2E_STUDENT_EMAIL");
        StudentPassword = Required("E2E_STUDENT_PASSWORD");
        ProjectId = Required("E2E_PROJECT_ID");
        Browser = Optional("E2E_BROWSER", "chrome").ToLowerInvariant();
        Headless = Boolean("E2E_HEADLESS", defaultValue: true);
        AcceptInsecureCertificates = Boolean("E2E_ACCEPT_INSECURE_CERTIFICATES", defaultValue: false);
        TimeoutSeconds = Integer("E2E_TIMEOUT_SECONDS", defaultValue: 30, minimum: 5, maximum: 180);
        ExpectedJiraIssueKey = Optional("E2E_EXPECTED_JIRA_ISSUE_KEY", string.Empty);
        ExpectedJiraStatus = Optional("E2E_EXPECTED_JIRA_STATUS", string.Empty);
    }

    internal string BaseUrl { get; }
    internal string SupervisorEmail { get; }
    internal string SupervisorPassword { get; }
    internal string StudentEmail { get; }
    internal string StudentPassword { get; }
    internal string ProjectId { get; }
    internal string Browser { get; }
    internal bool Headless { get; }
    internal bool AcceptInsecureCertificates { get; }
    internal int TimeoutSeconds { get; }
    internal string ExpectedJiraIssueKey { get; }
    internal string ExpectedJiraStatus { get; }

    internal string SupervisorProjectUrl => $"{BaseUrl}/supervisor/projects/{ProjectId}";
    internal string StudentProjectUrl => $"{BaseUrl}/student/projects/{ProjectId}";

    internal static E2ETestConfiguration Load() => new(DotEnvFile.Read());

    internal void RequireRefreshExpectation()
    {
        if (string.IsNullOrWhiteSpace(ExpectedJiraIssueKey) || string.IsNullOrWhiteSpace(ExpectedJiraStatus))
        {
            throw new InvalidOperationException(
                "E2E-002 requires E2E_EXPECTED_JIRA_ISSUE_KEY and E2E_EXPECTED_JIRA_STATUS in .env.e2e or process environment variables.");
        }
    }

    private string Required(string name)
    {
        var value = Lookup(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required E2E configuration '{name}' was not provided. Copy .env.e2e.example to .env.e2e and provide test-environment values.");
        }

        return value.Trim();
    }

    private string Optional(string name, string defaultValue)
    {
        var value = Lookup(name);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }

    private bool Boolean(string name, bool defaultValue)
    {
        var value = Lookup(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"E2E configuration '{name}' must be true or false.");
    }

    private int Integer(string name, int defaultValue, int minimum, int maximum)
    {
        var value = Lookup(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (int.TryParse(value, out var parsed) && parsed >= minimum && parsed <= maximum)
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"E2E configuration '{name}' must be an integer between {minimum} and {maximum}.");
    }

    private string? Lookup(string name)
    {
        var environmentValue = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue;
        }

        return _fileValues.TryGetValue(name, out var fileValue) ? fileValue : null;
    }
}
