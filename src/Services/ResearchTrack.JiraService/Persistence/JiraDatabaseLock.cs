using System.Data;
using Microsoft.EntityFrameworkCore;

namespace ResearchTrack.JiraService.Persistence;

/// <summary>
/// MySQL named locks used to coordinate Jira synchronization across service replicas.
/// Locks are connection-scoped, so the supplied DbContext must remain alive while the lease is held.
/// </summary>
internal static class JiraDatabaseLock
{
    public static string ProjectSync(Guid projectId) => $"rt:jira:sync:{projectId:N}";
    public static string ProjectSchedule(Guid projectId) => $"rt:jira:schedule:{projectId:N}";
    public static string ProjectWebhook(Guid projectId) => $"rt:jira:webhook:{projectId:N}";
    public const string JobClaim = "rt:jira:job-claim";

    public static async Task<IAsyncDisposable?> TryAcquireAsync(JiraDbContext db, string name, int timeoutSeconds, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT GET_LOCK(@name, @timeout)";
        var nameParameter = command.CreateParameter();
        nameParameter.ParameterName = "@name";
        nameParameter.Value = name;
        command.Parameters.Add(nameParameter);
        var timeoutParameter = command.CreateParameter();
        timeoutParameter.ParameterName = "@timeout";
        timeoutParameter.Value = timeoutSeconds;
        command.Parameters.Add(timeoutParameter);
        var result = await command.ExecuteScalarAsync(ct);
        if (result is null || result is DBNull || Convert.ToInt32(result) != 1)
        {
            await db.Database.CloseConnectionAsync();
            return null;
        }
        return new Lease(db, name);
    }

    private sealed class Lease : IAsyncDisposable
    {
        private readonly JiraDbContext _db;
        private readonly string _name;
        private bool _released;

        public Lease(JiraDbContext db, string name) { _db = db; _name = name; }

        public async ValueTask DisposeAsync()
        {
            if (_released) return;
            _released = true;
            try
            {
                var connection = _db.Database.GetDbConnection();
                if (connection.State == ConnectionState.Open)
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "SELECT RELEASE_LOCK(@name)";
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@name";
                    parameter.Value = _name;
                    command.Parameters.Add(parameter);
                    await command.ExecuteScalarAsync();
                }
            }
            finally
            {
                await _db.Database.CloseConnectionAsync();
            }
        }
    }
}
