using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using ResearchTrack.SubmissionService.Configuration;
using ResearchTrack.SubmissionService.Features;
using ResearchTrack.SubmissionService.Infrastructure;

namespace ResearchTrack.SubmissionService.Extensions;

public static class SubmissionFeatureExtensions
{
    public static IServiceCollection AddSubmissionFeatures(this IServiceCollection services, IConfiguration configuration)
    {
        var submission = configuration.GetSection(SubmissionOptions.SectionName).Get<SubmissionOptions>() ?? new SubmissionOptions();
        var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        Validate(submission, storage);
        var projectBaseUrl = RequireUrl(configuration, "Services:Project:BaseUrl");
        var authBaseUrl = RequireUrl(configuration, "Services:Auth:BaseUrl");

        services.AddSingleton(submission);
        services.AddSingleton(storage);
        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddHttpClient<IProjectAuthorizationClient, ProjectAuthorizationClient>(client =>
        {
            client.BaseAddress = EnsureTrailingSlash(projectBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(submission.DependencyTimeoutSeconds);
        });
        services.AddHttpClient<IUserProfileClient, UserProfileClient>(client =>
        {
            client.BaseAddress = EnsureTrailingSlash(authBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(submission.DependencyTimeoutSeconds);
        });
        services.AddSingleton(_ => BuildS3Client(storage));
        services.AddSingleton<IObjectStorageService, S3ObjectStorageService>();
        services.AddScoped<ISubmissionRequirementService, SubmissionRequirementService>();
        services.AddScoped<IResearchSubmissionService, ResearchSubmissionService>();
        services.AddHostedService<ExpiredUploadSessionCleanupService>();
        return services;
    }

    private static AmazonS3Client BuildS3Client(StorageOptions options)
    {
        var credentials = new BasicAWSCredentials(options.AccessKey!.Trim(), options.SecretKey!.Trim());
        var config = new AmazonS3Config { ForcePathStyle = options.ForcePathStyle };
        var endpoint = NormalizeOptional(options.Endpoint);
        if (endpoint is null)
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region.Trim());
        }
        else
        {
            config.ServiceURL = endpoint;
            config.AuthenticationRegion = options.Region.Trim();
        }
        return new AmazonS3Client(credentials, config);
    }

    private static void Validate(SubmissionOptions submission, StorageOptions storage)
    {
        if (submission.DependencyTimeoutSeconds <= 0) throw new InvalidOperationException("Submission__DependencyTimeoutSeconds must be greater than zero.");
        if (submission.MaxFileNameLength <= 0 || submission.MaxFileNameLength > 255) throw new InvalidOperationException("Submission__MaxFileNameLength must be between 1 and 255.");
        if (submission.UploadSessionLifetimeMinutes <= 0) throw new InvalidOperationException("Submission__UploadSessionLifetimeMinutes must be greater than zero.");
        if (submission.CleanupIntervalMinutes <= 0) throw new InvalidOperationException("Submission__CleanupIntervalMinutes must be greater than zero.");
        if (submission.AllowedFileTypes.Length == 0) throw new InvalidOperationException("Submission__AllowedFileTypes must contain at least one file type.");
        RequireValue(storage.Bucket, "Storage__Bucket");
        RequireValue(storage.AccessKey, "Storage__AccessKey");
        RequireValue(storage.SecretKey, "Storage__SecretKey");
        RequireValue(storage.Region, "Storage__Region");
        if (storage.MaximumFileSizeBytes <= 0) throw new InvalidOperationException("Storage__MaximumFileSizeBytes must be greater than zero.");
        if (storage.PresignedUrlExpirySeconds < 60) throw new InvalidOperationException("Storage__PresignedUrlExpirySeconds must be at least 60 seconds.");
        var endpoint = NormalizeOptional(storage.Endpoint);
        if (endpoint is not null && (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            throw new InvalidOperationException("Storage__Endpoint must be an absolute http/https URL when configured.");
    }

    private static void RequireValue(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value) || IsPlaceholder(value)) throw new InvalidOperationException($"Required configuration '{key}' is missing or still contains a placeholder value.");
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) || IsPlaceholder(value) ? null : value.Trim();
    private static bool IsPlaceholder(string value) => value.Trim().Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase);

    private static string RequireUrl(IConfiguration configuration, string key)
    {
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw) || IsPlaceholder(raw) || !Uri.TryCreate(raw, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException($"Required environment configuration '{key.Replace(':', '_')}' is missing or invalid.");
        return raw.Trim();
    }

    private static Uri EnsureTrailingSlash(string raw) => new(raw.TrimEnd('/') + "/", UriKind.Absolute);
}
