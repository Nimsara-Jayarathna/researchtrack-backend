namespace ResearchTrack.AuthService.Contracts;

public sealed class RegisterCompleteRequest
{
    public string? RegistrationToken { get; init; }
    public string? Fname { get; init; }
    public string? Lname { get; init; }
    public string? Password { get; init; }
    public string? Name { get; init; }
    public string? Role { get; init; }
}
