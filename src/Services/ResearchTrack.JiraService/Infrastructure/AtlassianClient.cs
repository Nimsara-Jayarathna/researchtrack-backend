using System.Net.Http.Headers; using System.Net.Http.Json; using System.Text.Json; using ResearchTrack.BuildingBlocks.Api.Constants; using ResearchTrack.BuildingBlocks.Api.Exceptions; using ResearchTrack.JiraService.Configuration; using ResearchTrack.JiraService.Contracts;
namespace ResearchTrack.JiraService.Infrastructure;
public sealed class AtlassianClient
{
    private readonly HttpClient _http; private readonly JiraOptions _options;
    public AtlassianClient(HttpClient http, JiraOptions options){_http=http;_options=options;}
    public async Task<TokenResult> ExchangeCodeAsync(string code,CancellationToken ct){ using var response=await _http.PostAsJsonAsync(_options.TokenUrl,new {grant_type="authorization_code",client_id=_options.ClientId,client_secret=_options.ClientSecret,code,redirect_uri=_options.RedirectUri},ct); if(!response.IsSuccessStatusCode) throw Failure("Atlassian authorization could not be completed."); using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); var r=doc.RootElement; var access=GetString(r,"access_token") ?? throw Failure("Atlassian returned no access token."); var refresh=GetString(r,"refresh_token"); var scope=GetString(r,"scope"); DateTimeOffset? expires=null; if(r.TryGetProperty("expires_in",out var e)&&e.TryGetInt32(out var seconds)) expires=DateTimeOffset.UtcNow.AddSeconds(seconds); return new(access,refresh,expires,scope); }
    public async Task<IReadOnlyList<JiraWorkspaceOption>> GetWorkspacesAsync(string token,CancellationToken ct){ using var req=Authorized(HttpMethod.Get,_options.AccessibleResourcesUrl,token); using var res=await _http.SendAsync(req,ct); if(!res.IsSuccessStatusCode) throw Failure("Unable to load Jira workspaces."); var items=JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct)).RootElement; var list=new List<JiraWorkspaceOption>(); foreach(var x in items.EnumerateArray()){var id=GetString(x,"id");var name=GetString(x,"name"); if(id is not null&&name is not null) list.Add(new(id,name,GetString(x,"url")));} if(list.Count==0) throw Failure("No accessible Jira workspace was returned."); return list; }
    public async Task<IReadOnlyList<JiraProjectOption>> GetProjectsAsync(string token,string cloudId,CancellationToken ct){ var list=new List<JiraProjectOption>(); var start=0; while(true){using var req=Authorized(HttpMethod.Get,$"{_options.ApiBaseUrl.TrimEnd('/')}/ex/jira/{Uri.EscapeDataString(cloudId)}/rest/api/3/project/search?startAt={start}&maxResults=50&orderBy=name",token); using var res=await _http.SendAsync(req,ct); if(!res.IsSuccessStatusCode) throw Failure("Unable to load accessible Jira projects."); using var doc=JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct)); var root=doc.RootElement; if(!root.TryGetProperty("values",out var values)) break; foreach(var x in values.EnumerateArray()){var id=GetString(x,"id");var key=GetString(x,"key");var name=GetString(x,"name");if(id is not null&&key is not null&&name is not null)list.Add(new(id,key,name));} var total=root.TryGetProperty("total",out var t)&&t.TryGetInt32(out var n)?n:list.Count; start+=values.GetArrayLength(); if(start>=total||values.GetArrayLength()==0)break;} return list; }
    public async Task<IReadOnlyList<JiraBoardOption>> GetBoardsAsync(string token,string cloudId,string projectKeyOrId,CancellationToken ct)
    {
        var list=new List<JiraBoardOption>();
        var start=0;
        while(true)
        {
            using var req=Authorized(HttpMethod.Get,$"{_options.ApiBaseUrl.TrimEnd('/')}/ex/jira/{Uri.EscapeDataString(cloudId)}/rest/agile/1.0/board?projectKeyOrId={Uri.EscapeDataString(projectKeyOrId)}&startAt={start}&maxResults=50",token);
            using var res=await _http.SendAsync(req,ct);
            if(!res.IsSuccessStatusCode)throw Failure("Unable to load Jira boards for the selected project.");
            using var doc=JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if(!doc.RootElement.TryGetProperty("values",out var values))break;
            foreach(var x in values.EnumerateArray())
                if(x.TryGetProperty("id",out var i)&&i.TryGetInt64(out var id))
                    list.Add(new(id,GetString(x,"name")??$"Board {id}",GetString(x,"type")??"unknown"));
            var count=values.GetArrayLength();
            var isLast=doc.RootElement.TryGetProperty("isLast",out var l)&&l.ValueKind==JsonValueKind.True;
            var total=doc.RootElement.TryGetProperty("total",out var t)&&t.TryGetInt32(out var n)?n:(int?)null;
            start+=count;
            if(isLast||count==0||(total.HasValue&&start>=total.Value))break;
        }
        return list;
    }

    public async Task<TokenResult> RefreshTokenAsync(string refreshToken,CancellationToken ct){using var response=await _http.PostAsJsonAsync(_options.TokenUrl,new {grant_type="refresh_token",client_id=_options.ClientId,client_secret=_options.ClientSecret,refresh_token=refreshToken},ct);if(!response.IsSuccessStatusCode)throw Failure("Jira authorization has expired. Reconnect Jira.");using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));var r=doc.RootElement;var access=GetString(r,"access_token")??throw Failure("Atlassian returned no access token.");var refresh=GetString(r,"refresh_token")??refreshToken;var scope=GetString(r,"scope");DateTimeOffset? expires=null;if(r.TryGetProperty("expires_in",out var e)&&e.TryGetInt32(out var seconds))expires=DateTimeOffset.UtcNow.AddSeconds(seconds);return new(access,refresh,expires,scope);}
    public async Task<IReadOnlyList<JiraFieldDefinition>> GetFieldsAsync(string token,string cloudId,CancellationToken ct){using var req=Authorized(HttpMethod.Get,$"{_options.ApiBaseUrl.TrimEnd('/')}/ex/jira/{Uri.EscapeDataString(cloudId)}/rest/api/3/field",token);using var res=await _http.SendAsync(req,ct);if(!res.IsSuccessStatusCode)throw Failure("Unable to discover Jira fields.");using var doc=JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));var list=new List<JiraFieldDefinition>();foreach(var x in doc.RootElement.EnumerateArray()){var id=GetString(x,"id");var name=GetString(x,"name");if(id is null||name is null)continue;string? custom=null;if(x.TryGetProperty("schema",out var schema))custom=GetString(schema,"custom");list.Add(new(id,name,custom));}return list;}
    public async Task<IReadOnlyList<JsonElement>> GetProjectIssuesAsync(string token,string cloudId,string projectKey,string? storyPointsFieldId,CancellationToken ct){var list=new List<JsonElement>();string? next=null;do{var fields=new List<string>{"summary","description","issuetype","status","priority","assignee","reporter","timetracking","parent","resolution","resolutiondate","duedate","created","updated"};if(!string.IsNullOrWhiteSpace(storyPointsFieldId))fields.Add(storyPointsFieldId);var url=$"{_options.ApiBaseUrl.TrimEnd('/')}/ex/jira/{Uri.EscapeDataString(cloudId)}/rest/api/3/search/jql?jql={Uri.EscapeDataString($"project = {projectKey} ORDER BY key ASC")}&maxResults=100&fields={Uri.EscapeDataString(string.Join(',',fields))}";if(!string.IsNullOrWhiteSpace(next))url+=$"&nextPageToken={Uri.EscapeDataString(next)}";using var req=Authorized(HttpMethod.Get,url,token);using var res=await _http.SendAsync(req,ct);if(!res.IsSuccessStatusCode)throw Failure("Unable to synchronize Jira issues.");using var doc=JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));if(doc.RootElement.TryGetProperty("issues",out var issues))foreach(var x in issues.EnumerateArray())list.Add(x.Clone());next=GetString(doc.RootElement,"nextPageToken");}while(!string.IsNullOrWhiteSpace(next));return list;}
    public async Task<IReadOnlyList<JsonElement>> GetSprintsAsync(string token,string cloudId,long boardId,CancellationToken ct){var list=new List<JsonElement>();var start=0;while(true){using var req=Authorized(HttpMethod.Get,$"{_options.ApiBaseUrl.TrimEnd('/')}/ex/jira/{Uri.EscapeDataString(cloudId)}/rest/agile/1.0/board/{boardId}/sprint?startAt={start}&maxResults=50",token);using var res=await _http.SendAsync(req,ct);if(!res.IsSuccessStatusCode)throw Failure("Unable to synchronize Jira sprints.");using var doc=JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));if(!doc.RootElement.TryGetProperty("values",out var values))break;foreach(var x in values.EnumerateArray())list.Add(x.Clone());var count=values.GetArrayLength();var isLast=doc.RootElement.TryGetProperty("isLast",out var l)&&l.ValueKind==JsonValueKind.True;start+=count;if(isLast||count==0)break;}return list;}
    // Sprint membership is resolved through Jira's normal issue-search API instead of
    // /rest/agile/1.0/sprint/{id}/issue. The latter requires additional Agile issue
    // permissions in some Atlassian installations even when board/sprint metadata is
    // readable. Search is already required by the issue mirror and gives us stable issue
    // ids without making the whole synchronization dependent on that extra endpoint.
    public async Task<IReadOnlyList<string>> GetSprintIssueIdsAsync(string token,string cloudId,long sprintId,CancellationToken ct)
    {
        var list=new List<string>();
        string? next=null;
        do
        {
            var jql=$"sprint = {sprintId} ORDER BY key ASC";
            var url=$"{_options.ApiBaseUrl.TrimEnd('/')}/ex/jira/{Uri.EscapeDataString(cloudId)}/rest/api/3/search/jql?jql={Uri.EscapeDataString(jql)}&maxResults=100&fields=id";
            if(!string.IsNullOrWhiteSpace(next))url+=$"&nextPageToken={Uri.EscapeDataString(next)}";
            using var req=Authorized(HttpMethod.Get,url,token);
            using var res=await _http.SendAsync(req,ct);
            if(!res.IsSuccessStatusCode)throw Failure("Unable to synchronize Jira sprint membership.");
            using var doc=JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if(doc.RootElement.TryGetProperty("issues",out var issues))
                foreach(var x in issues.EnumerateArray())
                {
                    var id=GetString(x,"id");
                    if(id is not null)list.Add(id);
                }
            next=GetString(doc.RootElement,"nextPageToken");
        }while(!string.IsNullOrWhiteSpace(next));
        return list;
    }
    public async Task<(long Id, DateTimeOffset? ExpiresAt)> RegisterWebhookAsync(string token,string cloudId,string projectKey,string callbackUrl,CancellationToken ct)
    {
        var url=$"{_options.ApiBaseUrl.TrimEnd('/')}/ex/jira/{Uri.EscapeDataString(cloudId)}/rest/api/3/webhook";
        var body=new { url=callbackUrl, webhooks=new[]{new { jqlFilter=$"project = {projectKey}", events=new[]{"jira:issue_created","jira:issue_updated","jira:issue_deleted"} }} };
        using var req=Authorized(HttpMethod.Post,url,token); req.Content=JsonContent.Create(body);
        using var res=await _http.SendAsync(req,ct); if(!res.IsSuccessStatusCode)throw Failure("Unable to register Jira webhook. Ensure manage:jira-webhook is authorized and the callback uses public HTTPS.");
        using var doc=JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        if(!doc.RootElement.TryGetProperty("webhookRegistrationResult",out var results)||results.GetArrayLength()==0)throw Failure("Jira did not return a webhook registration result.");
        var first=results[0]; if(first.TryGetProperty("errors",out var errors)&&errors.ValueKind==JsonValueKind.Array&&errors.GetArrayLength()>0)throw Failure("Jira rejected webhook registration.");
        if(!first.TryGetProperty("createdWebhookId",out var id)||!id.TryGetInt64(out var webhookId))throw Failure("Jira did not return a webhook id.");
        return (webhookId,DateTimeOffset.UtcNow.AddDays(30));
    }
    public async Task<DateTimeOffset?> RefreshWebhooksAsync(string token,string cloudId,IReadOnlyCollection<long> webhookIds,CancellationToken ct)
    {
        if(webhookIds.Count==0)return null;
        var url=$"{_options.ApiBaseUrl.TrimEnd('/')}/ex/jira/{Uri.EscapeDataString(cloudId)}/rest/api/3/webhook/refresh";
        using var req=Authorized(HttpMethod.Put,url,token); req.Content=JsonContent.Create(new { webhookIds });
        using var res=await _http.SendAsync(req,ct); if(!res.IsSuccessStatusCode)throw Failure("Unable to refresh Jira webhook registration.");
        using var doc=JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct)); var raw=GetString(doc.RootElement,"expirationDate");
        return DateTimeOffset.TryParse(raw,out var value)?value:DateTimeOffset.UtcNow.AddDays(30);
    }
    public async Task DeleteWebhooksAsync(string token,string cloudId,IReadOnlyCollection<long> webhookIds,CancellationToken ct)
    {
        if(webhookIds.Count==0)return;
        var url=$"{_options.ApiBaseUrl.TrimEnd('/')}/ex/jira/{Uri.EscapeDataString(cloudId)}/rest/api/3/webhook";
        using var req=Authorized(HttpMethod.Delete,url,token); req.Content=JsonContent.Create(new { webhookIds });
        using var res=await _http.SendAsync(req,ct); if(!res.IsSuccessStatusCode)throw Failure("Unable to remove Jira webhook registration.");
    }
    public sealed record JiraFieldDefinition(string Id,string Name,string? CustomSchema);
    private static HttpRequestMessage Authorized(HttpMethod method,string url,string token){var r=new HttpRequestMessage(method,url);r.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));return r;}
    private static string? GetString(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString():null;
    private static ApiException Failure(string m)=>new(400,ErrorCodes.ValidationError,m);
    public sealed record TokenResult(string AccessToken,string? RefreshToken,DateTimeOffset? ExpiresAt,string? Scope);
}
