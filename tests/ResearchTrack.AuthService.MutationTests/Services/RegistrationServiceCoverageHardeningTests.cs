using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ResearchTrack.AuthService.Configuration;
using ResearchTrack.AuthService.Contracts;
using ResearchTrack.AuthService.Domain;
using ResearchTrack.AuthService.Features.Passwords;
using ResearchTrack.AuthService.Features.Registration;
using ResearchTrack.AuthService.Infrastructure.Email;
using ResearchTrack.AuthService.Infrastructure.Security;
using ResearchTrack.AuthService.Infrastructure.Tokens;
using ResearchTrack.AuthService.Persistence;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;

namespace ResearchTrack.AuthService.MutationTests.Services;

public sealed class RegistrationServiceCoverageHardeningTests
{
    [Fact]
    public async Task RegisterAsync_ValidSupervisor_NormalizesAndPersistsServerAssignedRole()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteAuthDbContextFactory.CreateAsync(ct);
        var passwordPolicy = PassingPasswordPolicy();
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Hash("StrongPassword!1").Returns("hashed");
        var sut = CreateSut(factory, passwordPolicy, hasher);

        var response = await sut.RegisterAsync(new RegisterRequest
        {
            FirstName = "  Ada ",
            LastName = " Lovelace  ",
            Email = "  ADA@STAFF.EXAMPLE.EDU ",
            Password = "StrongPassword!1",
            Role = "student"
        }, ct);

        Assert.Equal("ada@staff.example.edu", response.Email);
        Assert.Equal("Ada", response.FirstName);
        Assert.Equal("Lovelace", response.LastName);
        Assert.Equal("SUPERVISOR", response.Role);
        Assert.Null(response.RegistrationNumber);
        hasher.Received(1).Hash("StrongPassword!1");
        await using var db = factory.CreateDbContext();
        var user = await db.Users.SingleAsync(ct);
        Assert.Equal(UserRole.Supervisor, user.Role);
        Assert.Equal("hashed", user.PasswordHash);
    }

    [Fact]
    public async Task RegisterAsync_ValidStudent_UsesRegistrationNumberAndIgnoresClientRole()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteAuthDbContextFactory.CreateAsync(ct);
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Hash(Arg.Any<string>()).Returns("hash");
        var sut = CreateSut(factory, PassingPasswordPolicy(), hasher);

        var response = await sut.RegisterAsync(new RegisterRequest
        {
            FirstName = "Student",
            LastName = "One",
            Email = "IT20260001@student.example.edu",
            Password = "StrongPassword!1",
            RegistrationNumber = "it20260001",
            Role = "supervisor"
        }, ct);

        Assert.Equal("STUDENT", response.Role);
        Assert.Equal("IT20260001", response.RegistrationNumber);
        await using var db = factory.CreateDbContext();
        var user = await db.Users.SingleAsync(ct);
        Assert.Equal(UserRole.Student, user.Role);
        Assert.Equal("IT20260001", user.RegistrationNumber);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_ReturnsConflictWithoutHashingSecondUser()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteAuthDbContextFactory.CreateAsync(ct);
        await SeedUserAsync(factory, "supervisor@staff.example.edu", UserRole.Supervisor, null, ct);
        var hasher = Substitute.For<IPasswordHasher>();
        var sut = CreateSut(factory, PassingPasswordPolicy(), hasher);

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.RegisterAsync(new RegisterRequest
        {
            FirstName = "Duplicate",
            LastName = "User",
            Email = "SUPERVISOR@STAFF.EXAMPLE.EDU",
            Password = "StrongPassword!1"
        }, ct));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        hasher.DidNotReceive().Hash(Arg.Any<string>());
    }

    [Fact]
    public async Task RegisterAsync_DuplicateStudentRegistrationNumber_ReturnsConflict()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteAuthDbContextFactory.CreateAsync(ct);
        await SeedUserAsync(factory, "legacy@student.example.edu", UserRole.Student, "IT20260001", ct);
        var sut = CreateSut(factory, PassingPasswordPolicy(), Substitute.For<IPasswordHasher>());

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.RegisterAsync(new RegisterRequest
        {
            FirstName = "Student",
            LastName = "Two",
            Email = "IT20260001@student.example.edu",
            Password = "StrongPassword!1",
            RegistrationNumber = "IT20260001"
        }, ct));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
    }

    [Theory]
    [InlineData("outside@example.org")]
    [InlineData("not-an-email")]
    public async Task RegisterAsync_InvalidEmail_FailsBeforeDatabaseAndHashing(string email)
    {
        var ct = CancellationToken.None;
        var factory = Substitute.For<IDbContextFactory<AuthDbContext>>();
        var hasher = Substitute.For<IPasswordHasher>();
        var sut = CreateSut(factory, PassingPasswordPolicy(), hasher);

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.RegisterAsync(new RegisterRequest
        {
            FirstName = "User",
            LastName = "Name",
            Email = email,
            Password = "StrongPassword!1"
        }, ct));

        await factory.DidNotReceive().CreateDbContextAsync(ct);
        hasher.DidNotReceive().Hash(Arg.Any<string>());
    }

    [Fact]
    public async Task RegisterAsync_PasswordPolicyFailure_FailsBeforeDatabase()
    {
        var ct = CancellationToken.None;
        var factory = Substitute.For<IDbContextFactory<AuthDbContext>>();
        var policy = Substitute.For<IPasswordPolicyValidator>();
        policy.Validate("weak", "password").Returns(new[] { new ApiFieldError("password", ["Password is too weak."]) });
        var sut = CreateSut(factory, policy, Substitute.For<IPasswordHasher>());

        var exception = await Assert.ThrowsAsync<ApiValidationException>(() => sut.RegisterAsync(new RegisterRequest
        {
            FirstName = "User",
            LastName = "Name",
            Email = "user@staff.example.edu",
            Password = "weak"
        }, ct));

        Assert.Contains(exception.FieldErrors, error => error.Field == "password");
        await factory.DidNotReceive().CreateDbContextAsync(ct);
    }

    [Fact]
    public async Task InitRegistrationAsync_ExistingUser_ReturnsConflictWithoutSendingOtp()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteAuthDbContextFactory.CreateAsync(ct);
        await SeedUserAsync(factory, "existing@staff.example.edu", UserRole.Supervisor, null, ct);
        var email = Substitute.For<IRegistrationEmailService>();
        var sut = CreateSut(factory, PassingPasswordPolicy(), Substitute.For<IPasswordHasher>(), email: email);

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.InitRegistrationAsync("existing@staff.example.edu", ct));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        await email.DidNotReceive().SendOtpEmailAsync(Arg.Any<string>(), Arg.Any<string>(), ct);
    }

    [Fact]
    public async Task InitRegistrationAsync_ValidEmail_PersistsOtpAndSendsSixDigitCode()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteAuthDbContextFactory.CreateAsync(ct);
        var email = Substitute.For<IRegistrationEmailService>();
        var sut = CreateSut(factory, PassingPasswordPolicy(), Substitute.For<IPasswordHasher>(), email: email);

        await sut.InitRegistrationAsync("new@staff.example.edu", ct);

        await email.Received(1).SendOtpEmailAsync(
            "new@staff.example.edu",
            Arg.Is<string>(otp => otp.Length == 6 && otp.All(char.IsDigit)),
            ct);
        await using var db = factory.CreateDbContext();
        var stored = await db.EmailOtps.SingleAsync(ct);
        Assert.Equal("new@staff.example.edu", stored.Email);
        Assert.NotEmpty(stored.OtpHash);
        Assert.True(stored.ExpiresAt > stored.CreatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("abcdef")]
    public async Task VerifyOtpAsync_InvalidOtpShape_FailsBeforeDatabase(string? otp)
    {
        var ct = CancellationToken.None;
        var factory = Substitute.For<IDbContextFactory<AuthDbContext>>();
        var sut = CreateSut(factory, PassingPasswordPolicy(), Substitute.For<IPasswordHasher>());

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.VerifyOtpAsync("user@staff.example.edu", otp, ct));

        await factory.DidNotReceive().CreateDbContextAsync(ct);
    }

    [Fact]
    public async Task VerifyOtpAsync_ValidStoredOtp_MarksOtpUsedAndCreatesRegistrationSession()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteAuthDbContextFactory.CreateAsync(ct);
        const string otp = "123456";
        await using (var db = factory.CreateDbContext())
        {
            db.EmailOtps.Add(new EmailOtp
            {
                Id = Guid.NewGuid(),
                Email = "user@staff.example.edu",
                OtpHash = Sha256Base64(otp),
                CreatedAt = DateTime.UtcNow.AddMinutes(-1),
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            });
            await db.SaveChangesAsync(ct);
        }
        var sut = CreateSut(factory, PassingPasswordPolicy(), Substitute.For<IPasswordHasher>());

        var response = await sut.VerifyOtpAsync("user@staff.example.edu", otp, ct);

        Assert.StartsWith("token_", response.RegistrationToken);
        Assert.False(response.RequiresRoleSelection);
        Assert.Equal("SUPERVISOR", response.Role);
        await using var verifyDb = factory.CreateDbContext();
        Assert.NotNull((await verifyDb.EmailOtps.SingleAsync(ct)).UsedAt);
        Assert.Single(await verifyDb.RegistrationSessions.ToListAsync(ct));
    }

    [Fact]
    public async Task CompleteRegistrationAsync_ValidSupervisorSession_CreatesUserRefreshTokenAndAccessToken()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteAuthDbContextFactory.CreateAsync(ct);
        const string rawToken = "registration-raw-token";
        await SeedSessionAsync(factory, rawToken, "complete@staff.example.edu", UserRole.Supervisor, ct);
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Hash("StrongPassword!1").Returns("hashed-password");
        var access = Substitute.For<IAccessTokenService>();
        access.Generate(Arg.Any<User>()).Returns("access-token");
        var email = Substitute.For<IRegistrationEmailService>();
        var sut = CreateSut(factory, PassingPasswordPolicy(), hasher, access, email);

        var result = await sut.CompleteRegistrationAsync(new RegisterCompleteRequest
        {
            RegistrationToken = "token_" + rawToken,
            Fname = "Complete",
            Lname = "User",
            Password = "StrongPassword!1",
            Role = "student"
        }, ct);

        Assert.Equal("access-token", result.AccessToken);
        Assert.NotEmpty(result.RefreshToken);
        Assert.Equal("SUPERVISOR", result.Response.User.Role);
        access.Received(1).Generate(Arg.Is<User>(user => user.Email == "complete@staff.example.edu"));
        await email.Received(1).SendRegistrationSuccessEmailAsync("complete@staff.example.edu", "Complete", ct);
        await using var db = factory.CreateDbContext();
        Assert.Single(await db.Users.ToListAsync(ct));
        Assert.Single(await db.RefreshTokens.ToListAsync(ct));
        Assert.NotNull((await db.RegistrationSessions.SingleAsync(ct)).UsedAt);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_SuccessEmailFailure_DoesNotRollbackCreatedAccount()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteAuthDbContextFactory.CreateAsync(ct);
        const string rawToken = "mail-failure-token";
        await SeedSessionAsync(factory, rawToken, "mailfail@staff.example.edu", UserRole.Supervisor, ct);
        var email = Substitute.For<IRegistrationEmailService>();
        email.SendRegistrationSuccessEmailAsync("mailfail@staff.example.edu", "Mail", ct)
            .Returns(_ => Task.FromException(new InvalidOperationException("mail unavailable")));
        var access = Substitute.For<IAccessTokenService>();
        access.Generate(Arg.Any<User>()).Returns("access");
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Hash(Arg.Any<string>()).Returns("hash");
        var sut = CreateSut(factory, PassingPasswordPolicy(), hasher, access, email);

        var result = await sut.CompleteRegistrationAsync(new RegisterCompleteRequest
        {
            RegistrationToken = rawToken,
            Fname = "Mail",
            Lname = "Failure",
            Password = "StrongPassword!1"
        }, ct);

        Assert.Equal("access", result.AccessToken);
        await using var db = factory.CreateDbContext();
        Assert.Single(await db.Users.ToListAsync(ct));
    }

    private static RegistrationService CreateSut(
        IDbContextFactory<AuthDbContext> factory,
        IPasswordPolicyValidator policy,
        IPasswordHasher hasher,
        IAccessTokenService? access = null,
        IRegistrationEmailService? email = null) =>
        new(
            factory,
            Options(),
            policy,
            new JwtOptions("issuer", "audience", new string('x', 64), 15, 30),
            hasher,
            access ?? Substitute.For<IAccessTokenService>(),
            email ?? Substitute.For<IRegistrationEmailService>(),
            NullLogger<RegistrationService>.Instance);

    private static IPasswordPolicyValidator PassingPasswordPolicy()
    {
        var policy = Substitute.For<IPasswordPolicyValidator>();
        policy.Validate(Arg.Any<string>(), Arg.Any<string>()).Returns(Array.Empty<ApiFieldError>());
        return policy;
    }

    private static RegistrationOptions Options() => new(
        true,
        "student.example.edu",
        "staff.example.edu",
        true,
        "^IT[0-9]{8}$",
        true,
        true,
        50,
        50,
        200,
        20,
        300,
        600);

    private static async Task SeedUserAsync(
        SqliteAuthDbContextFactory factory,
        string email,
        UserRole role,
        string? registrationNumber,
        CancellationToken ct)
    {
        await using var db = factory.CreateDbContext();
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = email.ToLowerInvariant(),
            FirstName = "Existing",
            LastName = "User",
            PasswordHash = "hash",
            Role = role,
            RegistrationNumber = registrationNumber,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedSessionAsync(
        SqliteAuthDbContextFactory factory,
        string rawToken,
        string email,
        UserRole role,
        CancellationToken ct)
    {
        await using var db = factory.CreateDbContext();
        db.RegistrationSessions.Add(new RegistrationSession
        {
            Id = Guid.NewGuid(),
            TokenHash = Sha256Base64(rawToken),
            Email = email,
            Role = role,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });
        await db.SaveChangesAsync(ct);
    }

    private static string Sha256Base64(string raw) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private sealed class SqliteAuthDbContextFactory : IDbContextFactory<AuthDbContext>, IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AuthDbContext> _options;

        private SqliteAuthDbContextFactory(SqliteConnection connection)
        {
            _connection = connection;
            _options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(connection).Options;
        }

        public static async Task<SqliteAuthDbContextFactory> CreateAsync(CancellationToken ct)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(ct);
            var factory = new SqliteAuthDbContextFactory(connection);
            await using var db = factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync(ct);
            return factory;
        }

        public AuthDbContext CreateDbContext() => new(_options);
        public Task<AuthDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
