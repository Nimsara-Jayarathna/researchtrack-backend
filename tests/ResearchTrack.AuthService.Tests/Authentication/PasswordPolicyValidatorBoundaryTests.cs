using ResearchTrack.AuthService.Configuration;
using ResearchTrack.AuthService.Features.Passwords;

namespace ResearchTrack.AuthService.Tests.Authentication;

public sealed class PasswordPolicyValidatorBoundaryTests
{
    [Fact]
    public void Validate_AcceptsExactMinimumAndMaximumLengthsWhenOtherRulesPass()
    {
        var validator = new PasswordPolicyValidator(new PasswordPolicyOptions(
            MinimumLength: 4,
            MaximumLength: 6,
            RequireUppercase: true,
            RequireLowercase: true,
            RequireDigit: true,
            RequireSpecialCharacter: true));

        Assert.Empty(validator.Validate("Aa1!", "password"));
        Assert.Empty(validator.Validate("Aa1!xy", "password"));
    }

    [Fact]
    public void Validate_ReportsBothLengthBoundariesPrecisely()
    {
        var validator = new PasswordPolicyValidator(new PasswordPolicyOptions(
            MinimumLength: 4,
            MaximumLength: 6,
            RequireUppercase: false,
            RequireLowercase: false,
            RequireDigit: false,
            RequireSpecialCharacter: false));

        var tooShort = validator.Validate("abc", "password");
        var tooLong = validator.Validate("abcdefg", "password");

        Assert.Contains(tooShort, error => error.Errors.Contains("Password must be at least 4 characters."));
        Assert.Contains(tooLong, error => error.Errors.Contains("Password must not exceed 6 characters."));
    }

    [Theory]
    [InlineData("Abc!", "Password must contain a digit.")]
    [InlineData("abc1!", "Password must contain an uppercase letter.")]
    [InlineData("ABC1!", "Password must contain a lowercase letter.")]
    [InlineData("Abc1", "Password must contain a special character.")]
    public void Validate_ReportsEachRequiredCharacterClassIndependently(string password, string expectedMessage)
    {
        var validator = StrictValidator();

        var errors = validator.Validate(password, "password");

        Assert.Contains(errors, error => error.Errors.Contains(expectedMessage));
    }

    [Fact]
    public void Validate_RespectsDisabledCharacterRequirements()
    {
        var validator = new PasswordPolicyValidator(new PasswordPolicyOptions(
            MinimumLength: 1,
            MaximumLength: 20,
            RequireUppercase: false,
            RequireLowercase: false,
            RequireDigit: false,
            RequireSpecialCharacter: false));

        Assert.Empty(validator.Validate("x", "password"));
    }

    [Fact]
    public void Validate_RejectsNullPasswordAndBlankFieldName()
    {
        var validator = StrictValidator();

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!, "password"));
        Assert.Throws<ArgumentException>(() => validator.Validate("Abc1!", "   "));
    }

    private static PasswordPolicyValidator StrictValidator() => new(new PasswordPolicyOptions(
        MinimumLength: 4,
        MaximumLength: 128,
        RequireUppercase: true,
        RequireLowercase: true,
        RequireDigit: true,
        RequireSpecialCharacter: true));
}
