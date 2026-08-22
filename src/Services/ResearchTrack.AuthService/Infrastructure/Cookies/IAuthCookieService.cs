namespace ResearchTrack.AuthService.Infrastructure.Cookies;

public interface IAuthCookieService
{
    string? ReadRefreshToken(HttpRequest request);
    void WriteSession(HttpResponse response, string accessToken, string refreshToken);
    void ClearSession(HttpResponse response);
}
