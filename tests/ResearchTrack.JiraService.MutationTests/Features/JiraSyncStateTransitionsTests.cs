using ResearchTrack.JiraService.Domain;
using ResearchTrack.JiraService.Features;

namespace ResearchTrack.JiraService.MutationTests.Features;

public sealed class JiraSyncStateTransitionsTests
{
    [Fact]
    public void Begin_SetsSyncingAndClearsPreviousErrorWithoutMovingLastSuccessfulSync()
    {
        var lastGood = DateTimeOffset.UtcNow.AddHours(-1);
        var now = DateTimeOffset.UtcNow;
        var connection = new JiraConnection { SyncStatus = "FAILED", LastSyncError = "old", LastSyncedAt = lastGood };

        JiraSyncStateTransitions.Begin(connection, now);

        Assert.Equal("SYNCING", connection.SyncStatus);
        Assert.Null(connection.LastSyncError);
        Assert.Equal(lastGood, connection.LastSyncedAt);
        Assert.Equal(now, connection.UpdatedAt);
    }

    [Fact]
    public void Complete_MarksSyncedIncrementsRevisionAndMovesSuccessfulTimestamps()
    {
        var now = DateTimeOffset.UtcNow;
        var connection = new JiraConnection { SyncStatus = "SYNCING", SyncRevision = 4, LastSyncError = "old" };

        JiraSyncStateTransitions.Complete(connection, now);

        Assert.Equal("SYNCED", connection.SyncStatus);
        Assert.Equal(5, connection.SyncRevision);
        Assert.Equal(now, connection.LastSyncedAt);
        Assert.Equal(now, connection.LastReconciledAt);
        Assert.Null(connection.LastSyncError);
    }

    [Theory]
    [InlineData(false, "FAILED")]
    [InlineData(true, "INVALID_AUTH")]
    public void Fail_PreservesLastSuccessfulSyncAndSetsExpectedFailureState(bool authorizationFailure, string expectedStatus)
    {
        var lastGood = DateTimeOffset.UtcNow.AddHours(-1);
        var now = DateTimeOffset.UtcNow;
        var connection = new JiraConnection { LastSyncedAt = lastGood, SyncStatus = "SYNCING" };

        JiraSyncStateTransitions.Fail(connection, now, authorizationFailure, "failure");

        Assert.Equal(expectedStatus, connection.SyncStatus);
        Assert.Equal("failure", connection.LastSyncError);
        Assert.Equal(lastGood, connection.LastSyncedAt);
        Assert.Equal(now, connection.UpdatedAt);
    }

    [Fact]
    public void Fail_TruncatesErrorBeforePersisting()
    {
        var connection = new JiraConnection();

        JiraSyncStateTransitions.Fail(connection, DateTimeOffset.UtcNow, false, new string('x', 1200));

        Assert.NotNull(connection.LastSyncError);
        Assert.Equal(1000, connection.LastSyncError!.Length);
    }
}
