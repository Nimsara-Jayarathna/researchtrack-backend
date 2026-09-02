namespace ResearchTrack.Ui.Tests.Configuration;

public sealed record TestSettings(
    Uri FrontendBaseUrl,
    Uri ApiBaseUrl,
    bool Headless,
    TimeSpan WaitTimeout,
    string StudentEmail,
    string InvalidStudentEmail,
    string SupervisorEmail,
    string? EmailFlowEmail)
{
    public static TestSettings Load() => new(
        ReadUrl("RESEARCHTRACK_FRONTEND_URL", "http://localhost:5173"),
        ReadUrl("RESEARCHTRACK_API_URL", "http://localhost:5000"),
        ReadBool("SELENIUM_HEADLESS", false),
        TimeSpan.FromSeconds(ReadPositiveInt("SELENIUM_WAIT_SECONDS", 15)),
        ReadString("RESEARCHTRACK_STUDENT_EMAIL", "IT24100487@my.sliit.lk"),
        ReadString("RESEARCHTRACK_INVALID_STUDENT_EMAIL", "XX24100487@my.sliit.lk"),
        ReadString("RESEARCHTRACK_SUPERVISOR_EMAIL", "selenium.supervisor@sliit.lk"),
        ReadOptionalString("RESEARCHTRACK_EMAIL_FLOW_EMAIL"));

    public Uri RegistrationUrl => new(FrontendBaseUrl, "/register");

    private static Uri ReadUrl(string name, string fallback)
    {
        var raw = ReadString(name, fallback).TrimEnd('/') + "/";
        return Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException($"{name} must be an absolute URL.");
    }

    private static bool ReadBool(string name, bool fallback) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;

    private static int ReadPositiveInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0
            ? value
            : fallback;

    private static string ReadString(string name, string fallback) =>
        ReadOptionalString(name) ?? fallback;

    private static string? ReadOptionalString(string name)
    {
        var value = Environment.GetEnvironmentVariable(name)?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
