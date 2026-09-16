namespace ResearchTrack.GitHubService.Features.Installation;

public interface IGitHubAccessRequestTokenService
{
    (string Nonce, string Token, string Hash) CreateRequestToken(Guid requestId);
    (string Nonce, string Token, string Hash) CreateResultToken(Guid requestId);
    string RecreateRequestToken(Guid requestId, string nonce);
    string RecreateResultToken(Guid requestId, string nonce);
    string Hash(string token);
    bool IsWellFormed(string token, Guid requestId, string nonce, bool resultToken);
}
