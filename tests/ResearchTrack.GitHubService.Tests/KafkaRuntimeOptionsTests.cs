using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ResearchTrack.BuildingBlocks.Kafka;

namespace ResearchTrack.GitHubService.Tests;

public sealed class KafkaRuntimeOptionsTests
{
    [Fact]
    public void DisabledAndLegacyConfigurationDoNotRequireKafka()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddResearchTrackKafkaConfiguration(configuration, production: true);
        using var provider = services.BuildServiceProvider();
        Assert.False(provider.GetRequiredService<KafkaRuntimeOptions>().Enabled);
        var disabled = Config(new()
        {
            ["Kafka:Enabled"] = "false",
            ["Kafka:EnableSslCertificateVerification"] = "unused-invalid-value",
            ["Kafka:SslCaCertificateBase64"] = "unused-invalid-value"
        });
        Assert.False(KafkaRuntimeOptions.Create(disabled, production: true).Enabled);
    }

    [Theory]
    [InlineData("Kafka:BootstrapServers", "localhost:9092")]
    [InlineData("Kafka:BootstrapServers", "8.8.8.8:9092")]
    [InlineData("Kafka:BootstrapServers", "10.0.0.4:29092")]
    [InlineData("Kafka:SecurityProtocol", "PLAINTEXT")]
    [InlineData("Kafka:EnableSslCertificateVerification", "false")]
    [InlineData("Kafka:SslEndpointIdentificationAlgorithm", "none")]
    [InlineData("Kafka:Topic", "")]
    [InlineData("Kafka:ContractVersion", "CHANGE_ME")]
    public void ProductionRejectsUnsafeSettings(string key, string value)
    {
        var values = Settings(KafkaRuntimeOptions.DeliveredCaLocation);
        values[key] = value;
        Assert.Throws<InvalidOperationException>(() => KafkaRuntimeOptions.Create(Config(values), true));
    }

    [Fact]
    public void EnvironmentBindingDeliversValidatedPublicCaBeforeClientUse()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var values = Settings(Path.Combine(directory, "ca.crt"));
            var options = KafkaRuntimeOptions.Create(Config(values), false);
            Assert.True(options.Enabled);
            Assert.Equal("researchtrack.github.events.v1", options.Topic);
            Assert.Equal("1", options.ContractVersion);
            Assert.Equal("", options.SslCaCertificateBase64);
            using var certificate = X509Certificate2.CreateFromPem(File.ReadAllText(options.SslCaLocation));
            Assert.False(certificate.HasPrivateKey);
            // A service restart rewrites trust atomically without any topic or broker operation.
            Assert.True(KafkaRuntimeOptions.Create(Config(values), false).Enabled);
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("not-base64")]
    [InlineData("LS0tLS1CRUdJTiBQUklWQVRFIEtFWS0tLS0t")]
    public void InvalidTrustMaterialIsRejected(string encoded)
    {
        var values = Settings(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "ca.crt"));
        values["Kafka:SslCaCertificateBase64"] = encoded;
        Assert.Throws<InvalidOperationException>(() => KafkaRuntimeOptions.Create(Config(values), false));
        Assert.False(File.Exists(values["Kafka:SslCaLocation"]));
    }

    [Fact]
    public void MissingMountedCaFailsInsteadOfPretendingPathIsUsable()
    {
        var values = Settings(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.crt"));
        values["Kafka:SslCaCertificateBase64"] = "";
        Assert.Throws<InvalidOperationException>(() => KafkaRuntimeOptions.Create(Config(values), false));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, -2)]
    [InlineData(true, 1)]
    public void TrustRequiresCurrentlyValidCa(bool certificateAuthority, int dayOffset)
    {
        var values = Settings(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "ca.crt"), certificateAuthority, dayOffset);
        Assert.Throws<InvalidOperationException>(() => KafkaRuntimeOptions.Create(Config(values), false));
        Assert.False(File.Exists(values["Kafka:SslCaLocation"]));
    }

    [Fact]
    public void EnvironmentVariablesBindTheSameKeysUsedByDeployment()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var prefix = "KAFKA_TEST_" + Guid.NewGuid().ToString("N") + "_";
        var values = Settings(Path.Combine(directory, "ca.crt"));
        try
        {
            foreach (var pair in values) Environment.SetEnvironmentVariable(prefix + pair.Key.Replace(":", "__"), pair.Value);
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
            var options = KafkaRuntimeOptions.Create(configuration, false);
            Assert.True(options.Enabled);
            Assert.Equal("10.0.0.4:9092", options.BootstrapServers);
            Assert.True(File.Exists(options.SslCaLocation));
            values["Kafka:SslCaCertificateBase64"] = "";
            Assert.True(KafkaRuntimeOptions.Create(Config(values), false).Enabled); // Existing mounted public CA.
        }
        finally
        {
            foreach (var pair in values) Environment.SetEnvironmentVariable(prefix + pair.Key.Replace(":", "__"), null);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> Settings(string location, bool certificateAuthority = true, int dayOffset = 0)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Offline Kafka CA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority, false, 0, true));
        using var ca = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(dayOffset).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(dayOffset).AddHours(1));
        return new()
        {
            ["Kafka:Enabled"] = "true", ["Kafka:BootstrapServers"] = "10.0.0.4:9092",
            ["Kafka:Topic"] = "researchtrack.github.events.v1", ["Kafka:ContractVersion"] = "1",
            ["Kafka:SslCaLocation"] = location,
            ["Kafka:SslCaCertificateBase64"] = Convert.ToBase64String(Encoding.ASCII.GetBytes(ca.ExportCertificatePem()))
        };
    }
}
