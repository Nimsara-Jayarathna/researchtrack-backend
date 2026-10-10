using Microsoft.Extensions.Configuration;
using ResearchTrack.AuthService.Configuration;

namespace ResearchTrack.AuthService.Tests.Configuration;

public sealed class PasswordPolicyOptionsTests
{
    [Fact]
    public void FromConfiguration_MapsValidPolicy()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["PasswordPolicy:MinimumLength"] = "12",
            ["PasswordPolicy:MaximumLength"] = "128",
            ["PasswordPolicy:RequireUppercase"] = "true",
            ["PasswordPolicy:RequireLowercase"] = "true",
            ["PasswordPolicy:RequireDigit"] = "true",
            ["PasswordPolicy:RequireSpecialCharacter"] = "false"
        });

        var options = PasswordPolicyOptions.FromConfiguration(configuration);

        Assert.Equal(12, options.MinimumLength);
        Assert.Equal(128, options.MaximumLength);
        Assert.True(options.RequireUppercase);
        Assert.True(options.RequireLowercase);
        Assert.True(options.RequireDigit);
        Assert.False(options.RequireSpecialCharacter);
    }

    [Theory]
    [InlineData("PasswordPolicy:MinimumLength", null)]
    [InlineData("PasswordPolicy:MinimumLength", "CHANGE_ME")]
    [InlineData("PasswordPolicy:MinimumLength", "0")]
    [InlineData("PasswordPolicy:MinimumLength", "1025")]
    [InlineData("PasswordPolicy:MinimumLength", "abc")]
    [InlineData("PasswordPolicy:MaximumLength", "0")]
    [InlineData("PasswordPolicy:MaximumLength", "4097")]
    [InlineData("PasswordPolicy:MaximumLength", "abc")]
    [InlineData("PasswordPolicy:RequireUppercase", "yes")]
    [InlineData("PasswordPolicy:RequireLowercase", "1")]
    [InlineData("PasswordPolicy:RequireDigit", "CHANGE_ME")]
    [InlineData("PasswordPolicy:RequireSpecialCharacter", "")]
    public void FromConfiguration_RejectsInvalidOrPlaceholderValues(string key, string? invalidValue)
    {
        var values = ValidValues();
        values[key] = invalidValue;

        Assert.Throws<InvalidOperationException>(() =>
            PasswordPolicyOptions.FromConfiguration(Build(values)));
    }

    private static Dictionary<string, string?> ValidValues() => new()
    {
        ["PasswordPolicy:MinimumLength"] = "12",
        ["PasswordPolicy:MaximumLength"] = "128",
        ["PasswordPolicy:RequireUppercase"] = "true",
        ["PasswordPolicy:RequireLowercase"] = "true",
        ["PasswordPolicy:RequireDigit"] = "true",
        ["PasswordPolicy:RequireSpecialCharacter"] = "true"
    };

    private static IConfiguration Build(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
