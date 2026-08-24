namespace ResearchTrack.AuthService.Contracts;

public sealed record AuthUserResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Role);
