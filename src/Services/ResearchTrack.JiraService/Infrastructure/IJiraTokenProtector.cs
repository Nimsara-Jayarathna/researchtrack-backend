namespace ResearchTrack.JiraService.Infrastructure;
public interface IJiraTokenProtector { string Protect(string value); string Unprotect(string value); }
