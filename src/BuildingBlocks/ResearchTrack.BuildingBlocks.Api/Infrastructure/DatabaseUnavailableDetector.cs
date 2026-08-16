using System.Data.Common;
using System.Net.Sockets;

namespace ResearchTrack.BuildingBlocks.Api.Infrastructure;

internal static class DatabaseUnavailableDetector
{
    public static bool IsDatabaseUnavailable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException or SocketException or TimeoutException or IOException)
            {
                return true;
            }
        }

        return false;
    }
}
