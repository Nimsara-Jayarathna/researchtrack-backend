extern alias JiraApp;
extern alias GatewayApp;

using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MySql.Data.MySqlClient;
using ResearchTrack.Testing;
using JiraApp::ResearchTrack.JiraService.Contracts;
using JiraApp::ResearchTrack.JiraService.Domain;
using JiraApp::ResearchTrack.JiraService.Features;
using JiraApp::ResearchTrack.JiraService.Infrastructure;
using JiraApp::ResearchTrack.JiraService.Persistence;

namespace ResearchTrack.JiraIssuesBenchmark;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly AsyncLocal<RequestProbe?> Current = new();
    private static readonly Guid[] Projects = [Guid.Parse("10000000-0000-0000-0000-000000000001"), Guid.Parse("10000000-0000-0000-0000-000000000002"), Guid.Parse("10000000-0000-0000-0000-000000000003")];
    private static readonly int[] Sizes = [100, 1000, 5000];
    private static readonly ConcurrentDictionary<Guid, List<CapturedCommand>> Plans = new();
    private static long _commands;
    private static long _authCalls;
    private static int _authDelay;
    private static string _token = "";

    public static async Task Main()
    {
        var connection = Environment.GetEnvironmentVariable("RT_JIRA_PERF_CONNECTION") ?? "";
        var parsed = new MySqlConnectionStringBuilder(connection);
        if (Environment.GetEnvironmentVariable("RT_JIRA_PERF_ENABLE") != "1" || parsed.Server != "127.0.0.1" ||
            parsed.Port != 13308 || parsed.Database != "researchtrack_test_jira_perf") throw new InvalidOperationException("Use the guarded disposable performance runner.");
        await VerifyContainerAsync();
        var output = Environment.GetEnvironmentVariable("RT_JIRA_PERF_OUTPUT")!;
        var root = Environment.GetEnvironmentVariable("RT_JIRA_PERF_ROOT")!;
        var factory = new DatabaseFactory(connection);
        await SeedAsync(factory);
        var signing = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-signing-key-that-is-at-least-32-bytes-long-123456789")), SecurityAlgorithms.HmacSha256);
        _token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("ResearchTrack.AuthService.Tests", "ResearchTrack.Tests",
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, "Supervisor")], expires: DateTime.UtcNow.AddHours(2), signingCredentials: signing));
        await using var jiraRoot = new ResearchTrackWebApplicationFactory<JiraApp::Program>(connection, services =>
        {
            foreach (var registration in services.Where(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType?.Namespace?.StartsWith("ResearchTrack.", StringComparison.Ordinal) == true).ToArray()) services.Remove(registration);
            services.RemoveAll<IDbContextFactory<JiraDbContext>>();
            services.AddSingleton<IDbContextFactory<JiraDbContext>>(factory);
            services.AddHttpClient<IProjectAuthorizationClient, ProjectAuthorizationClient>().ConfigurePrimaryHttpMessageHandler(() => new AuthorizationStub());
            services.AddScoped<IJiraIssueQueryService, TimedQuery>();
            services.AddLogging(logging => logging.ClearProviders());
        });
        await using var jira = jiraRoot.WithWebHostBuilder(builder => builder.UseContentRoot(Path.Combine(root, "src/Services/ResearchTrack.JiraService")));
        jira.UseKestrel(options => options.Listen(IPAddress.Loopback, 19114));
        using var jiraClient = jira.CreateClient();
        await using var gatewayRoot = new ResearchTrackWebApplicationFactory<GatewayApp::Program>();
        await using var gateway = gatewayRoot.WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(Path.Combine(root, "src/Gateway/ResearchTrack.Gateway"));
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReverseProxy:Clusters:jira:Destinations:primary:Address"] = "http://127.0.0.1:19114/",
                ["RateLimiting:PerformanceTest:Enabled"] = "false"
            }));
            builder.ConfigureServices(services =>
            {
                // Fixture only: replace the gateway's wildcard/metrics listeners.
                // Production configuration and the authorization pipeline are unchanged.
                services.RemoveAll<IConfigureOptions<KestrelServerOptions>>();
                // Capacity measurement must not be capped by the normal 120/minute
                // per-IP bucket. Rate-limit correctness is covered by existing tests.
                services.PostConfigure<RateLimiterOptions>(options => options.GlobalLimiter = null);
            });
            builder.ConfigureLogging(logging => logging.ClearProviders());
        });
        gateway.UseKestrel(options => options.Listen(IPAddress.Loopback, 19115));
        using var http = gateway.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("http://127.0.0.1:19115") });
        http.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        VerifyListener(jira.Services, 19114);
        VerifyListener(gateway.Services, 19115);
        // Contract and authorization controls before timing; no real provider/network auth.
        for (var i = 0; i < Sizes.Length; i++)
        {
            using var response = await http.GetAsync(Route(Projects[i]));
            if (response.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("Expected HTTP 200.");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = body.RootElement.GetProperty("data");
            var items = data.GetProperty("items");
            if (!body.RootElement.GetProperty("success").GetBoolean() || items.GetArrayLength() != Sizes[i] || data.GetProperty("summary").GetProperty("total").GetInt32() != Sizes[i] ||
                items[0].GetProperty("issueKey").GetString() != "RT-00000" || data.GetProperty("sync").GetProperty("status").GetString() != "SYNCED") throw new InvalidOperationException("Contract sanity check failed.");
            var index = 0;
            foreach (var item in items.EnumerateArray())
            {
                if (item.GetProperty("issueKey").GetString() != $"RT-{index++:D5}" ||
                    item.EnumerateObject().Count() != 15 ||
                    !item.GetProperty("descriptionJson").GetString()!.Contains(new string('x', i == 0 ? 1024 : 2048), StringComparison.Ordinal))
                    throw new InvalidOperationException("Issue ordering, projection or description changed.");
            }
        }
        var beforeCommands = Interlocked.Read(ref _commands);
        using (var denied = await http.GetAsync(Route(Guid.Empty))) if (denied.StatusCode != HttpStatusCode.Forbidden) throw new InvalidOperationException("Authorization regression.");
        if (Interlocked.Read(ref _commands) != beforeCommands) throw new InvalidOperationException("Denied request reached Jira MySQL.");
        var beforeAuth = Interlocked.Read(ref _authCalls);
        using (var request = new HttpRequestMessage(HttpMethod.Get, Route(Projects[0])))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-synthetic-token");
            using var denied = await http.SendAsync(request);
            if (denied.StatusCode != HttpStatusCode.Unauthorized || Interlocked.Read(ref _authCalls) != beforeAuth) throw new InvalidOperationException("JWT rejection regression.");
        }
        Console.WriteLine("PASS: three response contracts; forbidden-before-DB and invalid-JWT-before-auth checks.");
        var results = new List<object>();
        for (var i = 0; i < Sizes.Length; i++)
        foreach (var concurrency in new[] { 1, 4 })
        for (var run = 1; run <= 3; run++)
        {
            for (var warm = 0; warm < 20; warm++) await MeasureAsync(http, Projects[i]);
            var samples = new ConcurrentBag<Sample>();
            await Parallel.ForEachAsync(Enumerable.Range(0, 200), new ParallelOptions { MaxDegreeOfParallelism = concurrency }, async (_, _) => samples.Add(await MeasureAsync(http, Projects[i])));
            var summary = Summarize(Sizes[i], concurrency, run, samples.ToArray(), "baseline");
            results.Add(summary);
            Console.WriteLine(JsonSerializer.Serialize(summary));
        }
        // Controlled sensitivity, not a reproduction or attribution of production latency.
        _authDelay = 100;
        for (var run = 1; run <= 3; run++)
        {
            var samples = new List<Sample>();
            for (var i = 0; i < 200; i++) samples.Add(await MeasureAsync(http, Projects[1]));
            var summary = Summarize(1000, 1, run, samples.ToArray(), "auth-delay-100ms");
            results.Add(summary);
            Console.WriteLine(JsonSerializer.Serialize(summary));
        }
        await File.WriteAllTextAsync(Path.Combine(output, "measurements.json"), JsonSerializer.Serialize(new { scope = "disposable loopback Gateway/Jira/MySQL; synthetic authorization and data; no Kafka workers", requestsPerRun = 200, warmupPerBaselineRun = 20, results }, Json));
        await ExplainAsync(factory, output);
    }

    private static string Route(Guid project) => $"/api/v1/projects/{project}/jira/issues";
    private static void VerifyListener(IServiceProvider services, int port)
    {
        var addresses = services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        if (addresses.Count != 1 || addresses.Single() != $"http://127.0.0.1:{port}")
            throw new InvalidOperationException("Only the fixture's loopback HTTP listener is allowed.");
    }
    private static async Task<Sample> MeasureAsync(HttpClient http, Guid project)
    {
        var timer = Stopwatch.StartNew();
        using var response = await http.GetAsync(Route(project), HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        timer.Stop();
        var stats = JsonSerializer.Deserialize<RequestProbe>(response.Headers.GetValues("X-Local-Benchmark").Single())!;
        if (stats.Commands != 2 || stats.AuthorizationCalls != 1) throw new InvalidOperationException("Unexpected SQL/auth request count.");
        return new(timer.Elapsed.TotalMilliseconds, stats.ServiceMs, stats.AuthMs, stats.ReaderOpenMs, bytes.Length);
    }
    private static object Summarize(int issues, int concurrency, int run, Sample[] samples, string scenario)
    {
        static double Quantile(IEnumerable<double> values, double p) { var sorted = values.Order().ToArray(); var index = (sorted.Length - 1) * p; var lower = (int)index; return Math.Round(sorted[lower] + (sorted[Math.Min(lower + 1, sorted.Length - 1)] - sorted[lower]) * (index - lower), 3); }
        return new { scenario, issues, concurrency, run, samples = samples.Length, bytes = samples[0].Bytes,
            p50Ms = Quantile(samples.Select(x => x.HttpMs), .5), p95Ms = Quantile(samples.Select(x => x.HttpMs), .95), p99Ms = Quantile(samples.Select(x => x.HttpMs), .99),
            serviceP95Ms = Quantile(samples.Select(x => x.ServiceMs), .95), authP95Ms = Quantile(samples.Select(x => x.AuthMs), .95), readerOpenP95Ms = Quantile(samples.Select(x => x.ReaderOpenMs), .95) };
    }
    private static async Task SeedAsync(DatabaseFactory factory)
    {
        await using var db = factory.CreateDbContext();
        await db.Database.MigrateAsync();
        if (await db.JiraConnections.AnyAsync()) throw new InvalidOperationException("Fresh runner-owned database required; never clear existing rows.");
        var now = DateTimeOffset.UtcNow;
        for (var p = 0; p < Sizes.Length; p++)
        {
            var connection = new JiraConnection { Id = Guid.NewGuid(), ResearchProjectId = Projects[p], CloudId = "synthetic-cloud", WorkspaceName = "Synthetic", JiraProjectId = p.ToString(CultureInfo.InvariantCulture), JiraProjectKey = "RT", JiraProjectName = "Synthetic", AccessTokenProtected = "synthetic-never-decrypted", ConnectedByUserId = Guid.NewGuid(), ConnectedAt = now, UpdatedAt = now, LastSyncedAt = now, SyncStatus = "SYNCED" };
            db.JiraConnections.Add(connection);
            for (var i = 0; i < Sizes[p]; i++) db.JiraIssues.Add(new JiraIssue { Id = Guid.NewGuid(), JiraConnectionId = connection.Id, ResearchProjectId = Projects[p], JiraIssueId = i.ToString(CultureInfo.InvariantCulture), IssueKey = $"RT-{i:D5}", Summary = $"Synthetic issue {i}", IssueTypeName = "Task", StatusName = i % 3 == 0 ? "Done" : "Open", StatusCategoryKey = i % 3 == 0 ? "done" : i % 3 == 1 ? "new" : "indeterminate", DescriptionJson = JsonSerializer.Serialize(new { type = "doc", version = 1, text = new string('x', p == 0 ? 1024 : 2048) }), SyncedAt = now, JiraCreatedAt = now, JiraUpdatedAt = now });
        }
        await db.SaveChangesAsync();
    }
    private static async Task ExplainAsync(DatabaseFactory factory, string output)
    {
        await using var db = factory.CreateDbContext();
        await db.Database.OpenConnectionAsync();
        var results = new List<object>();
        foreach (var (project, commands) in Plans)
        foreach (var captured in commands)
        {
            if (!captured.Sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Only captured read queries may be explained.");
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "EXPLAIN ANALYZE " + captured.Sql;
            foreach (var (name, value) in captured.Parameters) { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter); }
            var lines = new List<string>();
            await using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync()) lines.Add(reader.GetString(0));
            command.CommandText = "EXPLAIN FORMAT=JSON " + captured.Sql;
            var estimate = JsonSerializer.Deserialize<JsonElement>((string)(await command.ExecuteScalarAsync())!);
            results.Add(new { project, captured.Sql, analysis = lines, estimate });
        }
        await File.WriteAllTextAsync(Path.Combine(output, "explain-analyze.json"), JsonSerializer.Serialize(results, Json));
    }
    private static async Task VerifyContainerAsync()
    {
        using var process = new Process { StartInfo = new("docker") { RedirectStandardOutput = true, RedirectStandardError = true } };
        process.StartInfo.ArgumentList.Add("inspect"); process.StartInfo.ArgumentList.Add(Environment.GetEnvironmentVariable("RT_JIRA_PERF_CONTAINER_ID") ?? "");
        process.Start(); var text = await process.StandardOutput.ReadToEndAsync(); await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException("Fixture identity unavailable.");
        using var doc = JsonDocument.Parse(text); var c = doc.RootElement[0];
        var ports = c.GetProperty("HostConfig").GetProperty("PortBindings").GetProperty("3306/tcp");
        if (!c.GetProperty("Name").GetString()!.StartsWith("/rt-jira-perf-", StringComparison.Ordinal) || c.GetProperty("Config").GetProperty("Labels").GetProperty("researchtrack.fixture").GetString() != "jira-issues-perf" || ports.GetArrayLength() != 1 || ports[0].GetProperty("HostIp").GetString() != "127.0.0.1" || ports[0].GetProperty("HostPort").GetString() != "13308") throw new InvalidOperationException("Not the runner-owned loopback performance fixture.");
    }
    private sealed record Sample(double HttpMs, double ServiceMs, double AuthMs, double ReaderOpenMs, int Bytes);
    public sealed class RequestProbe { public Guid Project { get; set; } public int Commands { get; set; } public int AuthorizationCalls { get; set; } public double ServiceMs { get; set; } public double AuthMs { get; set; } public double ReaderOpenMs { get; set; } public List<CapturedCommand> Captured { get; } = []; }
    public sealed record CapturedCommand(string Sql, Dictionary<string, object> Parameters);
    private sealed class DatabaseFactory(string connection) : IDbContextFactory<JiraDbContext>
    {
        public JiraDbContext CreateDbContext() => new(new DbContextOptionsBuilder<JiraDbContext>().UseMySQL(connection).AddInterceptors(new QueryProbe()).Options);
        public Task<JiraDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
    private sealed class QueryProbe : DbCommandInterceptor
    {
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _commands);
            if (Current.Value is { } probe)
            {
                probe.Commands++; probe.ReaderOpenMs += eventData.Duration.TotalMilliseconds;
                if (Plans.Count < 3 || !Plans.ContainsKey(probe.Project)) probe.Captured.Add(new(command.CommandText, command.Parameters.Cast<DbParameter>().ToDictionary(x => x.ParameterName, x => x.Value ?? DBNull.Value)));
            }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class AuthorizationStub : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host != "localhost" || request.RequestUri.Port != 5102 || request.Method != HttpMethod.Get || request.Headers.Authorization?.Parameter != _token) throw new InvalidOperationException("Unexpected auth target or forwarded credential.");
            Interlocked.Increment(ref _authCalls); if (Current.Value is { } probe) probe.AuthorizationCalls++;
            var timer = Stopwatch.StartNew(); if (_authDelay != 0) await Task.Delay(_authDelay, cancellationToken);
            if (Current.Value is { } stats) stats.AuthMs += timer.Elapsed.TotalMilliseconds;
            return new(Projects.Any(p => request.RequestUri.AbsolutePath == $"/api/v1/projects/{p}/authorization/access") ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden);
        }
    }
    private sealed class TimedQuery(IDbContextFactory<JiraDbContext> db, IProjectAuthorizationClient auth, IHttpContextAccessor context) : IJiraIssueQueryService
    {
        public async Task<JiraIssueListResponse> GetIssuesAsync(Guid projectId, CancellationToken ct)
        {
            var probe = new RequestProbe { Project = projectId }; Current.Value = probe;
            var timer = Stopwatch.StartNew();
            try
            {
                var result = await new JiraIssueQueryService(db, auth).GetIssuesAsync(projectId, ct);
                probe.ServiceMs = timer.Elapsed.TotalMilliseconds;
                Plans.TryAdd(projectId, probe.Captured);
                context.HttpContext!.Response.Headers["X-Local-Benchmark"] = JsonSerializer.Serialize(new { probe.Commands, probe.AuthorizationCalls, probe.ServiceMs, probe.AuthMs, probe.ReaderOpenMs });
                return result;
            }
            finally { Current.Value = null; }
        }
        public Task<JiraHealthResponse> GetHealthAsync(Guid projectId, CancellationToken ct) => new JiraIssueQueryService(db, auth).GetHealthAsync(projectId, ct);
    }
}
