using System.Collections.Concurrent;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Features.Synchronization;

public sealed class GitHubInstallationTokenProvider : IGitHubInstallationTokenProvider
{
    private readonly IGitHubAppClient _client;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<long, TokenEntry> _cache = new();
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _locks = new();

    public GitHubInstallationTokenProvider(IGitHubAppClient client, TimeProvider timeProvider)
    {
        _client = client;
        _timeProvider = timeProvider;
    }

    public async Task<string> GetTokenAsync(long installationId, CancellationToken cancellationToken)
    {
        if (installationId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(installationId));
        }

        if (TryGetValidToken(installationId, out var cached))
        {
            return cached;
        }

        var gate = _locks.GetOrAdd(installationId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (TryGetValidToken(installationId, out cached))
            {
                return cached;
            }

            var created = await _client.CreateInstallationTokenAsync(installationId, cancellationToken);
            _cache[installationId] = new TokenEntry(created.Value, created.ExpiresAt);
            return created.Value;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryGetValidToken(long installationId, out string token)
    {
        token = string.Empty;
        if (!_cache.TryGetValue(installationId, out var entry))
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (entry.ExpiresAt <= now.AddMinutes(5))
        {
            _cache.TryRemove(installationId, out _);
            return false;
        }

        token = entry.Value;
        return true;
    }

    private sealed record TokenEntry(string Value, DateTime ExpiresAt);
}
