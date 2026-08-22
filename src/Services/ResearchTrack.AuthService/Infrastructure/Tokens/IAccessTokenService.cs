using ResearchTrack.AuthService.Domain;

namespace ResearchTrack.AuthService.Infrastructure.Tokens;

public interface IAccessTokenService
{
    string Generate(User user);
}
