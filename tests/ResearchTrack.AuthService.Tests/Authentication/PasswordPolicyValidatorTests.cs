using ResearchTrack.AuthService.Configuration;
using ResearchTrack.AuthService.Features.Passwords;

namespace ResearchTrack.AuthService.Tests.Authentication;

public sealed class PasswordPolicyValidatorTests
{
    private static readonly PasswordPolicyOptions Policy = new(
        MinimumLength: 12,
        MaximumLength: 128,
        RequireUppercase: true,
        RequireLowercase: true,
        RequireDigit: true,
        RequireSpecialCharacter: true);

    private readonly PasswordPolicyValidator _validator = new(Policy);

    [Fact]
    public void Strong_password_passes_all_configured_rules()
    {
        var errors = _validator.Validate("StrongPassword!1", "newPassword");

        Assert.Empty(errors);
    }

    [Fact]
    public void Weak_password_reports_each_failed_rule_on_requested_field()
    {
        var errors = _validator.Validate("lowercaseonly", "newPassword");

        Assert.All(errors, error => Assert.Equal("newPassword", error.Field));
        Assert.Contains(errors, error => error.Errors.Contains("Password must contain an uppercase letter."));
        Assert.Contains(errors, error => error.Errors.Contains("Password must contain a digit."));
        Assert.Contains(errors, error => error.Errors.Contains("Password must contain a special character."));
    }
}
