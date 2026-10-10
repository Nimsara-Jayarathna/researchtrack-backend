using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ResearchTrack.AuthService.Configuration;
using ResearchTrack.AuthService.Contracts;
using ResearchTrack.AuthService.Features.Passwords;
using ResearchTrack.AuthService.Features.Users;
using ResearchTrack.AuthService.Infrastructure.Email;
using ResearchTrack.AuthService.Infrastructure.Security;
using ResearchTrack.AuthService.Persistence;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ResearchTrack.AuthService.MutationTests.Services;

public sealed class AuthValidationCoverageHardeningTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    public async Task RequestResetAsync_InvalidEmail_IsRejectedBeforeDatabase(string? email)
    {
        var factory = Substitute.For<IDbContextFactory<AuthDbContext>>();
        var sut = CreateResetSut(factory);

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.RequestResetAsync(
            new ForgotPasswordRequest(email), CancellationToken.None));

        await factory.DidNotReceive().CreateDbContextAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestResetAsync_EmailOverMaximumLength_IsRejectedBeforeDatabase()
    {
        var factory = Substitute.For<IDbContextFactory<AuthDbContext>>();
        var sut = CreateResetSut(factory, maxEmailLength: 20);

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.RequestResetAsync(
            new ForgotPasswordRequest("very.long.email.address@example.com"), CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateTokenAsync_InvalidShape_ReturnsFalseWithoutDatabase(string? token)
    {
        var factory = Substitute.For<IDbContextFactory<AuthDbContext>>();
        var sut = CreateResetSut(factory);

        var result = await sut.ValidateTokenAsync(token, CancellationToken.None);

        Assert.False(result.Valid);
        await factory.DidNotReceive().CreateDbContextAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidateTokenAsync_OverMaximumLength_ReturnsFalseWithoutDatabase()
    {
        var factory = Substitute.For<IDbContextFactory<AuthDbContext>>();
        var sut = CreateResetSut(factory);

        var result = await sut.ValidateTokenAsync(new string('x', 513), CancellationToken.None);

        Assert.False(result.Valid);
    }

    [Fact]
    public async Task ResetPasswordAsync_InvalidTokenAndBlankPassword_ReturnsBothValidationErrorsBeforeDatabase()
    {
        var factory = Substitute.For<IDbContextFactory<AuthDbContext>>();
        var sut = CreateResetSut(factory);

        var exception = await Assert.ThrowsAsync<ApiValidationException>(() => sut.ResetPasswordAsync(
            new ResetPasswordRequest("", ""), CancellationToken.None));

        Assert.Contains(exception.FieldErrors, error => error.Field == "token");
        Assert.Contains(exception.FieldErrors, error => error.Field == "newPassword");
        await factory.DidNotReceive().CreateDbContextAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "New#Password1")]
    [InlineData("Current#Password1", "")]
    public async Task ChangePasswordAsync_MissingRequiredField_FailsBeforeDatabase(string currentPassword, string newPassword)
    {
        var factory = Substitute.For<IDbContextFactory<AuthDbContext>>();
        var hasher = Substitute.For<IPasswordHasher>();
        var policy = Substitute.For<IPasswordPolicyValidator>();
        policy.Validate(Arg.Any<string>(), Arg.Any<string>()).Returns([]);
        var sut = new UserAccountService(factory, hasher, policy);

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.ChangePasswordAsync(
            Guid.NewGuid(), new ChangePasswordRequest(currentPassword, newPassword),
            CancellationToken.None));

        await factory.DidNotReceive().CreateDbContextAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangePasswordAsync_PolicyFailure_FailsBeforeDatabase()
    {
        var factory = Substitute.For<IDbContextFactory<AuthDbContext>>();
        var hasher = Substitute.For<IPasswordHasher>();
        var policy = Substitute.For<IPasswordPolicyValidator>();
        policy.Validate("weak", "newPassword").Returns([
            new ResearchTrack.BuildingBlocks.Api.Contracts.ApiFieldError("newPassword", ["too weak"])
        ]);
        var sut = new UserAccountService(factory, hasher, policy);

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.ChangePasswordAsync(
            Guid.NewGuid(), new ChangePasswordRequest("Current#1", "weak"),
            CancellationToken.None));
    }

    private static PasswordResetService CreateResetSut(
        IDbContextFactory<AuthDbContext> factory,
        int maxEmailLength = 254)
    {
        var policy = Substitute.For<IPasswordPolicyValidator>();
        policy.Validate(Arg.Any<string>(), Arg.Any<string>()).Returns([]);
        return new PasswordResetService(
            factory,
            new RegistrationOptions(
                false, "students.example.edu", "staff.example.edu", false, "^[A-Za-z0-9]+$",
                false, false, 100, 100, maxEmailLength, 100, 300, 300),
            new PasswordResetOptions(30, new Uri("https://example.test")),
            policy,
            Substitute.For<IPasswordHasher>(),
            Substitute.For<IPasswordResetEmailService>(),
            NullLogger<PasswordResetService>.Instance);
    }
}
