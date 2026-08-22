using ResearchTrack.AuthService.Configuration;
using ResearchTrack.BuildingBlocks.Api.Security;

namespace ResearchTrack.AuthService.Infrastructure.Cookies;

public sealed class AuthCookieService : IAuthCookieService
{
    private const string AccessPath = "/api";
    private const string RefreshPath = "/api/v1/auth";

    private readonly JwtOptions _jwtOptions;
    private readonly AuthCookieOptions _cookieOptions;

    public AuthCookieService(JwtOptions jwtOptions, AuthCookieOptions cookieOptions)
    {
        _jwtOptions = jwtOptions;
        _cookieOptions = cookieOptions;
    }

    public string? ReadRefreshToken(HttpRequest request) =>
        request.Cookies.TryGetValue(AuthSecurityConstants.RefreshCookieName, out var value)
            ? value
            : null;

    public void WriteSession(HttpResponse response, string accessToken, string refreshToken)
    {
        response.Cookies.Append(
            AuthSecurityConstants.AccessCookieName,
            accessToken,
            CreateCookieOptions(AccessPath, TimeSpan.FromMinutes(_jwtOptions.AccessTokenMinutes)));

        response.Cookies.Append(
            AuthSecurityConstants.RefreshCookieName,
            refreshToken,
            CreateCookieOptions(RefreshPath, TimeSpan.FromDays(_jwtOptions.RefreshTokenDays)));
    }

    public void ClearSession(HttpResponse response)
    {
        response.Cookies.Delete(
            AuthSecurityConstants.AccessCookieName,
            CreateDeleteOptions(AccessPath));
        response.Cookies.Delete(
            AuthSecurityConstants.RefreshCookieName,
            CreateDeleteOptions(RefreshPath));
    }

    private CookieOptions CreateCookieOptions(string path, TimeSpan maxAge) => new()
    {
        HttpOnly = true,
        Secure = _cookieOptions.Secure,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
        Path = path,
        MaxAge = maxAge
    };

    private CookieOptions CreateDeleteOptions(string path) => new()
    {
        HttpOnly = true,
        Secure = _cookieOptions.Secure,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
        Path = path
    };
}
