namespace ResearchTrack.AuthService.Contracts;

public sealed class RegisterVerifyRequest
{
    public string? Email { get; init; }
    public string? Otp { get; init; }
}
