using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ResearchTrack.BuildingBlocks.Kafka;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WebhookReadyEvent(
    Guid EventId, string EventName, string ContractVersion, DateTimeOffset OccurredAtUtc,
    Guid CorrelationId, WebhookReadyData Data);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WebhookReadyData
{
    public Guid? DeliveryRecordId { get; init; }
    public string? DeliveryId { get; init; }
    public long? InstallationId { get; init; }
    public long? RepositoryId { get; init; }
    public Guid? WebhookRecordId { get; init; }
    public Guid? ResearchProjectId { get; init; }
    public string? CloudId { get; init; }
    public required string EventType { get; init; }
}

public sealed class KafkaContractException : Exception
{
    public KafkaContractException() : base("Kafka event failed approved contract or inbox identity validation.") { }
}

public static class KafkaWebhookContract
{
    // MySQL datetime(6) stores microseconds, while .NET timestamps carry 100ns ticks.
    public static DateTimeOffset NormalizeUtc(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true
    };
    public static bool SupportsGitHub(string eventType) => eventType is
        "push" or "pull_request" or "pull_request_review" or "pull_request_review_comment"
        or "repository" or "installation" or "installation_repositories";
    public static bool SupportsJira(string eventType) => eventType is
        "jira:issue_created" or "jira:issue_updated" or "jira:issue_deleted";

    public static string Serialize(WebhookReadyEvent value) => JsonSerializer.Serialize(value, Json);
    public static string Hash(WebhookReadyEvent value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(value))));

    public static string Key(WebhookReadyEvent value) => value.EventName == "github.webhook.ready"
        ? "installation:" + value.Data.InstallationId!.Value.ToString(CultureInfo.InvariantCulture)
        : value.Data.ResearchProjectId!.Value.ToString("D");

    public static WebhookReadyEvent Parse(string? payload, string? key, string service, string version)
    {
        try
        {
            if (payload is null || Encoding.UTF8.GetByteCount(payload) > 16 * 1024) throw new KafkaContractException();
            // Duplicate JSON fields are ambiguous and must not be accepted with last-value-wins.
            using var document = JsonDocument.Parse(payload);
            RequireUniqueFields(document.RootElement);
            var value = JsonSerializer.Deserialize<WebhookReadyEvent>(payload, Json) ?? throw new KafkaContractException();
            if (version != "1" || value.ContractVersion != version || value.EventId == Guid.Empty ||
                value.CorrelationId != value.EventId || value.OccurredAtUtc == default ||
                value.OccurredAtUtc.Offset != TimeSpan.Zero || value.Data is null) throw new KafkaContractException();
            var data = value.Data;
            if (service == "github")
            {
                if (value.EventName != "github.webhook.ready" || data.DeliveryRecordId != value.EventId ||
                    string.IsNullOrWhiteSpace(data.DeliveryId) || data.DeliveryId.Length > 128 ||
                    data.InstallationId is not > 0 || data.RepositoryId is <= 0 ||
                    data.WebhookRecordId is not null || data.ResearchProjectId is not null ||
                    data.CloudId is not null || !SupportsGitHub(data.EventType)) throw new KafkaContractException();
            }
            else if (service == "jira")
            {
                if (value.EventName != "jira.webhook.ready" || data.WebhookRecordId != value.EventId ||
                    data.ResearchProjectId is null || data.ResearchProjectId == Guid.Empty ||
                    string.IsNullOrWhiteSpace(data.CloudId) || data.CloudId.Length > 128 ||
                    data.DeliveryRecordId is not null || data.DeliveryId is not null ||
                    data.InstallationId is not null || data.RepositoryId is not null ||
                    !SupportsJira(data.EventType)) throw new KafkaContractException();
            }
            else throw new KafkaContractException();
            if (Key(value) != key) throw new KafkaContractException();
            return value;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new KafkaContractException();
        }
    }

    private static void RequireUniqueFields(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new KafkaContractException();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new KafkaContractException();
            if (property.Value.ValueKind == JsonValueKind.Object) RequireUniqueFields(property.Value);
        }
    }
}
