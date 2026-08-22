using ResearchTrack.AuthService.Features.Registration;
using ResearchTrack.AuthService.Infrastructure.Security;

namespace ResearchTrack.AuthService.Configuration;

public static class AuthFeatureExtensions
{
    public static IServiceCollection AddAuthFeatures(this IServiceCollection services, IConfiguration configuration)
    {
        var registrationOptions = RegistrationOptions.FromConfiguration(configuration);
        var passwordPolicyOptions = PasswordPolicyOptions.FromConfiguration(configuration);
        var passwordHashingOptions = PasswordHashingOptions.FromConfiguration(configuration);

        if (passwordPolicyOptions.MaximumLength < passwordPolicyOptions.MinimumLength)
        {
            throw new InvalidOperationException("PasswordPolicy:MaximumLength must be greater than or equal to PasswordPolicy:MinimumLength.");
        }

        services.AddSingleton(registrationOptions);
        services.AddSingleton(passwordPolicyOptions);
        services.AddSingleton(passwordHashingOptions);
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<IRegistrationService, RegistrationService>();
        return services;
    }
}
