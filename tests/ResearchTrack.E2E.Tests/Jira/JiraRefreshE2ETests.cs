using ResearchTrack.E2E.Tests.Infrastructure;

namespace ResearchTrack.E2E.Tests.Jira;

public sealed class JiraRefreshE2ETests : SeleniumTestBase
{
    [Fact]
    [Trait("Category", "E2E")]
    public void E2E002_RefreshShowsPreparedJiraUpdate()
    {
        RunWithArtifacts(nameof(E2E002_RefreshShowsPreparedJiraUpdate), () =>
        {
            Configuration.RequireRefreshExpectation();
            Login.LoginAsSupervisor();
            Project.OpenSupervisorProject();
            Project.OpenTab("jira");
            Jira.AssertConnectedDataVisible();
            Jira.RefreshAndWait();
            Jira.AssertIssueStatus(Configuration.ExpectedJiraIssueKey, Configuration.ExpectedJiraStatus);
        });
    }
}
