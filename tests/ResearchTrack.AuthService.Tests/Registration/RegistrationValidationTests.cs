using System.Net;
using System.Net.Http.Json;
using ResearchTrack.AuthService.Contracts;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.Testing;

namespace ResearchTrack.AuthService.Tests.Registration;

public sealed class RegistrationValidationTests : IAsyncLifetime
{
    private ResearchTrackWebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public ValueTask InitializeAsync()
    {
        _factory = new ResearchTrackWebApplicationFactory<Program>(TestDatabaseConfiguration.NonConnectingPlaceholder);
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Registration_config_exposes_environment_driven_policy()
    {
        var response = await Client.GetAsync("/api/v1/auth/register/config", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<RegistrationConfigResponse>>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(payload);
        Assert.True(payload!.Success);
        Assert.Equal("@students.example.edu", payload.Data?.StudentDomain);
        Assert.Equal("@staff.example.edu", payload.Data?.SupervisorDomain);
        Assert.Equal(12, payload.Data?.PasswordPolicy.MinimumLength);
    }

    [Fact]
    public async Task Invalid_institutional_email_is_rejected_before_database_access()
    {
        var request = ValidStudentRequest("ST12345678@gmail.com");
        var response = await Client.PostAsJsonAsync(
            "/api/v1/auth/register",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(TestContext.Current.CancellationToken);
        Assert.Equal("VALIDATION_ERROR", payload?.Error?.Code);
        Assert.Contains(payload?.Error?.FieldErrors ?? [], error => error.Field == "email");
    }

    [Fact]
    public async Task Missing_required_fields_return_field_level_validation_errors()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email = "" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(TestContext.Current.CancellationToken);
        var fields = (payload?.Error?.FieldErrors ?? []).Select(error => error.Field).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("firstName", fields);
        Assert.Contains("lastName", fields);
        Assert.Contains("email", fields);
        Assert.Contains("password", fields);
    }

    [Fact]
    public async Task Weak_password_is_rejected_using_environment_policy()
    {
        var request = new RegisterRequest
        {
            FirstName = "Test",
            LastName = "Student",
            Email = "ST12345678@students.example.edu",
            RegistrationNumber = "ST12345678",
            Password = "weak"
        };
        var response = await Client.PostAsJsonAsync(
            "/api/v1/auth/register",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(TestContext.Current.CancellationToken);
        Assert.Contains(payload?.Error?.FieldErrors ?? [], error => error.Field == "password");
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

    private static RegisterRequest ValidStudentRequest(string email) => new()
    {
        FirstName = "Test",
        LastName = "Student",
        Email = email,
        RegistrationNumber = "ST12345678",
        Password = "StrongPassword!1"
    };
}
