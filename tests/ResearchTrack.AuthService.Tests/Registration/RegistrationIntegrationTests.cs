using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResearchTrack.AuthService.Contracts;
using ResearchTrack.AuthService.Domain;
using ResearchTrack.AuthService.Infrastructure.Security;
using ResearchTrack.AuthService.Persistence;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.Testing;

namespace ResearchTrack.AuthService.Tests.Registration;

public sealed class RegistrationIntegrationTests : IAsyncLifetime
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
        await using var dbContext = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await dbContext.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Student_registration_assigns_student_role_ignores_client_role_and_hashes_password()
    {
        var request = new RegisterRequest
        {
            FirstName = "Nimal",
            LastName = "Perera",
            Email = "ST12345678@students.example.edu",
            RegistrationNumber = "st12345678",
            Password = "StrongPassword!1",
            Role = "SUPERVISOR"
        };

        var response = await Client.PostAsJsonAsync(
            "/api/v1/auth/register",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<RegisterResponse>>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(payload?.Data);
        Assert.Equal("STUDENT", payload!.Data!.Role);
        Assert.Equal("st12345678@students.example.edu", payload.Data.Email);
        Assert.Equal("ST12345678", payload.Data.RegistrationNumber);

        await using var scope = Factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthDbContext>>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await using var dbContext = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var stored = await dbContext.Users.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(UserRole.Student, stored.Role);
        Assert.NotEqual(request.Password, stored.PasswordHash);
        Assert.True(hasher.Verify(request.Password!, stored.PasswordHash));
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Supervisor_registration_assigns_supervisor_role_without_student_number()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest
            {
                FirstName = "Dr",
                LastName = "Supervisor",
                Email = "lecturer@staff.example.edu",
                Password = "StrongPassword!1"
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<RegisterResponse>>(
            TestContext.Current.CancellationToken);
        Assert.Equal("SUPERVISOR", payload?.Data?.Role);
        Assert.Null(payload?.Data?.RegistrationNumber);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Duplicate_email_is_prevented_case_insensitively()
    {
        var first = new RegisterRequest
        {
            FirstName = "First",
            LastName = "User",
            Email = "lecturer@staff.example.edu",
            Password = "StrongPassword!1"
        };
        var second = new RegisterRequest
        {
            FirstName = "Second",
            LastName = "User",
            Email = "LECTURER@STAFF.EXAMPLE.EDU",
            Password = "StrongPassword!1"
        };

        var created = await Client.PostAsJsonAsync("/api/v1/auth/register", first, TestContext.Current.CancellationToken);
        var duplicate = await Client.PostAsJsonAsync("/api/v1/auth/register", second, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var payload = await duplicate.Content.ReadFromJsonAsync<ApiResponse<object>>(TestContext.Current.CancellationToken);
        Assert.Equal("CONFLICT", payload?.Error?.Code);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Registration_field_aliases_are_accepted_but_role_is_still_server_assigned()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new
            {
                fname = "Legacy",
                lname = "Student",
                email = "ST87654321@students.example.edu",
                name = "ST87654321",
                password = "StrongPassword!1",
                role = "SUPERVISOR"
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<RegisterResponse>>(
            TestContext.Current.CancellationToken);
        Assert.Equal("Legacy", payload?.Data?.FirstName);
        Assert.Equal("STUDENT", payload?.Data?.Role);
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private HttpClient Client => _client ?? throw new InvalidOperationException("Test client is not initialized.");
    private ResearchTrackWebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Test factory is not initialized.");
}
