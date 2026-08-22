namespace ResearchTrack.AuthService.Contracts;

public sealed record RegistrationCompleteResponse(RegistrationUserResponse User);

public sealed record RegistrationUserResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Role);
