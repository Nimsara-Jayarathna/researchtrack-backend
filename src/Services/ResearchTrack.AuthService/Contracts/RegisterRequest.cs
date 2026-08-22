using System.Text.Json.Serialization;

namespace ResearchTrack.AuthService.Contracts;

public sealed class RegisterRequest
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? Password { get; init; }
    public string? RegistrationNumber { get; init; }

    // SuperviseSuite-compatible aliases. Canonical ResearchTrack fields above take precedence.
    [JsonPropertyName("fname")]
    public string? LegacyFirstName { get; init; }

    [JsonPropertyName("lname")]
    public string? LegacyLastName { get; init; }

    [JsonPropertyName("name")]
    public string? LegacyRegistrationNumber { get; init; }

    // Accepted only for backward-compatible payloads. The server never trusts it.
    public string? Role { get; init; }

    public string? EffectiveFirstName => FirstName ?? LegacyFirstName;
    public string? EffectiveLastName => LastName ?? LegacyLastName;
    public string? EffectiveRegistrationNumber => RegistrationNumber ?? LegacyRegistrationNumber;
}
