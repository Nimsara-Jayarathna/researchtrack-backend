using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Infrastructure;

internal static class ProjectRepositoryMutationLock
{
    private const int TimeoutSeconds = 10;

    public static async Task<IAsyncDisposable> AcquireAsync(
        GitHubDbContext dbContext,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }

        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        var connection = dbContext.Database.GetDbConnection();
        var lockName = $"rt:github:project:{projectId:N}";

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT GET_LOCK(@lockName, @timeoutSeconds);";
        AddParameter(command, "@lockName", lockName);
        AddParameter(command, "@timeoutSeconds", TimeoutSeconds);

        var raw = await command.ExecuteScalarAsync(cancellationToken);
        var acquired = raw is not null
            && raw != DBNull.Value
            && Convert.ToInt32(raw, CultureInfo.InvariantCulture) == 1;
        if (!acquired)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Another GitHub repository update is already in progress for this project. Please retry.");
        }

        return new Handle(connection, lockName);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed class Handle(DbConnection connection, string lockName) : IAsyncDisposable
    {
        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            if (_disposed || connection.State != System.Data.ConnectionState.Open)
            {
                return;
            }

            _disposed = true;
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT RELEASE_LOCK(@lockName);";
                AddParameter(command, "@lockName", lockName);
                await command.ExecuteScalarAsync(CancellationToken.None);
            }
            catch
            {
                // MySQL also releases named locks when this DbContext connection is closed.
                // Do not mask the repository operation result with best-effort cleanup failure.
            }
        }
    }
}
