namespace ResearchTrack.AuthService.Contracts;

public sealed record RegisterVerifyResponse(
    string RegistrationToken,
    bool RequiresRoleSelection,
    string? Role);
