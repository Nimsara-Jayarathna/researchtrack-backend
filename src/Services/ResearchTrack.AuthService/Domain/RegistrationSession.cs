namespace ResearchTrack.AuthService.Domain;

public sealed class RegistrationSession
{
    public Guid Id { get; set; }
    public required string TokenHash { get; set; }
    public required string Email { get; set; }
    public UserRole? Role { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
