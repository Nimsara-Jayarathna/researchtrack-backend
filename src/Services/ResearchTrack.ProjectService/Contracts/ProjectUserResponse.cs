namespace ResearchTrack.ProjectService.Contracts;

public sealed record ProjectUserResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? RegistrationNumber);

public sealed record ProjectMemberResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? RegistrationNumber,
    string MemberRole);
