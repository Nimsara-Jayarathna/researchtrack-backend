using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.SubmissionService.Configuration;

namespace ResearchTrack.SubmissionService.Infrastructure;

public sealed class S3ObjectStorageService : IObjectStorageService
{
    private readonly AmazonS3Client _s3;
    private readonly StorageOptions _options;
    private readonly TimeProvider _timeProvider;

    public S3ObjectStorageService(AmazonS3Client s3, StorageOptions options, TimeProvider timeProvider)
    {
        _s3 = s3;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<ObjectUploadGrant> CreateUploadGrantAsync(string objectKey, string contentType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expiresAt = _timeProvider.GetUtcNow().AddSeconds(_options.PresignedUrlExpirySeconds);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = expiresAt.UtcDateTime
        };

        try
        {
            var url = await _s3.GetPreSignedURLAsync(request);
            return new ObjectUploadGrant(url, expiresAt, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = contentType
            });
        }
        catch (Exception exception) when (exception is Amazon.Runtime.AmazonServiceException or InvalidOperationException)
        {
            throw StorageUnavailable("Unable to create a secure upload URL.", exception);
        }
    }

    public async Task<StoredObjectMetadata?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _s3.GetObjectMetadataAsync(_options.Bucket, objectKey, cancellationToken);
            return new StoredObjectMetadata(
                response.ContentLength,
                response.ContentType ?? string.Empty,
                response.ETag);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (AmazonS3Exception exception)
        {
            throw StorageUnavailable("Unable to verify the uploaded object.", exception);
        }
    }

    public async Task PromoteAsync(string temporaryObjectKey, string finalObjectKey, CancellationToken cancellationToken)
    {
        try
        {
            await _s3.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket = _options.Bucket,
                SourceKey = temporaryObjectKey,
                DestinationBucket = _options.Bucket,
                DestinationKey = finalObjectKey
            }, cancellationToken);
        }
        catch (AmazonS3Exception exception)
        {
            throw StorageUnavailable("Unable to finalize the uploaded object.", exception);
        }
    }

    public async Task DeleteIfExistsAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            await _s3.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _options.Bucket,
                Key = objectKey
            }, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
        }
        catch (AmazonS3Exception exception)
        {
            throw StorageUnavailable("Unable to remove the temporary upload object.", exception);
        }
    }

    public async Task<ObjectDownloadGrant> CreateDownloadGrantAsync(string objectKey, string fileName, bool inline, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expiresAt = _timeProvider.GetUtcNow().AddSeconds(_options.PresignedUrlExpirySeconds);
        var disposition = inline ? "inline" : "attachment";
        var safeFileName = fileName.Replace("\"", string.Empty, StringComparison.Ordinal);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = expiresAt.UtcDateTime,
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentDisposition = $"{disposition}; filename=\"{safeFileName}\""
            }
        };

        try
        {
            var url = await _s3.GetPreSignedURLAsync(request);
            return new ObjectDownloadGrant(url, expiresAt);
        }
        catch (Exception exception) when (exception is Amazon.Runtime.AmazonServiceException or InvalidOperationException)
        {
            throw StorageUnavailable("Unable to create a secure download URL.", exception);
        }
    }

    private static ApiException StorageUnavailable(string message, Exception exception) =>
        new(StatusCodes.Status503ServiceUnavailable, ErrorCodes.DependencyUnavailable, message, innerException: exception);
}
