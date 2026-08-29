namespace ResearchTrack.AuthService.Contracts;

public sealed record ChangePasswordRequest(
    string? CurrentPassword,
    string? NewPassword);
