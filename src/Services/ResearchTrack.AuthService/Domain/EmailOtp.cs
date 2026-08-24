namespace ResearchTrack.AuthService.Domain;

public sealed class EmailOtp
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string OtpHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
