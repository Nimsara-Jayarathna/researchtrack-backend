using ResearchTrack.BuildingBlocks.Api.Contracts;

namespace ResearchTrack.AuthService.Features.Passwords;

public interface IPasswordPolicyValidator
{
    IReadOnlyList<ApiFieldError> Validate(string password, string field);
}
