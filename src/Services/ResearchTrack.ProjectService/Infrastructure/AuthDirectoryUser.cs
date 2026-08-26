namespace ResearchTrack.ProjectService.Infrastructure;

public sealed record AuthDirectoryUser(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? RegistrationNumber,
    string Role);