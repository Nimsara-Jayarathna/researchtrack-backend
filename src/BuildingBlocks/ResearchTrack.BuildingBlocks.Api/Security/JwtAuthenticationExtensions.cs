using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace ResearchTrack.BuildingBlocks.Api.Security;

public static class JwtAuthenticationExtensions
{
    public static IServiceCollection AddResearchTrackJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var issuer = Require(configuration, "Jwt:Issuer");
        var audience = Require(configuration, "Jwt:Audience");
        var signingKey = Require(configuration, "Jwt:SigningKey");
        var signingKeyBytes = Encoding.UTF8.GetBytes(signingKey);

        if (signingKeyBytes.Length < 32)
        {
            throw new InvalidOperationException("Configuration 'Jwt:SigningKey' must contain at least 32 UTF-8 bytes for HS256.");
        }

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = AuthSecurityConstants.SubjectClaim,
                    RoleClaimType = AuthSecurityConstants.RoleClaim
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (string.IsNullOrWhiteSpace(context.Token)
                            && context.Request.Cookies.TryGetValue(
                                AuthSecurityConstants.AccessCookieName,
                                out var cookieToken))
                        {
                            context.Token = cookieToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(
                AuthSecurityConstants.Policies.Authenticated,
                policy => policy.RequireAuthenticatedUser())
            .AddPolicy(
                AuthSecurityConstants.Policies.StudentOnly,
                policy => policy
                    .RequireAuthenticatedUser()
                    .RequireRole(AuthSecurityConstants.Roles.Student))
            .AddPolicy(
                AuthSecurityConstants.Policies.SupervisorOnly,
                policy => policy
                    .RequireAuthenticatedUser()
                    .RequireRole(AuthSecurityConstants.Roles.Supervisor));

        return services;
    }

    private static string Require(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Required configuration '{key}' is missing or still contains a placeholder value.");
        }

        return value.Trim();
    }
}
