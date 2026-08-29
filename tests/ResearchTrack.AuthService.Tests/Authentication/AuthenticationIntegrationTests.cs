using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResearchTrack.AuthService.Contracts;
using ResearchTrack.AuthService.Domain;
using ResearchTrack.AuthService.Infrastructure.Security;
using ResearchTrack.AuthService.Persistence;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.Testing;

namespace ResearchTrack.AuthService.Tests.Authentication;

public sealed class AuthenticationIntegrationTests : IAsyncLifetime
{
    private ResearchTrackWebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async ValueTask InitializeAsync()
    {
        var connectionString = TestDatabaseConfiguration.GetRequiredConnectionString("AUTH");
        _factory = new ResearchTrackWebApplicationFactory<Program>(connectionString);
        _client = _factory.CreateClient();

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthDbContext>>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await using var dbContext = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await dbContext.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);

        dbContext.Users.AddRange(
            CreateUser(hasher, "student@students.example.edu", UserRole.Student),
            CreateUser(
                hasher,
                "learner@example.edu",
                UserRole.Student,
                firstName: "Learner",
                registrationNumber: "ST87654321"),
            CreateUser(hasher, "supervisor@staff.example.edu", UserRole.Supervisor));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Login_with_valid_credentials_returns_user_and_http_only_session_cookies()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = "SUPERVISOR@STAFF.EXAMPLE.EDU", Password = "StrongPassword!1" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>(
            TestContext.Current.CancellationToken);
        Assert.Equal(AuthSecurityConstants.Roles.Supervisor, payload?.Data?.User.Role);
        Assert.Equal("supervisor@staff.example.edu", payload?.Data?.User.Email);

        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(cookies, value => value.StartsWith($"{AuthSecurityConstants.AccessCookieName}=", StringComparison.Ordinal));
        Assert.Contains(cookies, value => value.StartsWith($"{AuthSecurityConstants.RefreshCookieName}=", StringComparison.Ordinal));
        Assert.All(cookies, value => Assert.Contains("httponly", value, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(cookies, value => value.StartsWith($"{AuthSecurityConstants.AccessCookieName}=", StringComparison.Ordinal)
            && value.Contains("path=/api", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(cookies, value => value.StartsWith($"{AuthSecurityConstants.RefreshCookieName}=", StringComparison.Ordinal)
            && value.Contains("path=/api/v1/auth", StringComparison.OrdinalIgnoreCase));

        var rawRefreshToken = ExtractCookie(response, AuthSecurityConstants.RefreshCookieName);
        await using var scope = Factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthDbContext>>();
        await using var dbContext = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var stored = await dbContext.RefreshTokens.AsNoTracking().SingleAsync(
            token => token.UserId == payload!.Data!.User.Id,
            TestContext.Current.CancellationToken);
        Assert.NotEqual(rawRefreshToken, stored.TokenHash);
    }

    [Theory]
    [InlineData("missing@staff.example.edu", "StrongPassword!1")]
    [InlineData("supervisor@staff.example.edu", "WrongPassword!1")]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Login_with_invalid_credentials_returns_same_non_sensitive_error(string email, string password)
    {
        var response = await Client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = email, Password = password },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(
            TestContext.Current.CancellationToken);
        Assert.Equal("UNAUTHORIZED", payload?.Error?.Code);
        Assert.Equal("Invalid email or password.", payload?.Error?.Message);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Refresh_rotates_one_time_refresh_token_and_rejects_replay()
    {
        var login = await LoginAsync("student@students.example.edu");
        var firstRefresh = ExtractCookie(login, AuthSecurityConstants.RefreshCookieName);

        using var refreshRequest = CreateCookiePost("/api/v1/auth/refresh", AuthSecurityConstants.RefreshCookieName, firstRefresh);
        var refreshed = await Client.SendAsync(refreshRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var replacement = ExtractCookie(refreshed, AuthSecurityConstants.RefreshCookieName);
        Assert.NotEqual(firstRefresh, replacement);

        using var replayRequest = CreateCookiePost("/api/v1/auth/refresh", AuthSecurityConstants.RefreshCookieName, firstRefresh);
        var replay = await Client.SendAsync(replayRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Me_requires_valid_access_cookie_and_returns_current_user()
    {
        var anonymous = await Client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var login = await LoginAsync("student@students.example.edu");
        var accessToken = ExtractCookie(login, AuthSecurityConstants.AccessCookieName);
        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        meRequest.Headers.Add("Cookie", $"{AuthSecurityConstants.AccessCookieName}={accessToken}");
        var me = await Client.SendAsync(meRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var payload = await me.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>(
            TestContext.Current.CancellationToken);
        Assert.Equal(AuthSecurityConstants.Roles.Student, payload?.Data?.User.Role);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Supervisor_can_search_and_resolve_registered_students_but_student_cannot()
    {
        var supervisorLogin = await LoginAsync("supervisor@staff.example.edu");
        var supervisorAccessToken = ExtractCookie(supervisorLogin, AuthSecurityConstants.AccessCookieName);

        using var searchRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/users/students?query=student@");
        searchRequest.Headers.Add(
            "Cookie",
            $"{AuthSecurityConstants.AccessCookieName}={supervisorAccessToken}");
        var searchResponse = await Client.SendAsync(
            searchRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var searchPayload = await searchResponse.Content.ReadFromJsonAsync<
            ApiResponse<IReadOnlyList<UserDirectoryResponse>>>(
            TestContext.Current.CancellationToken);
        var student = Assert.Single(searchPayload?.Data ?? []);
        Assert.Equal(AuthSecurityConstants.Roles.Student, student.Role);

        await using var scope = Factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthDbContext>>();
        await using var dbContext = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var allStudentIds = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Role == UserRole.Student)
            .OrderBy(user => user.Email)
            .Select(user => user.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, allStudentIds.Length);

        using var resolveRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/users/students/resolve");
        resolveRequest.Headers.Add(
            "Cookie",
            $"{AuthSecurityConstants.AccessCookieName}={supervisorAccessToken}");
        resolveRequest.Content = JsonContent.Create(new
        {
            studentIds = allStudentIds.Append(Guid.NewGuid()).ToArray()
        });
        var resolveResponse = await Client.SendAsync(
            resolveRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);
        var resolvedPayload = await resolveResponse.Content.ReadFromJsonAsync<
            ApiResponse<IReadOnlyList<UserDirectoryResponse>>>(
            TestContext.Current.CancellationToken);
        Assert.Equal(2, resolvedPayload?.Data?.Count);

        var studentLogin = await LoginAsync("student@students.example.edu");
        var studentAccessToken = ExtractCookie(studentLogin, AuthSecurityConstants.AccessCookieName);
        using var deniedRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/users/students?query=student");
        deniedRequest.Headers.Add(
            "Cookie",
            $"{AuthSecurityConstants.AccessCookieName}={studentAccessToken}");
        var deniedResponse = await Client.SendAsync(
            deniedRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Change_password_updates_hash_and_revokes_all_refresh_sessions()
    {
        var firstLogin = await LoginAsync("student@students.example.edu");
        var secondLogin = await LoginAsync("student@students.example.edu");
        var firstRefreshToken = ExtractCookie(firstLogin, AuthSecurityConstants.RefreshCookieName);
        var secondRefreshToken = ExtractCookie(secondLogin, AuthSecurityConstants.RefreshCookieName);
        var accessToken = ExtractCookie(secondLogin, AuthSecurityConstants.AccessCookieName);

        using var changeRequest = CreateCookieJsonRequest(
            HttpMethod.Patch,
            "/api/v1/users/me/password",
            AuthSecurityConstants.AccessCookieName,
            accessToken,
            new ChangePasswordRequest("StrongPassword!1", "NewStrongPassword!2"));
        var changed = await Client.SendAsync(changeRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthDbContext>>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            await using var dbContext = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
            var user = await dbContext.Users.AsNoTracking().SingleAsync(
                candidate => candidate.Email == "student@students.example.edu",
                TestContext.Current.CancellationToken);
            var refreshTokens = await dbContext.RefreshTokens.AsNoTracking()
                .Where(token => token.UserId == user.Id)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.True(hasher.Verify("NewStrongPassword!2", user.PasswordHash));
            Assert.False(hasher.Verify("StrongPassword!1", user.PasswordHash));
            Assert.Collection(
                refreshTokens,
                token => Assert.NotNull(token.RevokedAt),
                token => Assert.NotNull(token.RevokedAt));
        }

        using var firstRefreshRequest = CreateCookiePost(
            "/api/v1/auth/refresh",
            AuthSecurityConstants.RefreshCookieName,
            firstRefreshToken);
        var firstRefresh = await Client.SendAsync(
            firstRefreshRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, firstRefresh.StatusCode);

        using var secondRefreshRequest = CreateCookiePost(
            "/api/v1/auth/refresh",
            AuthSecurityConstants.RefreshCookieName,
            secondRefreshToken);
        var secondRefresh = await Client.SendAsync(
            secondRefreshRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, secondRefresh.StatusCode);

        var oldPasswordLogin = await Client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest
            {
                Email = "student@students.example.edu",
                Password = "StrongPassword!1"
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);

        var newPasswordLogin = await Client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest
            {
                Email = "student@students.example.edu",
                Password = "NewStrongPassword!2"
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, newPasswordLogin.StatusCode);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Change_password_rejects_incorrect_current_password_without_changing_hash()
    {
        var login = await LoginAsync("supervisor@staff.example.edu");
        var accessToken = ExtractCookie(login, AuthSecurityConstants.AccessCookieName);

        using var changeRequest = CreateCookieJsonRequest(
            HttpMethod.Patch,
            "/api/v1/users/me/password",
            AuthSecurityConstants.AccessCookieName,
            accessToken,
            new ChangePasswordRequest("WrongPassword!1", "NewStrongPassword!2"));
        var response = await Client.SendAsync(changeRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(
            TestContext.Current.CancellationToken);
        Assert.Equal("CURRENT_PASSWORD_INCORRECT", payload?.Error?.Code);
        Assert.Equal("Current password is incorrect.", payload?.Error?.Message);

        await using var scope = Factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthDbContext>>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await using var dbContext = await dbFactory.CreateDbContextAsync(
            TestContext.Current.CancellationToken);
        var user = await dbContext.Users.AsNoTracking().SingleAsync(
            candidate => candidate.Email == "supervisor@staff.example.edu",
            TestContext.Current.CancellationToken);
        var refreshToken = await dbContext.RefreshTokens.AsNoTracking().SingleAsync(
            token => token.UserId == user.Id,
            TestContext.Current.CancellationToken);

        Assert.True(hasher.Verify("StrongPassword!1", user.PasswordHash));
        Assert.Null(refreshToken.RevokedAt);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Change_password_rejects_reusing_current_password()
    {
        var login = await LoginAsync("student@students.example.edu");
        var accessToken = ExtractCookie(login, AuthSecurityConstants.AccessCookieName);

        using var changeRequest = CreateCookieJsonRequest(
            HttpMethod.Patch,
            "/api/v1/users/me/password",
            AuthSecurityConstants.AccessCookieName,
            accessToken,
            new ChangePasswordRequest("StrongPassword!1", "StrongPassword!1"));
        var response = await Client.SendAsync(changeRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(
            TestContext.Current.CancellationToken);
        Assert.Equal("VALIDATION_ERROR", payload?.Error?.Code);
        Assert.Contains(
            payload?.Error?.FieldErrors ?? [],
            error => error.Field == "newPassword"
                && error.Errors.Contains("New password must be different from current password."));
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Change_password_enforces_the_same_password_policy_as_registration()
    {
        var login = await LoginAsync("student@students.example.edu");
        var accessToken = ExtractCookie(login, AuthSecurityConstants.AccessCookieName);

        using var changeRequest = CreateCookieJsonRequest(
            HttpMethod.Patch,
            "/api/v1/users/me/password",
            AuthSecurityConstants.AccessCookieName,
            accessToken,
            new ChangePasswordRequest("StrongPassword!1", "alllowercasepassword"));
        var response = await Client.SendAsync(changeRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(
            TestContext.Current.CancellationToken);
        var fieldErrors = payload?.Error?.FieldErrors ?? [];
        Assert.Contains(fieldErrors, error => error.Field == "newPassword");
        Assert.Contains(
            fieldErrors.SelectMany(error => error.Errors),
            message => message == "Password must contain an uppercase letter.");
        Assert.Contains(
            fieldErrors.SelectMany(error => error.Errors),
            message => message == "Password must contain a digit.");
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Change_password_requires_authentication()
    {
        var response = await Client.PatchAsJsonAsync(
            "/api/v1/users/me/password",
            new ChangePasswordRequest("StrongPassword!1", "NewStrongPassword!2"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Logout_revokes_refresh_token_and_clears_auth_cookies()
    {
        var login = await LoginAsync("supervisor@staff.example.edu");
        var refreshToken = ExtractCookie(login, AuthSecurityConstants.RefreshCookieName);

        using var logoutRequest = CreateCookiePost("/api/v1/auth/logout", AuthSecurityConstants.RefreshCookieName, refreshToken);
        var logout = await Client.SendAsync(logoutRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var clearCookies = logout.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(clearCookies, value => value.StartsWith($"{AuthSecurityConstants.AccessCookieName}=", StringComparison.Ordinal));
        Assert.Contains(clearCookies, value => value.StartsWith($"{AuthSecurityConstants.RefreshCookieName}=", StringComparison.Ordinal));

        using var refreshRequest = CreateCookiePost("/api/v1/auth/refresh", AuthSecurityConstants.RefreshCookieName, refreshToken);
        var refresh = await Client.SendAsync(refreshRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private async Task<HttpResponseMessage> LoginAsync(string email) => await Client.PostAsJsonAsync(
        "/api/v1/auth/login",
        new LoginRequest { Email = email, Password = "StrongPassword!1" },
        TestContext.Current.CancellationToken);

    private static HttpRequestMessage CreateCookiePost(string path, string cookieName, string cookieValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Cookie", $"{cookieName}={cookieValue}");
        request.Content = JsonContent.Create(new { });
        return request;
    }

    private static HttpRequestMessage CreateCookieJsonRequest(
        HttpMethod method,
        string path,
        string cookieName,
        string cookieValue,
        object body)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"{cookieName}={cookieValue}");
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static string ExtractCookie(HttpResponseMessage response, string name)
    {
        var header = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith($"{name}=", StringComparison.Ordinal));
        var firstPart = header.Split(';', 2)[0];
        return firstPart[(name.Length + 1)..];
    }

    private static User CreateUser(
        IPasswordHasher hasher,
        string email,
        UserRole role,
        string? firstName = null,
        string? registrationNumber = null) => new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = firstName ?? (role == UserRole.Student ? "Student" : "Supervisor"),
            LastName = "User",
            PasswordHash = hasher.Hash("StrongPassword!1"),
            Role = role,
            RegistrationNumber = role == UserRole.Student ? registrationNumber ?? "ST12345678" : null,
            CreatedAt = DateTime.UtcNow
        };

    private ResearchTrackWebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Test factory is not initialized.");

    private HttpClient Client => _client ?? throw new InvalidOperationException("Test client is not initialized.");
}
