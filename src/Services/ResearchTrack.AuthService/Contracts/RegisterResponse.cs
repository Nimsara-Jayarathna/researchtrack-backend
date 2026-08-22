namespace ResearchTrack.AuthService.Contracts;

public sealed record RegisterResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? RegistrationNumber,
    string Role,
    DateTime CreatedAt);
