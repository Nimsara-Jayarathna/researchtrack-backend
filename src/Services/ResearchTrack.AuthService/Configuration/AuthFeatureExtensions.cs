using ResearchTrack.AuthService.Features.Authentication;
using ResearchTrack.AuthService.Features.Registration;
using ResearchTrack.AuthService.Infrastructure.Cookies;
using ResearchTrack.AuthService.Infrastructure.Email;
using ResearchTrack.AuthService.Infrastructure.Security;
using ResearchTrack.AuthService.Infrastructure.Tokens;

namespace ResearchTrack.AuthService.Configuration;

public static class AuthFeatureExtensions
{
    public static IServiceCollection AddAuthFeatures(this IServiceCollection services, IConfiguration configuration)
    {
        var registrationOptions = RegistrationOptions.FromConfiguration(configuration);
        var passwordPolicyOptions = PasswordPolicyOptions.FromConfiguration(configuration);
        var passwordHashingOptions = PasswordHashingOptions.FromConfiguration(configuration);
        var emailOptions = EmailOptions.FromConfiguration(configuration);
        var jwtOptions = JwtOptions.FromConfiguration(configuration);
        var cookieOptions = AuthCookieOptions.FromConfiguration(configuration);

        if (passwordPolicyOptions.MaximumLength < passwordPolicyOptions.MinimumLength)
        {
            throw new InvalidOperationException("PasswordPolicy:MaximumLength must be greater than or equal to PasswordPolicy:MinimumLength.");
        }

        services.AddSingleton(registrationOptions);
        services.AddSingleton(passwordPolicyOptions);
        services.AddSingleton(passwordHashingOptions);
        services.AddSingleton(emailOptions);
        services.AddSingleton(jwtOptions);
        services.AddSingleton(cookieOptions);
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<InvalidPasswordTimingGuard>();
        services.AddSingleton<IAuthCookieService, AuthCookieService>();
        services.AddSingleton<IAccessTokenService, JwtAccessTokenService>();

        services.AddHttpClient<IEmailProvider, BrevoEmailProvider>(client =>
        {
            client.BaseAddress = new Uri(emailOptions.BaseUrl, UriKind.Absolute);
        });
        services.AddScoped<IRegistrationEmailService, RegistrationEmailService>();
        services.AddScoped<IRegistrationService, RegistrationService>();
        services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
        return services;
    }
}
