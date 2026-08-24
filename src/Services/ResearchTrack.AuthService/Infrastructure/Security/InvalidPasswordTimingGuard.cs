namespace ResearchTrack.AuthService.Infrastructure.Security;

public sealed class InvalidPasswordTimingGuard
{
    private const string DummyPassword = "ResearchTrack-Timing-Guard-Only";
    private readonly IPasswordHasher _passwordHasher;
    private readonly string _dummyHash;

    public InvalidPasswordTimingGuard(IPasswordHasher passwordHasher)
    {
        _passwordHasher = passwordHasher;
        _dummyHash = passwordHasher.Hash(DummyPassword);
    }

    public void Verify(string password)
    {
        _ = _passwordHasher.Verify(password, _dummyHash);
    }
}
