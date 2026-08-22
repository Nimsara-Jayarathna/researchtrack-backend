using System.ComponentModel.DataAnnotations;

namespace ResearchTrack.AuthService.Contracts;

public sealed class LoginRequest
{
    [Required]
    [EmailAddress]
    public string? Email { get; init; }

    [Required]
    public string? Password { get; init; }
}
