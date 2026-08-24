namespace ResearchTrack.AuthService.Contracts;

public sealed record UserDirectoryResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? RegistrationNumber,
    string Role);
