using ResearchTrack.AuthService.Contracts;

namespace ResearchTrack.AuthService.Features.Authentication;

public sealed record AuthSessionResult(
    string AccessToken,
    string RefreshToken,
    LoginResponse Response);
