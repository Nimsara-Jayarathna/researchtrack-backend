using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ResearchTrack.Kafka.Tests;

// Relational SQLite proves atomic writes/constraints locally; live fixtures use MySQL.
public sealed class KafkaTestDatabase<TContext> : IDbContextFactory<TContext>, IDisposable where TContext : DbContext
{
    private readonly SqliteConnection? _connection;
    private readonly Func<DbContextOptions<TContext>, TContext> _create;
    private readonly DbContextOptions<TContext> _options;
    public KafkaTestDatabase(Func<DbContextOptions<TContext>, TContext> create, string? mysqlConnection = null)
    {
        _create = create;
        var builder = new DbContextOptionsBuilder<TContext>();
        if (mysqlConnection is null)
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            builder.UseSqlite(_connection).ReplaceService<IModelCustomizer, KafkaTestModelCustomizer>();
        }
        else builder.UseMySQL(mysqlConnection);
        _options = builder.Options;
        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }
    public TContext CreateDbContext() => _create(_options);
    public Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    public void Dispose() => _connection?.Dispose();
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
