using ResearchTrack.AuthService.Configuration;
using ResearchTrack.BuildingBlocks.Api.Contracts;

namespace ResearchTrack.AuthService.Features.Passwords;

public sealed class PasswordPolicyValidator : IPasswordPolicyValidator
{
    private readonly PasswordPolicyOptions _options;

    public PasswordPolicyValidator(PasswordPolicyOptions options)
    {
        _options = options;
    }

    public IReadOnlyList<ApiFieldError> Validate(string password, string field)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        var errors = new List<ApiFieldError>();

        if (password.Length < _options.MinimumLength)
        {
            errors.Add(new ApiFieldError(
                field,
                [$"Password must be at least {_options.MinimumLength} characters."]));
        }

        if (password.Length > _options.MaximumLength)
        {
            errors.Add(new ApiFieldError(
                field,
                [$"Password must not exceed {_options.MaximumLength} characters."]));
        }

        if (_options.RequireUppercase && !password.Any(char.IsUpper))
        {
            errors.Add(new ApiFieldError(field, ["Password must contain an uppercase letter."]));
        }

        if (_options.RequireLowercase && !password.Any(char.IsLower))
        {
            errors.Add(new ApiFieldError(field, ["Password must contain a lowercase letter."]));
        }

        if (_options.RequireDigit && !password.Any(char.IsDigit))
        {
            errors.Add(new ApiFieldError(field, ["Password must contain a digit."]));
        }

        if (_options.RequireSpecialCharacter
            && !password.Any(character => !char.IsLetterOrDigit(character)))
        {
            errors.Add(new ApiFieldError(field, ["Password must contain a special character."]));
        }

        return errors;
    }
}
