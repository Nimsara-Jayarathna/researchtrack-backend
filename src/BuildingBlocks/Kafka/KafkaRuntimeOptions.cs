using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ResearchTrack.BuildingBlocks.Kafka;

/// <summary>Validated transport settings only. Does not register a Kafka client or provision topics.</summary>
public sealed class KafkaRuntimeOptions
{
    // Assigned by each service's messaging registration, never from deployment input.
    public string Service { get; set; } = "";
    public const string DeliveredCaLocation = "/tmp/researchtrack-kafka/ca.crt";
    public bool Enabled { get; set; }
    public string BootstrapServers { get; set; } = "";
    public string SecurityProtocol { get; set; } = "SSL";
    public string SslCaLocation { get; set; } = DeliveredCaLocation;
    public string SslCaCertificateBase64 { get; set; } = "";
    public bool EnableSslCertificateVerification { get; set; } = true;
    public string SslEndpointIdentificationAlgorithm { get; set; } = "https";
    public string Topic { get; set; } = "";
    public string ContractVersion { get; set; } = "";
    public string ConsumerGroupId { get; set; } = "";

    public static KafkaRuntimeOptions Create(IConfiguration configuration, bool production, string service = "")
    {
        var section = configuration.GetSection("Kafka");
        // Disabled integrations must not bind or validate unused client/trust settings.
        if (!section.GetValue<bool>(nameof(Enabled))) return new();
        var options = section.Get<KafkaRuntimeOptions>() ?? new();
        options.Service = service;
        static void Require(bool condition, string key)
        {
            if (!condition) throw new InvalidOperationException($"Invalid or missing Kafka:{key} configuration.");
        }
        Require(options.SecurityProtocol == "SSL", nameof(SecurityProtocol));
        Require(options.EnableSslCertificateVerification, nameof(EnableSslCertificateVerification));
        Require(options.SslEndpointIdentificationAlgorithm == "https", nameof(SslEndpointIdentificationAlgorithm));
        Require(Regex.IsMatch(options.Topic, @"^researchtrack\.[a-z][a-z0-9-]*\.[a-z][a-z0-9-]*\.v[1-9][0-9]*$"), nameof(Topic));
        Require(!string.IsNullOrWhiteSpace(options.ContractVersion) &&
                !Regex.IsMatch(options.ContractVersion, "CHANGE_ME|[<>]", RegexOptions.IgnoreCase), nameof(ContractVersion));
        if (service is "github" or "jira")
        {
            Require(options.ContractVersion == "1", nameof(ContractVersion));
            Require(options.ConsumerGroupId == "researchtrack-" + service + "-webhook-v1", nameof(ConsumerGroupId));
        }
        Require(!string.IsNullOrWhiteSpace(options.BootstrapServers), nameof(BootstrapServers));
        foreach (var endpoint in options.BootstrapServers.Split(','))
        {
            var match = Regex.Match(endpoint, @"^([A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?):([0-9]{1,5})$");
            Require(match.Success && !match.Groups[1].Value.Contains("..", StringComparison.Ordinal) &&
                    int.TryParse(match.Groups[2].Value, out var port) && port is > 0 and <= 65535 && port != 29092,
                    nameof(BootstrapServers));
            if (production)
            {
                Require(match.Groups[2].Value == "9092" && IPAddress.TryParse(match.Groups[1].Value, out var ip) &&
                        ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && IsPrivate(ip), nameof(BootstrapServers));
            }
        }
        Require(Path.IsPathFullyQualified(options.SslCaLocation), nameof(SslCaLocation));
        Require(!production || (options.SslCaLocation == DeliveredCaLocation &&
                                !string.IsNullOrWhiteSpace(options.SslCaCertificateBase64)), nameof(SslCaCertificateBase64));
        try
        {
            var pem = string.IsNullOrEmpty(options.SslCaCertificateBase64)
                ? File.ReadAllText(options.SslCaLocation)
                : Encoding.ASCII.GetString(Convert.FromBase64String(options.SslCaCertificateBase64));
            Require(Regex.IsMatch(pem, @"\A\s*-----BEGIN CERTIFICATE-----[A-Za-z0-9+/=\r\n]+-----END CERTIFICATE-----\s*\z"), nameof(SslCaLocation));
            using var ca = X509Certificate2.CreateFromPem(pem);
            Require(ca.Extensions.OfType<X509BasicConstraintsExtension>().Any(extension => extension.CertificateAuthority)
                    && ca.NotBefore.ToUniversalTime() <= DateTime.UtcNow && ca.NotAfter.ToUniversalTime() > DateTime.UtcNow,
                    nameof(SslCaLocation));
            if (!string.IsNullOrEmpty(options.SslCaCertificateBase64))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(options.SslCaLocation)!);
                var temporary = options.SslCaLocation + "." + Guid.NewGuid().ToString("N");
                try
                {
                    if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                    {
                        using var file = new FileStream(temporary, new FileStreamOptions
                        {
                            Mode = FileMode.CreateNew, Access = FileAccess.Write,
                            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                        });
                        var bytes = Encoding.ASCII.GetBytes(pem);
                        file.Write(bytes);
                    }
                    else File.WriteAllText(temporary, pem);
                    File.Move(temporary, options.SslCaLocation, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            options.SslCaCertificateBase64 = ""; // Clients receive the verified file path, not delivery material.
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or CryptographicException)
        {
            throw new InvalidOperationException("Kafka CA trust delivery failed; check Kafka:SslCaLocation and Kafka:SslCaCertificateBase64.");
        }
        return options;
    }

    private static bool IsPrivate(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168);
    }
}

public static class KafkaConfigurationExtensions
{
    public static IServiceCollection AddResearchTrackKafkaConfiguration(
        this IServiceCollection services, IConfiguration configuration, bool production, string service = "")
    {
        services.AddSingleton(KafkaRuntimeOptions.Create(configuration, production, service));
        return services;
    }
}
