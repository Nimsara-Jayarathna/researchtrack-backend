using MySql.Data.MySqlClient;

const string connectionStringVariable = "ConnectionStrings__DefaultConnection";

var connectionString = Environment.GetEnvironmentVariable(connectionStringVariable);
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine($"{connectionStringVariable} is missing.");
    return 1;
}

try
{
    var builder = new MySqlConnectionStringBuilder(connectionString);
    await using var connection = new MySqlConnection(builder.ConnectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT 1";
    await command.ExecuteScalarAsync();

    Console.WriteLine(
        $"Database connectivity OK: server={builder.Server};port={builder.Port};database={builder.Database};user={builder.UserID}");
    return 0;
}
catch (Exception ex) when (ex is MySqlException or InvalidOperationException or ArgumentException)
{
    Console.Error.WriteLine($"Database connectivity failed: {ex.GetType().Name}: {ex.Message}");
    return 1;
}
