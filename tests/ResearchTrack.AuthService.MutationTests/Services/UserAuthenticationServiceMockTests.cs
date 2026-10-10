using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ResearchTrack.AuthService.Configuration;
using ResearchTrack.AuthService.Contracts;
using ResearchTrack.AuthService.Domain;
using ResearchTrack.AuthService.Features.Authentication;
using ResearchTrack.AuthService.Infrastructure.Security;
using ResearchTrack.AuthService.Infrastructure.Tokens;
using ResearchTrack.AuthService.Persistence;
using ResearchTrack.BuildingBlocks.Api.Exceptions;

namespace ResearchTrack.AuthService.MutationTests.Services;

public sealed class UserAuthenticationServiceMockTests
{
    [Fact]
    public async Task LoginAsync_ValidCredentials_VerifiesPasswordGeneratesTokenAndPersistsRefreshToken()
    {
        var factory = new TestAuthDbContextFactory();
        var user = await SeedUserAsync(factory, "student@example.com", "stored-hash");
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Hash(Arg.Any<string>()).Returns("dummy-hash");
        hasher.Verify("Correct#123", "stored-hash").Returns(true);
        var tokenService = Substitute.For<IAccessTokenService>();
        tokenService.Generate(Arg.Any<User>()).Returns("access-token");
        var sut = CreateSut(factory, hasher, tokenService);

        var result = await sut.LoginAsync(
            new LoginRequest { Email = "  STUDENT@EXAMPLE.COM ", Password = "Correct#123" },
            CancellationToken.None);

        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal(user.Id, result.Response.User.Id);
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
        hasher.Received(1).Verify("Correct#123", "stored-hash");
        tokenService.Received(1).Generate(Arg.Is<User>(x => x.Id == user.Id));
        await using var verify = factory.CreateDbContext();
        var refresh = Assert.Single(verify.RefreshTokens);
        Assert.Equal(user.Id, refresh.UserId);
        Assert.NotEqual(result.RefreshToken, refresh.TokenHash);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_DoesNotGenerateTokenOrPersistRefreshToken()
    {
        var factory = new TestAuthDbContextFactory();
        await SeedUserAsync(factory, "student@example.com", "stored-hash");
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Hash(Arg.Any<string>()).Returns("dummy-hash");
        hasher.Verify("wrong", "stored-hash").Returns(false);
        var tokenService = Substitute.For<IAccessTokenService>();
        var sut = CreateSut(factory, hasher, tokenService);

        await Assert.ThrowsAsync<ApiException>(() => sut.LoginAsync(
            new LoginRequest { Email = "student@example.com", Password = "wrong" },
            CancellationToken.None));

        tokenService.DidNotReceive().Generate(Arg.Any<User>());
        await using var verify = factory.CreateDbContext();
        Assert.Empty(verify.RefreshTokens);
    }

    [Fact]
    public async Task LoginAsync_UnknownUser_ExecutesTimingGuardAndDoesNotGenerateToken()
    {
        var factory = new TestAuthDbContextFactory();
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Hash(Arg.Any<string>()).Returns("dummy-hash");
        hasher.Verify("Secret#123", "dummy-hash").Returns(false);
        var tokenService = Substitute.For<IAccessTokenService>();
        var sut = CreateSut(factory, hasher, tokenService);

        await Assert.ThrowsAsync<ApiException>(() => sut.LoginAsync(
            new LoginRequest { Email = "missing@example.com", Password = "Secret#123" },
            CancellationToken.None));

        hasher.Received(1).Verify("Secret#123", "dummy-hash");
        tokenService.DidNotReceive().Generate(Arg.Any<User>());
    }

    private static UserAuthenticationService CreateSut(
        IDbContextFactory<AuthDbContext> factory,
        IPasswordHasher hasher,
        IAccessTokenService tokenService) =>
        new(factory, hasher, new InvalidPasswordTimingGuard(hasher), tokenService,
            new JwtOptions("issuer", "audience", new string('k', 64), 15, 7));

    private static async Task<User> SeedUserAsync(TestAuthDbContextFactory factory, string email, string passwordHash)
    {
        var user = new User
        {
            Id = Guid.NewGuid(), Email = email, FirstName = "Sam", LastName = "Student",
            PasswordHash = passwordHash, Role = UserRole.Student, CreatedAt = DateTime.UtcNow
        };
        await using var db = factory.CreateDbContext();
        db.Users.Add(user);
        await db.SaveChangesAsync(CancellationToken.None);
        return user;
    }

    private sealed class TestAuthDbContextFactory : IDbContextFactory<AuthDbContext>
    {
        private readonly DbContextOptions<AuthDbContext> _options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase($"auth-mock-tests-{Guid.NewGuid():N}")
            .Options;
        public AuthDbContext CreateDbContext() => new(_options);
        public Task<AuthDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
