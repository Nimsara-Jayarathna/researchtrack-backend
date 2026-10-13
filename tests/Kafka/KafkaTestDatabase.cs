using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ResearchTrack.Kafka.Tests;

// Relational SQLite proves atomic writes/constraints locally; live fixtures use MySQL.
public sealed class KafkaTestDatabase<TContext> : IDbContextFactory<TContext>, IDisposable where TContext : DbContext
{
    private readonly string? _directory;
    private readonly Func<DbContextOptions<TContext>, TContext> _create;
    private readonly DbContextOptions<TContext> _options;
    public KafkaTestDatabase(Func<DbContextOptions<TContext>, TContext> create, string? mysqlConnection = null, bool initializeSchema = true)
    {
        _create = create;
        var builder = new DbContextOptionsBuilder<TContext>();
        if (mysqlConnection is null)
        {
            _directory = Path.Combine(Path.GetTempPath(), "researchtrack-kafka-db-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            builder.UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_directory, "fixture.sqlite"), Pooling = false, DefaultTimeout = 10
            }.ToString()).ReplaceService<IModelCustomizer, KafkaTestModelCustomizer>();
        }
        else builder.UseMySQL(mysqlConnection);
        _options = builder.Options;
        using var db = CreateDbContext();
        if (initializeSchema) db.Database.EnsureCreated();
        if (mysqlConnection is null) db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }
    public TContext CreateDbContext() => _create(_options);
    public Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    public void Dispose()
    {
        if (_directory is not null) Directory.Delete(_directory, recursive: true);
    }
}

public sealed class KafkaTestModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        // SQLite cannot compare DateTimeOffset natively. Preserve UTC ordering in tests.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
            if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                property.SetValueConverter(new ValueConverter<DateTimeOffset, long>(x => x.UtcTicks,
                    x => new DateTimeOffset(x, TimeSpan.Zero)));
    }
}

public sealed class KafkaTestLease : IAsyncDisposable
{
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

// Runs only on the runner-created loopback MySQL upgrade databases. No EnsureCreated
// or table repair: apply real historical migrations, preserve data, then migrate up.
public static class KafkaMigrationUpgrade
{
    public static async Task VerifyAsync<TContext>(string service, string baseline,
        Func<DbContextOptions<TContext>, TContext> create, Func<KafkaTestDatabase<TContext>, Task> seed,
        CancellationToken ct) where TContext : DbContext
    {
        var connection = new System.Data.Common.DbConnectionStringBuilder
        {
            ConnectionString = IsolatedKafkaFixture.DatabaseConnection(service)
        };
        var expected = "researchtrack_kafka_" + service;
        if (!Equals(connection["Database"], expected)) throw new InvalidOperationException("Disposable upgrade database required.");
        connection["Database"] = expected + "_upgrade";
        using var factory = new KafkaTestDatabase<TContext>(create, connection.ConnectionString, initializeSchema: false);
        await using var db = factory.CreateDbContext();
        var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        await migrator.MigrateAsync(baseline, ct);
        await seed(factory);
        var before = await SnapshotAsync(db, ct);
        if (before.Values.Sum(x => x.Rows) == 0) throw new InvalidOperationException("Older schema must contain representative records.");
        await migrator.MigrateAsync(cancellationToken: ct);
        var after = await SnapshotAsync(db, ct);
        foreach (var (table, snapshot) in before)
            if (!after.TryGetValue(table, out var actual) || snapshot != actual)
                throw new InvalidOperationException("Migration changed existing schema or records: " + table);
        foreach (var table in new[] { "kafka_outbox_messages", "kafka_inbox_receipts" })
            if (!after.ContainsKey(table)) throw new InvalidOperationException("Missing additive table: " + table);
        await VerifyIndexesAsync(db, ct);
        var history = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        if (history.Last() != "20261010151156_AddKafkaWebhookMessaging") throw new InvalidOperationException("Migration order mismatch.");
        await migrator.MigrateAsync(cancellationToken: ct);
        if (!(await db.Database.GetAppliedMigrationsAsync(ct)).SequenceEqual(history)) throw new InvalidOperationException("Migration reapply changed history.");
        var id = Guid.NewGuid();
        db.Set<ResearchTrack.BuildingBlocks.Kafka.KafkaOutboxMessage>().Add(new()
        {
            EventId = id, Topic = "researchtrack." + service + ".events.v1", MessageKey = "synthetic-upgrade",
            PayloadJson = "{}", CreatedAtUtc = DateTime.UtcNow, NextAttemptAtUtc = DateTime.UtcNow
        });
        db.Set<ResearchTrack.BuildingBlocks.Kafka.KafkaInboxReceipt>().Add(new()
        {
            EventId = id, PayloadHash = new string('a', 64), Outcome = "scheduled", HandedOffAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        if (!await db.Set<ResearchTrack.BuildingBlocks.Kafka.KafkaOutboxMessage>().AnyAsync(x => x.EventId == id, ct)
            || !await db.Set<ResearchTrack.BuildingBlocks.Kafka.KafkaInboxReceipt>().AnyAsync(x => x.EventId == id, ct))
            throw new InvalidOperationException("Upgraded inbox/outbox cannot persist records.");
        await IsolatedKafkaFixture.WriteEvidenceAsync(service + "-migration-upgrade", new
        {
            baseline, migration = history.Last(), appliedMigrations = history.Length,
            preservedTables = before.Count, preservedRows = before.Values.Sum(x => x.Rows),
            schemaAndRecordsPreserved = true, reapplyIdempotent = true, inboxOutboxRoundTrip = true,
            inboxIdentityAndOutboxRetryOrderingIndexesVerified = true
        }, ct);
    }

    private sealed record TableSnapshot(string SchemaHash, string DataHash, int Rows);

    private static async Task VerifyIndexesAsync(DbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TABLE_NAME, INDEX_NAME, NON_UNIQUE, GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX) "
            + "FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() "
            + "AND TABLE_NAME IN ('kafka_inbox_receipts','kafka_outbox_messages') "
            + "GROUP BY TABLE_NAME, INDEX_NAME, NON_UNIQUE";
        var actual = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            actual.Add($"{reader.GetString(0)}:{reader.GetString(1)}:{Convert.ToInt32(reader.GetValue(2))}:{reader.GetString(3)}");
        string[] expected =
        [
            "kafka_inbox_receipts:PRIMARY:0:EventId",
            "kafka_outbox_messages:PRIMARY:0:Sequence",
            "kafka_outbox_messages:IX_kafka_outbox_messages_EventId:0:EventId",
            "kafka_outbox_messages:IX_kafka_outbox_messages_MessageKey_Sequence:1:MessageKey,Sequence",
            "kafka_outbox_messages:IX_kafka_outbox_messages_Status_NextAttemptAtUtc:1:Status,NextAttemptAtUtc"
        ];
        if (!actual.SetEquals(expected)) throw new InvalidOperationException("Upgraded Kafka identity/retry indexes differ from the contract.");
    }
    private static async Task<Dictionary<string, TableSnapshot>> SnapshotAsync(DbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct);
        await using var list = connection.CreateCommand();
        list.CommandText = "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME <> '__EFMigrationsHistory' ORDER BY TABLE_NAME";
        var tables = new List<string>();
        await using (var reader = await list.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) tables.Add(reader.GetString(0));
        var result = new Dictionary<string, TableSnapshot>();
        foreach (var table in tables)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(table, "^[A-Za-z0-9_]+$")) throw new InvalidOperationException("Unexpected table identifier.");
            await using var schema = connection.CreateCommand();
            schema.CommandText = "SHOW CREATE TABLE `" + table + "`";
            string definition;
            await using (var reader = await schema.ExecuteReaderAsync(ct))
            {
                await reader.ReadAsync(ct);
                definition = reader.GetString(1);
            }
            await using var rows = connection.CreateCommand();
            rows.CommandText = "SELECT * FROM `" + table + "`";
            var values = new List<string>();
            await using (var reader = await rows.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                {
                    var row = new object[reader.FieldCount];
                    reader.GetValues(row);
                    values.Add(System.Text.Json.JsonSerializer.Serialize(row));
                }
            values.Sort(StringComparer.Ordinal);
            static string Hash(string text) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
            result.Add(table, new(Hash(definition), Hash(string.Join("\n", values)), values.Count));
        }
        return result;
    }
}
