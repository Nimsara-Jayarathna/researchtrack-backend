namespace ResearchTrack.SubmissionService.Infrastructure;

public sealed record StoredObjectMetadata(long ContentLength, string ContentType, string? ETag);
public sealed record ObjectUploadGrant(string Url, DateTimeOffset ExpiresAt, IReadOnlyDictionary<string, string> RequiredHeaders);
public sealed record ObjectDownloadGrant(string Url, DateTimeOffset ExpiresAt);

public interface IObjectStorageService
{
    Task<ObjectUploadGrant> CreateUploadGrantAsync(string objectKey, string contentType, CancellationToken cancellationToken);
    Task<StoredObjectMetadata?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken);
    Task PromoteAsync(string temporaryObjectKey, string finalObjectKey, CancellationToken cancellationToken);
    Task DeleteIfExistsAsync(string objectKey, CancellationToken cancellationToken);
    Task<ObjectDownloadGrant> CreateDownloadGrantAsync(string objectKey, string fileName, bool inline, CancellationToken cancellationToken);
}
