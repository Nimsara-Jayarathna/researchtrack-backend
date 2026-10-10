using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ResearchTrack.AuthService.Configuration;
using ResearchTrack.AuthService.Contracts;
using ResearchTrack.AuthService.Domain;
using ResearchTrack.AuthService.Features.Passwords;
using ResearchTrack.AuthService.Features.Users;
using ResearchTrack.AuthService.Infrastructure.Email;
using ResearchTrack.AuthService.Infrastructure.Security;
using ResearchTrack.AuthService.Persistence;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;

namespace ResearchTrack.AuthService.MutationTests.Services;

public sealed class PasswordResetAndAccountCoverageTests
{
    [Fact]
    public async Task RequestResetAsync_UnknownEmail_IsNonEnumeratingAndDoesNotSendEmail()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteAuthFactory.CreateAsync(ct);
        var email = Substitute.For<IPasswordResetEmailService>();
        var sut = CreateResetSut(factory, email: email);

        await sut.RequestResetAsync(new ForgotPasswordRequest("missing@staff.example.edu"), ct);

        await email.DidNotReceive().SendResetLinkAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), ct);
        await using var db = factory.CreateDbContext();
        Assert.Empty(await db.PasswordResetTokens.ToListAsync(ct));
    }

    [Fact]
    public async Task RequestResetAsync_KnownEmail_RevokesPriorUnusedTokenPersistsNewTokenAndSendsLink()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteAuthFactory.CreateAsync(ct);
        var userId = await SeedUserAsync(factory, ct);
        await using (var db = factory.CreateDbContext())
        {
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = "old-token",
                ExpiresAt = DateTime.UtcNow.AddMinutes(5),
                CreatedAt = DateTime.UtcNow.AddMinutes(-2)
            });
            await db.SaveChangesAsync(ct);
        }
        var email = Substitute.For<IPasswordResetEmailService>();
        var sut = CreateResetSut(factory, email: email);

        await sut.RequestResetAsync(new ForgotPasswordRequest("USER@STAFF.EXAMPLE.EDU"), ct);

        await email.Received(1).SendResetLinkAsync(
            "user@staff.example.edu",
            Arg.Is<string>(url => url.StartsWith("https://app.example.edu/reset-password?token=", StringComparison.Ordinal)),
            30,
            ct);
        await using var verify = factory.CreateDbContext();
        var tokens = await verify.PasswordResetTokens.OrderBy(x => x.CreatedAt).ToListAsync(ct);
        Assert.Equal(2, tokens.Count);
        Assert.NotNull(tokens[0].UsedAt);
        Assert.Null(tokens[1].UsedAt);
    }

    [Fact]
    public async Task RequestResetAsync_EmailDeliveryFailure_InvalidatesNewTokenButDoesNotThrow()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteAuthFactory.CreateAsync(ct);
        await SeedUserAsync(factory, ct);
        var email = Substitute.For<IPasswordResetEmailService>();
        email.SendResetLinkAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), ct)
            .Returns(_ => Task.FromException(new HttpRequestException("mail down")));
        var sut = CreateResetSut(factory, email: email);

        await sut.RequestResetAsync(new ForgotPasswordRequest("user@staff.example.edu"), ct);

        await using var db = factory.CreateDbContext();
        var token = await db.PasswordResetTokens.SingleAsync(ct);
        Assert.NotNull(token.UsedAt);
    }

    [Fact]
    public async Task ValidateTokenAsync_ActiveStoredToken_ReturnsTrue_UsedOrExpiredReturnsFalse()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteAuthFactory.CreateAsync(ct);
        var userId = await SeedUserAsync(factory, ct);
        const string rawToken = "valid-reset-token";
        await using (var db = factory.CreateDbContext())
        {
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = Hash(rawToken),
                ExpiresAt = DateTime.UtcNow.AddMinutes(5),
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
        var sut = CreateResetSut(factory);

        Assert.True((await sut.ValidateTokenAsync(rawToken, ct)).Valid);
        Assert.False((await sut.ValidateTokenAsync("different-token", ct)).Valid);
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidToken_ChangesHashConsumesTokenAndRevokesRefreshSessions()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteAuthFactory.CreateAsync(ct);
        var userId = await SeedUserAsync(factory, ct, passwordHash: "old-hash");
        const string rawToken = "reset-me";
        await using (var db = factory.CreateDbContext())
        {
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = Hash(rawToken),
                ExpiresAt = DateTime.UtcNow.AddMinutes(5),
                CreatedAt = DateTime.UtcNow
            });
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = "refresh",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(10)
            });
            await db.SaveChangesAsync(ct);
        }
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Verify("NewStrongPassword!1", "old-hash").Returns(false);
        hasher.Hash("NewStrongPassword!1").Returns("new-hash");
        var sut = CreateResetSut(factory, hasher: hasher);

        await sut.ResetPasswordAsync(new ResetPasswordRequest(rawToken, "NewStrongPassword!1"), ct);

        await using var verify = factory.CreateDbContext();
        Assert.Equal("new-hash", (await verify.Users.SingleAsync(ct)).PasswordHash);
        Assert.NotNull((await verify.PasswordResetTokens.SingleAsync(ct)).UsedAt);
        Assert.NotNull((await verify.RefreshTokens.SingleAsync(ct)).RevokedAt);
    }

    [Fact]
    public async Task ResetPasswordAsync_ReusedCurrentPassword_IsRejectedWithoutConsumingToken()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteAuthFactory.CreateAsync(ct);
        var userId = await SeedUserAsync(factory, ct, passwordHash: "same-hash");
        const string rawToken = "same-password-token";
        await SeedResetTokenAsync(factory, userId, rawToken, ct);
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Verify("StrongPassword!1", "same-hash").Returns(true);
        var sut = CreateResetSut(factory, hasher: hasher);

        var exception = await Assert.ThrowsAsync<ApiValidationException>(() =>
            sut.ResetPasswordAsync(new ResetPasswordRequest(rawToken, "StrongPassword!1"), ct));

        Assert.Contains(exception.FieldErrors, x => x.Field == "newPassword");
        await using var db = factory.CreateDbContext();
        Assert.Null((await db.PasswordResetTokens.SingleAsync(ct)).UsedAt);
        hasher.DidNotReceive().Hash(Arg.Any<string>());
    }

    [Fact]
    public async Task ResetPasswordAsync_UnknownToken_ReturnsGenericValidationError()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteAuthFactory.CreateAsync(ct);
        var sut = CreateResetSut(factory);

        var exception = await Assert.ThrowsAsync<ApiValidationException>(() =>
            sut.ResetPasswordAsync(new ResetPasswordRequest("unknown-token", "StrongPassword!1"), ct));

        Assert.Contains(exception.FieldErrors, x => x.Field == "token");
    }

    [Fact]
    public async Task ChangePasswordAsync_MissingUser_ReturnsUnauthorized()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteAuthFactory.CreateAsync(ct);
        var sut = CreateAccountSut(factory);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            sut.ChangePasswordAsync(Guid.NewGuid(), new ChangePasswordRequest("Current!1", "NewStrongPassword!1"), ct));

        Assert.Equal(StatusCodes.Status401Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task ChangePasswordAsync_IncorrectCurrentPassword_DoesNotMutateUser()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteAuthFactory.CreateAsync(ct);
        var userId = await SeedUserAsync(factory, ct, passwordHash: "old-hash");
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Verify("wrong", "old-hash").Returns(false);
        var sut = CreateAccountSut(factory, hasher);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            sut.ChangePasswordAsync(userId, new ChangePasswordRequest("wrong", "NewStrongPassword!1"), ct));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        hasher.DidNotReceive().Hash(Arg.Any<string>());
    }

    [Fact]
    public async Task ChangePasswordAsync_ValidRequest_UpdatesHashAndRevokesRefreshTokens()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteAuthFactory.CreateAsync(ct);
        var userId = await SeedUserAsync(factory, ct, passwordHash: "old-hash");
        await using (var db = factory.CreateDbContext())
        {
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.NewGuid(), UserId = userId, TokenHash = "refresh", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1)
            });
            await db.SaveChangesAsync(ct);
        }
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Verify("current", "old-hash").Returns(true);
        hasher.Verify("new-password", "old-hash").Returns(false);
        hasher.Hash("new-password").Returns("new-hash");
        var sut = CreateAccountSut(factory, hasher);

        await sut.ChangePasswordAsync(userId, new ChangePasswordRequest("current", "new-password"), ct);

        await using var verify = factory.CreateDbContext();
        Assert.Equal("new-hash", (await verify.Users.SingleAsync(ct)).PasswordHash);
        Assert.NotNull((await verify.RefreshTokens.SingleAsync(ct)).RevokedAt);
    }

    private static PasswordResetService CreateResetSut(
        IDbContextFactory<AuthDbContext> factory,
        IPasswordHasher? hasher = null,
        IPasswordResetEmailService? email = null)
    {
        var policy = Substitute.For<IPasswordPolicyValidator>();
        policy.Validate(Arg.Any<string>(), Arg.Any<string>()).Returns(Array.Empty<ApiFieldError>());
        return new PasswordResetService(
            factory,
            RegistrationOptions(),
            new PasswordResetOptions(30, new Uri("https://app.example.edu/")),
            policy,
            hasher ?? Substitute.For<IPasswordHasher>(),
            email ?? Substitute.For<IPasswordResetEmailService>(),
            NullLogger<PasswordResetService>.Instance);
    }

    private static UserAccountService CreateAccountSut(IDbContextFactory<AuthDbContext> factory, IPasswordHasher? hasher = null)
    {
        var policy = Substitute.For<IPasswordPolicyValidator>();
        policy.Validate(Arg.Any<string>(), Arg.Any<string>()).Returns(Array.Empty<ApiFieldError>());
        return new UserAccountService(factory, hasher ?? Substitute.For<IPasswordHasher>(), policy);
    }

    private static RegistrationOptions RegistrationOptions() => new(
        true, "student.example.edu", "staff.example.edu", true, "^IT[0-9]{8}$", true, true,
        50, 50, 200, 20, 300, 600);

    private static async Task<Guid> SeedUserAsync(
        SqliteAuthFactory factory,
        CancellationToken ct,
        string passwordHash = "hash")
    {
        var id = Guid.NewGuid();
        await using var db = factory.CreateDbContext();
        db.Users.Add(new User
        {
            Id = id,
            Email = "user@staff.example.edu",
            FirstName = "User",
            LastName = "Example",
            PasswordHash = passwordHash,
            Role = UserRole.Supervisor,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return id;
    }

    private static async Task SeedResetTokenAsync(SqliteAuthFactory factory, Guid userId, string raw, CancellationToken ct)
    {
        await using var db = factory.CreateDbContext();
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(), UserId = userId, TokenHash = Hash(raw), CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        });
        await db.SaveChangesAsync(ct);
    }

    private static string Hash(string raw) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private sealed class SqliteAuthFactory : IDbContextFactory<AuthDbContext>, IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AuthDbContext> _options;
        private SqliteAuthFactory(SqliteConnection connection)
        {
            _connection = connection;
            _options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(connection).Options;
        }
        public static async Task<SqliteAuthFactory> CreateAsync(CancellationToken ct)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(ct);
            var factory = new SqliteAuthFactory(connection);
            await using var db = factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync(ct);
            return factory;
        }
        public AuthDbContext CreateDbContext() => new(_options);
        public Task<AuthDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
