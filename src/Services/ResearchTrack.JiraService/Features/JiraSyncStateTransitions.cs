using ResearchTrack.JiraService.Domain;

namespace ResearchTrack.JiraService.Features;

internal static class JiraSyncStateTransitions
{
    public static void Begin(JiraConnection connection, DateTimeOffset now)
    {
        connection.SyncStatus = "SYNCING";
        connection.LastSyncError = null;
        connection.UpdatedAt = now;
    }

    public static void Complete(JiraConnection connection, DateTimeOffset now)
    {
        connection.SyncStatus = "SYNCED";
        connection.SyncRevision = checked(connection.SyncRevision + 1);
        connection.LastSyncedAt = now;
        connection.LastSyncError = null;
        connection.LastReconciledAt = now;
        connection.UpdatedAt = now;
    }

    public static void Fail(JiraConnection connection, DateTimeOffset now, bool authorizationFailure, string message)
    {
        connection.SyncStatus = authorizationFailure ? "INVALID_AUTH" : "FAILED";
        connection.LastSyncError = Safe(message);
        connection.UpdatedAt = now;
        // LastSyncedAt intentionally remains unchanged. It represents the last complete snapshot.
    }

    private static string Safe(string message) => message.Length <= 1000 ? message : message[..1000];
}
