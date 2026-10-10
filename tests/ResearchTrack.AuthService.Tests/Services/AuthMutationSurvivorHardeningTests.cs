using System.Security.Claims;
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
using ResearchTrack.AuthService.Features.Authentication;
using ResearchTrack.AuthService.Features.Passwords;
using ResearchTrack.AuthService.Features.Registration;
using ResearchTrack.AuthService.Features.Users;
using ResearchTrack.AuthService.Infrastructure.Email;
using ResearchTrack.AuthService.Infrastructure.Security;
using ResearchTrack.AuthService.Infrastructure.Tokens;
using ResearchTrack.AuthService.Persistence;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;

namespace ResearchTrack.AuthService.Tests.Services;

public sealed class AuthMutationSurvivorHardeningTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RefreshAsync_BlankToken_IsRejectedBeforeDatabase(string? token)
    {
        var factory = Substitute.For<IDbContextFactory<AuthDbContext>>();
        var sut = CreateAuthenticationSut(factory);

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.RefreshAsync(token!, Ct));

        Assert.Equal(StatusCodes.Status401Unauthorized, exception.StatusCode);
        await factory.DidNotReceive().CreateDbContextAsync(Arg.Any<CancellationToken>());
    }


    [Fact]
    public async Task RefreshAsync_UnknownNonBlankToken_IsRejected()
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        var sut = CreateAuthenticationSut(factory);

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.RefreshAsync("unknown-refresh-token", Ct));

        Assert.Equal(StatusCodes.Status401Unauthorized, exception.StatusCode);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RefreshAsync_RevokedOrExpiredToken_IsRejected(bool revoked, bool expired)
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        var user = await SeedUserAsync(factory, "refresh@staff.example.edu", UserRole.Supervisor, Ct);
        const string raw = "refresh-token-invalid";
        await SeedRefreshTokenAsync(
            factory,
            user.Id,
            raw,
            expired ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(5),
            revoked ? DateTime.UtcNow.AddMinutes(-1) : null,
            Ct);
        var sut = CreateAuthenticationSut(factory);

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.RefreshAsync(raw, Ct));

        Assert.Equal(StatusCodes.Status401Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task RefreshAsync_ValidToken_RotatesOnlyClaimedTokenAndReturnsSupervisorRole()
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        var user = await SeedUserAsync(factory, "refresh@staff.example.edu", UserRole.Supervisor, Ct);
        var other = await SeedUserAsync(factory, "other@staff.example.edu", UserRole.Supervisor, Ct);
        const string raw = "refresh-token-valid";
        await SeedRefreshTokenAsync(factory, user.Id, raw, DateTime.UtcNow.AddMinutes(10), null, Ct);
        await SeedRefreshTokenAsync(factory, other.Id, "other-refresh", DateTime.UtcNow.AddMinutes(10), null, Ct);
        var tokens = Substitute.For<IAccessTokenService>();
        tokens.Generate(Arg.Any<User>()).Returns("access");
        var sut = CreateAuthenticationSut(factory, tokenService: tokens);

        var result = await sut.RefreshAsync(raw, Ct);

        Assert.Equal("access", result.AccessToken);
        Assert.Equal("SUPERVISOR", result.Response.User.Role);
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
        Assert.NotEqual(raw, result.RefreshToken);
        await using var db = factory.CreateDbContext();
        var userTokens = await db.RefreshTokens.Where(x => x.UserId == user.Id).OrderBy(x => x.CreatedAt).ToListAsync(Ct);
        Assert.Equal(2, userTokens.Count);
        Assert.NotNull(userTokens[0].RevokedAt);
        Assert.Null(userTokens[1].RevokedAt);
        Assert.Null((await db.RefreshTokens.SingleAsync(x => x.UserId == other.Id, Ct)).RevokedAt);
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_BlankToken_IsNoOpWithoutDatabase()
    {
        var factory = Substitute.For<IDbContextFactory<AuthDbContext>>();
        var sut = CreateAuthenticationSut(factory);

        await sut.RevokeRefreshTokenAsync("   ", Ct);

        await factory.DidNotReceive().CreateDbContextAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_RevokesOnlyMatchingActiveToken()
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        var user = await SeedUserAsync(factory, "logout@staff.example.edu", UserRole.Supervisor, Ct);
        await SeedRefreshTokenAsync(factory, user.Id, "target", DateTime.UtcNow.AddMinutes(10), null, Ct);
        await SeedRefreshTokenAsync(factory, user.Id, "other", DateTime.UtcNow.AddMinutes(10), null, Ct);
        await SeedRefreshTokenAsync(factory, user.Id, "already-revoked", DateTime.UtcNow.AddMinutes(10), DateTime.UtcNow.AddMinutes(-2), Ct);
        var sut = CreateAuthenticationSut(factory);

        await sut.RevokeRefreshTokenAsync("target", Ct);

        await using var db = factory.CreateDbContext();
        Assert.NotNull((await db.RefreshTokens.SingleAsync(x => x.TokenHash == Hash("target"), Ct)).RevokedAt);
        Assert.Null((await db.RefreshTokens.SingleAsync(x => x.TokenHash == Hash("other"), Ct)).RevokedAt);
        Assert.NotNull((await db.RefreshTokens.SingleAsync(x => x.TokenHash == Hash("already-revoked"), Ct)).RevokedAt);
    }


    [Fact]
    public async Task RevokeRefreshTokenAsync_AlreadyRevokedMatchingToken_PreservesOriginalRevocationTime()
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        var user = await SeedUserAsync(factory, "revoked@staff.example.edu", UserRole.Supervisor, Ct);
        var original = DateTime.UtcNow.AddMinutes(-15);
        await SeedRefreshTokenAsync(factory, user.Id, "already-revoked-target", DateTime.UtcNow.AddMinutes(10), original, Ct);
        var sut = CreateAuthenticationSut(factory);

        await sut.RevokeRefreshTokenAsync("already-revoked-target", Ct);

        await using var db = factory.CreateDbContext();
        Assert.Equal(original, (await db.RefreshTokens.SingleAsync(Ct)).RevokedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task GetCurrentUserAsync_InvalidSubject_IsRejectedBeforeDatabase(string? subject)
    {
        var factory = Substitute.For<IDbContextFactory<AuthDbContext>>();
        var sut = CreateAuthenticationSut(factory);
        var principal = Principal(subject);

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.GetCurrentUserAsync(principal, Ct));

        Assert.Equal(StatusCodes.Status401Unauthorized, exception.StatusCode);
        await factory.DidNotReceive().CreateDbContextAsync(Arg.Any<CancellationToken>());
    }


    [Fact]
    public async Task GetCurrentUserAsync_ValidSubjectForMissingUser_IsRejected()
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        var sut = CreateAuthenticationSut(factory);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            sut.GetCurrentUserAsync(Principal(Guid.NewGuid().ToString()), Ct));

        Assert.Equal(StatusCodes.Status401Unauthorized, exception.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Student, "STUDENT")]
    [InlineData(UserRole.Supervisor, "SUPERVISOR")]
    public async Task GetCurrentUserAsync_ExistingUser_MapsRoleExactly(UserRole role, string expectedRole)
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        var user = await SeedUserAsync(factory, $"{role.ToString().ToLowerInvariant()}@example.edu", role, Ct);
        var sut = CreateAuthenticationSut(factory);

        var result = await sut.GetCurrentUserAsync(Principal(user.Id.ToString()), Ct);

        Assert.Equal(user.Id, result.User.Id);
        Assert.Equal(expectedRole, result.User.Role);
    }

    [Fact]
    public async Task ValidateTokenAsync_UsedAndExpiredTokens_AreRejectedIndependently()
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        var user = await SeedUserAsync(factory, "reset@staff.example.edu", UserRole.Supervisor, Ct);
        await SeedResetTokenAsync(factory, user.Id, "used", DateTime.UtcNow.AddMinutes(10), DateTime.UtcNow.AddMinutes(-1), Ct);
        await SeedResetTokenAsync(factory, user.Id, "expired", DateTime.UtcNow.AddMinutes(-1), null, Ct);
        var sut = CreatePasswordResetSut(factory);

        Assert.False((await sut.ValidateTokenAsync("used", Ct)).Valid);
        Assert.False((await sut.ValidateTokenAsync("expired", Ct)).Valid);
    }

    [Fact]
    public async Task ResetPasswordAsync_Success_InvalidatesOtherUserTokensButLeavesOtherUsersUntouched()
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        var user = await SeedUserAsync(factory, "reset@staff.example.edu", UserRole.Supervisor, Ct, "old-hash");
        var other = await SeedUserAsync(factory, "other@staff.example.edu", UserRole.Supervisor, Ct, "other-hash");
        await SeedResetTokenAsync(factory, user.Id, "claim", DateTime.UtcNow.AddMinutes(10), null, Ct);
        await SeedResetTokenAsync(factory, user.Id, "second", DateTime.UtcNow.AddMinutes(10), null, Ct);
        await SeedResetTokenAsync(factory, other.Id, "other-reset", DateTime.UtcNow.AddMinutes(10), null, Ct);
        await SeedRefreshTokenAsync(factory, user.Id, "user-refresh", DateTime.UtcNow.AddMinutes(10), null, Ct);
        await SeedRefreshTokenAsync(factory, other.Id, "other-refresh", DateTime.UtcNow.AddMinutes(10), null, Ct);
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Verify("NewStrongPassword!1", "old-hash").Returns(false);
        hasher.Hash("NewStrongPassword!1").Returns("new-hash");
        var sut = CreatePasswordResetSut(factory, hasher);

        await sut.ResetPasswordAsync(new ResetPasswordRequest("claim", "NewStrongPassword!1"), Ct);

        await using var db = factory.CreateDbContext();
        Assert.All(await db.PasswordResetTokens.Where(x => x.UserId == user.Id).ToListAsync(Ct), x => Assert.NotNull(x.UsedAt));
        Assert.Null((await db.PasswordResetTokens.SingleAsync(x => x.UserId == other.Id, Ct)).UsedAt);
        Assert.NotNull((await db.RefreshTokens.SingleAsync(x => x.UserId == user.Id, Ct)).RevokedAt);
        Assert.Null((await db.RefreshTokens.SingleAsync(x => x.UserId == other.Id, Ct)).RevokedAt);
    }


    [Fact]
    public async Task InitRegistrationAsync_EmailDeliveryFailure_RollsBackOtp()
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        var email = Substitute.For<IRegistrationEmailService>();
        email.SendOtpEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Ct)
            .Returns(_ => Task.FromException(new InvalidOperationException("mail unavailable")));
        var sut = CreateRegistrationSut(factory, email: email);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.InitRegistrationAsync("delivery@staff.example.edu", Ct));

        await using var db = factory.CreateDbContext();
        Assert.Empty(await db.EmailOtps.ToListAsync(Ct));
    }

    [Fact]
    public async Task CompleteRegistrationAsync_UnresolvedSessionRole_UsesRequestedSupervisorRole()
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        await SeedRegistrationSessionAsync(factory, "role-session", "role@staff.example.edu", null,
            DateTime.UtcNow.AddMinutes(10), null, Ct);
        var sut = CreateRegistrationSut(factory);
        var request = new RegisterCompleteRequest
        {
            RegistrationToken = "token_role-session",
            Fname = "Role",
            Lname = "Selected",
            Password = "StrongPassword!1",
            Role = " supervisor ",
            Name = null
        };

        var result = await sut.CompleteRegistrationAsync(request, Ct);

        Assert.Equal("SUPERVISOR", result.Response.User.Role);
        await using var db = factory.CreateDbContext();
        var user = await db.Users.SingleAsync(Ct);
        Assert.Equal(UserRole.Supervisor, user.Role);
        Assert.Null(user.RegistrationNumber);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_StudentRegistrationMustMatchEmailIdentity()
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        await SeedRegistrationSessionAsync(factory, "student-mismatch", "IT20260001@student.example.edu", UserRole.Student,
            DateTime.UtcNow.AddMinutes(10), null, Ct);
        var sut = CreateRegistrationSut(factory);
        var request = Completion("student-mismatch", "IT20260002");

        var exception = await Assert.ThrowsAsync<ApiValidationException>(() => sut.CompleteRegistrationAsync(request, Ct));

        Assert.Contains(exception.FieldErrors, x => x.Field == "name");
        await using var db = factory.CreateDbContext();
        Assert.Empty(await db.Users.ToListAsync(Ct));
    }

    [Fact]
    public async Task CompleteRegistrationAsync_DuplicateSessionEmail_IsRejectedBeforeAccountCreation()
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        await SeedUserAsync(factory, "duplicate@staff.example.edu", UserRole.Supervisor, Ct);
        await SeedRegistrationSessionAsync(factory, "duplicate-email-session", "duplicate@staff.example.edu", UserRole.Supervisor,
            DateTime.UtcNow.AddMinutes(10), null, Ct);
        var sut = CreateRegistrationSut(factory);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            sut.CompleteRegistrationAsync(Completion("duplicate-email-session"), Ct));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        await using var db = factory.CreateDbContext();
        Assert.Single(await db.Users.ToListAsync(Ct));
    }

    [Fact]
    public async Task VerifyOtpAsync_UsesNewestActiveOtp_NotOldest()
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        const string email = "otp@staff.example.edu";
        await using (var db = factory.CreateDbContext())
        {
            db.EmailOtps.AddRange(
                new EmailOtp
                {
                    Id = Guid.NewGuid(), Email = email, OtpHash = Hash("111111"),
                    CreatedAt = DateTime.UtcNow.AddMinutes(-5), ExpiresAt = DateTime.UtcNow.AddMinutes(10)
                },
                new EmailOtp
                {
                    Id = Guid.NewGuid(), Email = email, OtpHash = Hash("222222"),
                    CreatedAt = DateTime.UtcNow.AddMinutes(-1), ExpiresAt = DateTime.UtcNow.AddMinutes(10)
                });
            await db.SaveChangesAsync(Ct);
        }
        var sut = CreateRegistrationSut(factory);

        var result = await sut.VerifyOtpAsync(email, "222222", Ct);

        Assert.False(string.IsNullOrWhiteSpace(result.RegistrationToken));
        await using var verify = factory.CreateDbContext();
        var otps = await verify.EmailOtps.OrderBy(x => x.CreatedAt).ToListAsync(Ct);
        Assert.Null(otps[0].UsedAt);
        Assert.NotNull(otps[1].UsedAt);
    }

    [Fact]
    public async Task VerifyOtpAsync_UsedOrExpiredMatchingOtp_IsRejected()
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        const string email = "otp@staff.example.edu";
        await using (var db = factory.CreateDbContext())
        {
            db.EmailOtps.AddRange(
                new EmailOtp
                {
                    Id = Guid.NewGuid(), Email = email, OtpHash = Hash("111111"), UsedAt = DateTime.UtcNow.AddMinutes(-1),
                    CreatedAt = DateTime.UtcNow.AddMinutes(-2), ExpiresAt = DateTime.UtcNow.AddMinutes(10)
                },
                new EmailOtp
                {
                    Id = Guid.NewGuid(), Email = email, OtpHash = Hash("222222"),
                    CreatedAt = DateTime.UtcNow.AddMinutes(-3), ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
                });
            await db.SaveChangesAsync(Ct);
        }
        var sut = CreateRegistrationSut(factory);

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.VerifyOtpAsync(email, "111111", Ct));
        await Assert.ThrowsAsync<ApiValidationException>(() => sut.VerifyOtpAsync(email, "222222", Ct));
    }

    [Fact]
    public async Task CompleteRegistrationAsync_UsedAndExpiredSessions_AreRejectedWithoutCreatingUser()
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        await SeedRegistrationSessionAsync(factory, "used-session", "used@staff.example.edu", UserRole.Supervisor,
            DateTime.UtcNow.AddMinutes(10), DateTime.UtcNow.AddMinutes(-1), Ct);
        await SeedRegistrationSessionAsync(factory, "expired-session", "expired@staff.example.edu", UserRole.Supervisor,
            DateTime.UtcNow.AddMinutes(-1), null, Ct);
        var sut = CreateRegistrationSut(factory);

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.CompleteRegistrationAsync(Completion("used-session"), Ct));
        await Assert.ThrowsAsync<ApiValidationException>(() => sut.CompleteRegistrationAsync(Completion("expired-session"), Ct));

        await using var db = factory.CreateDbContext();
        Assert.Empty(await db.Users.ToListAsync(Ct));
    }

    [Fact]
    public async Task CompleteRegistrationAsync_StudentDuplicateRegistrationNumber_IsRejected()
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        await SeedUserAsync(factory, "legacy@student.example.edu", UserRole.Student, Ct, registrationNumber: "IT20260001");
        await SeedRegistrationSessionAsync(factory, "student-session", "IT20260001@student.example.edu", UserRole.Student,
            DateTime.UtcNow.AddMinutes(10), null, Ct);
        var sut = CreateRegistrationSut(factory);
        var request = Completion("student-session", "IT20260001");

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.CompleteRegistrationAsync(request, Ct));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
    }

    [Fact]
    public async Task CleanupExpiredSessionsAndOtpsAsync_DeletesExpiredOrUsed_AndPreservesActive()
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        await using (var db = factory.CreateDbContext())
        {
            db.EmailOtps.AddRange(
                Otp("expired@x.test", "111111", DateTime.UtcNow.AddMinutes(-1), null),
                Otp("used@x.test", "222222", DateTime.UtcNow.AddMinutes(10), DateTime.UtcNow.AddMinutes(-1)),
                Otp("active@x.test", "333333", DateTime.UtcNow.AddMinutes(10), null));
            db.RegistrationSessions.AddRange(
                Session("expired-session", "expired@x.test", DateTime.UtcNow.AddMinutes(-1), null),
                Session("used-session", "used@x.test", DateTime.UtcNow.AddMinutes(10), DateTime.UtcNow.AddMinutes(-1)),
                Session("active-session", "active@x.test", DateTime.UtcNow.AddMinutes(10), null));
            await db.SaveChangesAsync(Ct);
        }
        var sut = CreateRegistrationSut(factory);

        await sut.CleanupExpiredSessionsAndOtpsAsync(Ct);

        await using var verify = factory.CreateDbContext();
        Assert.Equal("active@x.test", (await verify.EmailOtps.SingleAsync(Ct)).Email);
        Assert.Equal("active@x.test", (await verify.RegistrationSessions.SingleAsync(Ct)).Email);
    }

    [Fact]
    public async Task ChangePasswordAsync_RevokesOnlyCurrentUsersActiveRefreshTokens()
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        var user = await SeedUserAsync(factory, "account@staff.example.edu", UserRole.Supervisor, Ct, "old-hash");
        var other = await SeedUserAsync(factory, "other-account@staff.example.edu", UserRole.Supervisor, Ct, "other-hash");
        await SeedRefreshTokenAsync(factory, user.Id, "current-active", DateTime.UtcNow.AddMinutes(10), null, Ct);
        var priorRevokedAt = DateTime.UtcNow.AddMinutes(-10);
        await SeedRefreshTokenAsync(factory, user.Id, "current-revoked", DateTime.UtcNow.AddMinutes(10), priorRevokedAt, Ct);
        await SeedRefreshTokenAsync(factory, other.Id, "other-active", DateTime.UtcNow.AddMinutes(10), null, Ct);
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Verify("current", "old-hash").Returns(true);
        hasher.Verify("new", "old-hash").Returns(false);
        hasher.Hash("new").Returns("new-hash");
        var sut = CreateAccountSut(factory, hasher);

        await sut.ChangePasswordAsync(user.Id, new ChangePasswordRequest("current", "new"), Ct);

        await using var db = factory.CreateDbContext();
        Assert.NotNull((await db.RefreshTokens.SingleAsync(x => x.TokenHash == Hash("current-active"), Ct)).RevokedAt);
        Assert.Equal(priorRevokedAt, (await db.RefreshTokens.SingleAsync(x => x.TokenHash == Hash("current-revoked"), Ct)).RevokedAt);
        Assert.Null((await db.RefreshTokens.SingleAsync(x => x.TokenHash == Hash("other-active"), Ct)).RevokedAt);
    }

    [Fact]
    public async Task SearchStudentsAsync_MatchesEachSupportedFieldAndPreservesOrdering()
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        var aSmith = await SeedUserAsync(factory, "alpha@students.example.edu", UserRole.Student, Ct, first: "Alice", last: "Smith", registrationNumber: "IT100001");
        var aBrown = await SeedUserAsync(factory, "beta@students.example.edu", UserRole.Student, Ct, first: "Alice", last: "Brown", registrationNumber: "IT200002");
        var bob = await SeedUserAsync(factory, "match-email@students.example.edu", UserRole.Student, Ct, first: "Bob", last: "Zulu", registrationNumber: "IT300003");
        var sut = new UserDirectoryService(factory);

        Assert.Equal(aBrown.Id, Assert.Single(await sut.SearchStudentsAsync("Brown", Ct)).Id);
        Assert.Equal(aSmith.Id, Assert.Single(await sut.SearchStudentsAsync("100001", Ct)).Id);
        Assert.Equal(bob.Id, Assert.Single(await sut.SearchStudentsAsync("match-email", Ct)).Id);
        var alice = await sut.SearchStudentsAsync("Alice", Ct);
        Assert.Equal(new[] { aBrown.Id, aSmith.Id }, alice.Select(x => x.Id).ToArray());
        Assert.All(alice, x => Assert.Equal("STUDENT", x.Role));
    }

    [Fact]
    public async Task ResolveStudentsAsync_OrdersByFirstThenLastName()
    {
        await using var factory = await SqliteAuthFactory.CreateAsync(Ct);
        var zed = await SeedUserAsync(factory, "z@students.example.edu", UserRole.Student, Ct, first: "Zed", last: "Alpha", registrationNumber: "IT400004");
        var aSmith = await SeedUserAsync(factory, "as@students.example.edu", UserRole.Student, Ct, first: "Alice", last: "Smith", registrationNumber: "IT500005");
        var aBrown = await SeedUserAsync(factory, "ab@students.example.edu", UserRole.Student, Ct, first: "Alice", last: "Brown", registrationNumber: "IT600006");
        var sut = new UserDirectoryService(factory);

        var result = await sut.ResolveStudentsAsync([zed.Id, aSmith.Id, aBrown.Id], Ct);

        Assert.Equal(new[] { aBrown.Id, aSmith.Id, zed.Id }, result.Select(x => x.Id).ToArray());
        Assert.All(result, x => Assert.Equal("STUDENT", x.Role));
    }

    private static UserAuthenticationService CreateAuthenticationSut(
        IDbContextFactory<AuthDbContext> factory,
        IPasswordHasher? hasher = null,
        IAccessTokenService? tokenService = null) =>
        new(factory,
            hasher ?? Substitute.For<IPasswordHasher>(),
            new InvalidPasswordTimingGuard(hasher ?? Substitute.For<IPasswordHasher>()),
            tokenService ?? Substitute.For<IAccessTokenService>(),
            new JwtOptions("issuer", "audience", new string('k', 64), 15, 7));

    private static PasswordResetService CreatePasswordResetSut(
        IDbContextFactory<AuthDbContext> factory,
        IPasswordHasher? hasher = null)
    {
        var policy = Substitute.For<IPasswordPolicyValidator>();
        policy.Validate(Arg.Any<string>(), Arg.Any<string>()).Returns(Array.Empty<ApiFieldError>());
        return new PasswordResetService(
            factory,
            RegistrationOptions(),
            new PasswordResetOptions(30, new Uri("https://app.example.edu/")),
            policy,
            hasher ?? Substitute.For<IPasswordHasher>(),
            Substitute.For<IPasswordResetEmailService>(),
            NullLogger<PasswordResetService>.Instance);
    }

    private static RegistrationService CreateRegistrationSut(
        IDbContextFactory<AuthDbContext> factory,
        IRegistrationEmailService? email = null)
    {
        var policy = Substitute.For<IPasswordPolicyValidator>();
        policy.Validate(Arg.Any<string>(), Arg.Any<string>()).Returns(Array.Empty<ApiFieldError>());
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Hash(Arg.Any<string>()).Returns("hash");
        return new RegistrationService(
            factory,
            RegistrationOptions(),
            policy,
            new JwtOptions("issuer", "audience", new string('x', 64), 15, 30),
            hasher,
            Substitute.For<IAccessTokenService>(),
            email ?? Substitute.For<IRegistrationEmailService>(),
            NullLogger<RegistrationService>.Instance);
    }

    private static UserAccountService CreateAccountSut(IDbContextFactory<AuthDbContext> factory, IPasswordHasher hasher)
    {
        var policy = Substitute.For<IPasswordPolicyValidator>();
        policy.Validate(Arg.Any<string>(), Arg.Any<string>()).Returns(Array.Empty<ApiFieldError>());
        return new UserAccountService(factory, hasher, policy);
    }

    private static RegistrationOptions RegistrationOptions() => new(
        true, "student.example.edu", "staff.example.edu", true, "^IT[0-9]{8}$", true, true,
        50, 50, 200, 20, 300, 600);

    private static RegisterCompleteRequest Completion(string rawToken, string? registrationNumber = null) => new()
    {
        RegistrationToken = "token_" + rawToken,
        Fname = "Valid",
        Lname = "User",
        Password = "StrongPassword!1",
        Name = registrationNumber
    };

    private static ClaimsPrincipal Principal(string? subject)
    {
        Claim[] claims = subject is null ? Array.Empty<Claim>() : new[] { new Claim(AuthSecurityConstants.SubjectClaim, subject) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static async Task<User> SeedUserAsync(
        SqliteAuthFactory factory,
        string email,
        UserRole role,
        CancellationToken ct,
        string passwordHash = "hash",
        string? first = null,
        string? last = null,
        string? registrationNumber = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(), Email = email, FirstName = first ?? "First", LastName = last ?? "Last",
            PasswordHash = passwordHash, Role = role, RegistrationNumber = registrationNumber, CreatedAt = DateTime.UtcNow
        };
        await using var db = factory.CreateDbContext();
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    private static async Task SeedRefreshTokenAsync(
        SqliteAuthFactory factory, Guid userId, string raw, DateTime expiresAt, DateTime? revokedAt, CancellationToken ct)
    {
        await using var db = factory.CreateDbContext();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = userId, TokenHash = Hash(raw), CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            ExpiresAt = expiresAt, RevokedAt = revokedAt
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedResetTokenAsync(
        SqliteAuthFactory factory, Guid userId, string raw, DateTime expiresAt, DateTime? usedAt, CancellationToken ct)
    {
        await using var db = factory.CreateDbContext();
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(), UserId = userId, TokenHash = Hash(raw), CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            ExpiresAt = expiresAt, UsedAt = usedAt
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedRegistrationSessionAsync(
        SqliteAuthFactory factory, string raw, string email, UserRole? role, DateTime expiresAt, DateTime? usedAt, CancellationToken ct)
    {
        await using var db = factory.CreateDbContext();
        db.RegistrationSessions.Add(new RegistrationSession
        {
            Id = Guid.NewGuid(), TokenHash = Hash(raw), Email = email, Role = role,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1), ExpiresAt = expiresAt, UsedAt = usedAt
        });
        await db.SaveChangesAsync(ct);
    }

    private static EmailOtp Otp(string email, string raw, DateTime expiresAt, DateTime? usedAt) => new()
    {
        Id = Guid.NewGuid(), Email = email, OtpHash = Hash(raw), CreatedAt = DateTime.UtcNow.AddMinutes(-2),
        ExpiresAt = expiresAt, UsedAt = usedAt
    };

    private static RegistrationSession Session(string raw, string email, DateTime expiresAt, DateTime? usedAt) => new()
    {
        Id = Guid.NewGuid(), TokenHash = Hash(raw), Email = email, Role = UserRole.Supervisor,
        CreatedAt = DateTime.UtcNow.AddMinutes(-2), ExpiresAt = expiresAt, UsedAt = usedAt
    };

    private static string Hash(string raw) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

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
