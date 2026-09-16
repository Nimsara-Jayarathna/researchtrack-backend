using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResearchTrack.AuthService.Contracts;
using ResearchTrack.AuthService.Domain;
using ResearchTrack.AuthService.Infrastructure.Email;
using ResearchTrack.AuthService.Infrastructure.Security;
using ResearchTrack.AuthService.Persistence;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.Testing;

namespace ResearchTrack.AuthService.Tests.Authentication;

public sealed class PasswordResetIntegrationTests : IAsyncLifetime
{
    private const string Email = "supervisor@staff.example.edu";
    private const string CurrentPassword = "StrongPassword!1";
    private const string NewPassword = "DifferentPassword!2";

    private readonly RecordingPasswordResetEmailService _emailService = new();
    private ResearchTrackWebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async ValueTask InitializeAsync()
    {
        var connectionString = TestDatabaseConfiguration.GetRequiredConnectionString("AUTH");
        _factory = new ResearchTrackWebApplicationFactory<Program>(
            connectionString,
            services =>
            {
                services.RemoveAll<IPasswordResetEmailService>();
                services.AddSingleton<IPasswordResetEmailService>(_emailService);
            });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthDbContext>>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await using var dbContext = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await dbContext.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
        dbContext.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = Email,
            FirstName = "Test",
            LastName = "Supervisor",
            PasswordHash = hasher.Hash(CurrentPassword),
            Role = UserRole.Supervisor,
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Forgot_password_is_non_enumerating_and_stores_only_a_hashed_token()
    {
        var known = await Client.PostAsJsonAsync(
            "/api/v1/auth/forgot-password",
            new ForgotPasswordRequest(Email),
            TestContext.Current.CancellationToken);
        var unknown = await Client.PostAsJsonAsync(
            "/api/v1/auth/forgot-password",
            new ForgotPasswordRequest("unknown@example.edu"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, known.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
        var resetUrl = Assert.Single(_emailService.ResetUrls);
        var rawToken = ExtractToken(resetUrl);

        await using var scope = Factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthDbContext>>();
        await using var dbContext = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var stored = await dbContext.PasswordResetTokens
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(rawToken, stored.TokenHash);
        Assert.DoesNotContain(rawToken, stored.TokenHash, StringComparison.Ordinal);
        Assert.StartsWith("http://localhost:5173/reset-password?token=", resetUrl, StringComparison.Ordinal);

        var validation = await Client.GetFromJsonAsync<ApiResponse<ValidateResetTokenResponse>>(
            $"/api/v1/auth/reset-password/validate?token={Uri.EscapeDataString(rawToken)}",
            TestContext.Current.CancellationToken);
        Assert.True(validation?.Data?.Valid);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Reset_password_is_single_use_updates_hash_revokes_sessions_and_clears_cookies()
    {
        var login = await Client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = Email, Password = CurrentPassword },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var forgot = await Client.PostAsJsonAsync(
            "/api/v1/auth/forgot-password",
            new ForgotPasswordRequest(Email),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, forgot.StatusCode);
        var rawToken = ExtractToken(Assert.Single(_emailService.ResetUrls));

        var reset = await Client.PostAsJsonAsync(
            "/api/v1/auth/reset-password",
            new ResetPasswordRequest(rawToken, NewPassword),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        var clearedCookies = reset.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(clearedCookies, value =>
            value.StartsWith($"{AuthSecurityConstants.AccessCookieName}=", StringComparison.Ordinal)
            && value.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(clearedCookies, value =>
            value.StartsWith($"{AuthSecurityConstants.RefreshCookieName}=", StringComparison.Ordinal)
            && value.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));

        var replay = await Client.PostAsJsonAsync(
            "/api/v1/auth/reset-password",
            new ResetPasswordRequest(rawToken, "AnotherPassword!3"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        var oldLogin = await Client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = Email, Password = CurrentPassword },
            TestContext.Current.CancellationToken);
        var newLogin = await Client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = Email, Password = NewPassword },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);

        var validation = await Client.GetFromJsonAsync<ApiResponse<ValidateResetTokenResponse>>(
            $"/api/v1/auth/reset-password/validate?token={Uri.EscapeDataString(rawToken)}",
            TestContext.Current.CancellationToken);
        Assert.False(validation?.Data?.Valid);

        await using var scope = Factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthDbContext>>();
        await using var dbContext = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var refreshTokens = await dbContext.RefreshTokens
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Single(refreshTokens, token => token.RevokedAt is not null);
        Assert.Single(refreshTokens, token => token.RevokedAt is null);
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private static string ExtractToken(string resetUrl)
    {
        var query = new Uri(resetUrl).Query;
        const string prefix = "?token=";
        Assert.StartsWith(prefix, query, StringComparison.Ordinal);
        return Uri.UnescapeDataString(query[prefix.Length..]);
    }

    private HttpClient Client =>
        _client ?? throw new InvalidOperationException("Test client is not initialized.");

    private ResearchTrackWebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Test factory is not initialized.");

    private sealed class RecordingPasswordResetEmailService : IPasswordResetEmailService
    {
        public List<string> ResetUrls { get; } = [];

        public Task SendResetLinkAsync(
            string to,
            string resetUrl,
            int expiryMinutes,
            CancellationToken cancellationToken)
        {
            ResetUrls.Add(resetUrl);
            return Task.CompletedTask;
        }
    }
}
