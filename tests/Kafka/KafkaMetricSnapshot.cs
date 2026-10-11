using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Prometheus;

namespace ResearchTrack.Kafka.Tests;

public static class KafkaMetricSnapshot
{
    public static async Task<string> ExportAsync(CollectorRegistry registry, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        await registry.CollectAndExportAsTextAsync(stream, ct);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    public static double Value(string text, string name, params (string Key, string Value)[] labels)
    {
        foreach (var line in text.Split('\n'))
        {
            if (!line.StartsWith(name + "{", StringComparison.Ordinal) && !line.StartsWith(name + " ", StringComparison.Ordinal)) continue;
            if (labels.All(label => line.Contains(label.Key + "=\"" + label.Value + "\"", StringComparison.Ordinal)))
                return double.Parse(line[(line.LastIndexOf(' ') + 1)..], CultureInfo.InvariantCulture);
        }
        throw new InvalidOperationException("Expected metric sample absent: " + name);
    }
    public static void AssertBoundedLabels(string text, string service)
    {
        foreach (var line in text.Split('\n').Where(x => x.StartsWith("researchtrack_kafka_", StringComparison.Ordinal)))
        foreach (Match label in Regex.Matches(line, "([a-z_]+)=\"([^\"]*)\""))
        {
            var allowed = label.Groups[1].Value switch
            {
                "service" => label.Groups[2].Value == service,
                "outcome" => new[] { "success", "retryable_failure", "permanent_failure", "duplicate", "noop", "failed", "rejected" }.Contains(label.Groups[2].Value),
                "stage" => new[] { "publish", "process", "commit" }.Contains(label.Groups[2].Value),
                "status" => new[] { "pending", "failed" }.Contains(label.Groups[2].Value),
                "le" => double.TryParse(label.Groups[2].Value, CultureInfo.InvariantCulture, out _) || label.Groups[2].Value == "+Inf",
                _ => false
            };
            if (!allowed) throw new InvalidOperationException("Unexpected metric label: " + label.Groups[1].Value);
        }
    }
    public static async Task SaveIsolatedAsync(string service, CollectorRegistry registry, CancellationToken ct)
    {
        var directory = Environment.GetEnvironmentVariable("RT_KAFKA_TEST_METRICS_DIRECTORY");
        if (!IsolatedKafkaFixture.Enabled || directory is null) throw new InvalidOperationException("Use the isolated fixture runner.");
        var text = await ExportAsync(registry, ct);
        AssertBoundedLabels(text, service);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, service + ".prom");
        var next = path + ".next";
        await File.WriteAllTextAsync(next, text, ct);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(next, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        File.Move(next, path, overwrite: true);
    }
}
