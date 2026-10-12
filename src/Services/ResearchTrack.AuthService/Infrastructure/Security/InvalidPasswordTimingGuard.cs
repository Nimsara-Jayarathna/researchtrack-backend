using System.Security.Cryptography;

namespace ResearchTrack.AuthService.Infrastructure.Security;

public sealed class InvalidPasswordTimingGuard
{
    private readonly IPasswordHasher _passwordHasher;
    private readonly string _dummyHash;

    public InvalidPasswordTimingGuard(IPasswordHasher passwordHasher)
    {
        _passwordHasher = passwordHasher;
        // This is never an account credential. Generate a throwaway value to
        // exercise the same password-verification path for unknown users.
        _dummyHash = passwordHasher.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    }

    public void Verify(string password)
    {
        _ = _passwordHasher.Verify(password, _dummyHash);
    }
}
