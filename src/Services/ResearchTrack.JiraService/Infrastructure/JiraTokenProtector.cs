using Microsoft.AspNetCore.DataProtection;
namespace ResearchTrack.JiraService.Infrastructure;
public sealed class JiraTokenProtector : IJiraTokenProtector
{
    private readonly IDataProtector _protector;
    public JiraTokenProtector(IDataProtectionProvider provider) => _protector=provider.CreateProtector("ResearchTrack.JiraService.OAuthTokens.v1");
    public string Protect(string value)=>_protector.Protect(value);
    public string Unprotect(string value)=>_protector.Unprotect(value);
}
