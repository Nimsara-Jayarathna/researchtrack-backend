using ResearchTrack.AuthService.Contracts;

namespace ResearchTrack.AuthService.Features.Registration;

public sealed record RegistrationCompletionResult(
    string AccessToken,
    string RefreshToken,
    RegistrationCompleteResponse Response);
