using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ResearchTrack.GitHubService.Infrastructure;

public static class PublicRepositoryIdentity
{
    public static long CreateSyntheticId(string owner, string repository)
    {
        var normalized = $"{owner.Trim().ToLowerInvariant()}/{repository.Trim().ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var value = BinaryPrimitives.ReadInt64BigEndian(hash.AsSpan(0, sizeof(long)));

        // GitHub repository ids are positive. Keeping provisional ids negative makes
        // them unambiguous and lets synchronization promote them to the real id later.
        if (value == long.MinValue)
        {
            return long.MinValue + 1;
        }

        value = -Math.Abs(value);
        return value == 0 ? -1 : value;
    }

    public static bool IsSynthetic(long repositoryId) => repositoryId < 0;
}
