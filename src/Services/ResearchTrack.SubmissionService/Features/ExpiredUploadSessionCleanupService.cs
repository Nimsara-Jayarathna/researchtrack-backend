using Microsoft.EntityFrameworkCore;
using ResearchTrack.SubmissionService.Configuration;
using ResearchTrack.SubmissionService.Domain;
using ResearchTrack.SubmissionService.Infrastructure;
using ResearchTrack.SubmissionService.Persistence;

namespace ResearchTrack.SubmissionService.Features;

public sealed class ExpiredUploadSessionCleanupService : BackgroundService
{
    private readonly IDbContextFactory<SubmissionDbContext> _dbContextFactory;
    private readonly IObjectStorageService _storage;
    private readonly SubmissionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ExpiredUploadSessionCleanupService> _logger;

    public ExpiredUploadSessionCleanupService(IDbContextFactory<SubmissionDbContext> dbContextFactory, IObjectStorageService storage, SubmissionOptions options, TimeProvider timeProvider, ILogger<ExpiredUploadSessionCleanupService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _storage = storage;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.CleanupIntervalMinutes), _timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await CleanupAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { _logger.LogError(exception, "Submission upload-session cleanup failed."); }
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var expired = await db.SubmissionUploadSessions.Where(x => x.Status == SubmissionConstants.UploadSessionStatus.Pending && x.ExpiresAt <= now).Take(100).ToListAsync(cancellationToken);
        foreach (var session in expired)
        {
            session.Status = SubmissionConstants.UploadSessionStatus.Expired;
            session.ActiveSlot = null;
            session.FailureReason = "Upload session expired before completion.";
        }
        if (expired.Count == 0) return;
        await db.SaveChangesAsync(cancellationToken);
        foreach (var session in expired)
        {
            try { await _storage.DeleteIfExistsAsync(session.TemporaryObjectKey, cancellationToken); }
            catch (Exception exception) { _logger.LogWarning(exception, "Unable to delete expired temporary object {ObjectKey}", session.TemporaryObjectKey); }
        }
    }
}
