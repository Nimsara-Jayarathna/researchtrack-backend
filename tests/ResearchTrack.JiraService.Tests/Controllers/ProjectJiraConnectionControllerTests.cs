using Microsoft.AspNetCore.Authorization;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.JiraService.Controllers;
using ResearchTrack.JiraService.Contracts;
using ResearchTrack.JiraService.Features;

namespace ResearchTrack.JiraService.Tests.Controllers;

public sealed class ProjectJiraConnectionControllerTests
{
    [Fact]
    public void Controller_RequiresSupervisorPolicyForConnectionManagement()
    {
        var attribute = Assert.Single(typeof(ProjectJiraConnectionController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal(AuthSecurityConstants.Policies.SupervisorOnly, attribute.Policy);
    }

    [Fact]
    public void ConnectionManagementRoutes_ArePresent()
    {
        var methods = typeof(ProjectJiraConnectionController).GetMethods()
            .Where(x => x.DeclaringType == typeof(ProjectJiraConnectionController))
            .Select(x => x.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(ProjectJiraConnectionController.AuthUrl), methods);
        Assert.Contains(nameof(ProjectJiraConnectionController.Boards), methods);
        Assert.Contains(nameof(ProjectJiraConnectionController.Link), methods);
        Assert.Contains(nameof(ProjectJiraConnectionController.Disconnect), methods);
        Assert.Contains(nameof(ProjectJiraConnectionController.Connection), methods);
    }
}
