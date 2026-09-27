using ResearchTrack.E2E.Tests.Infrastructure;

namespace ResearchTrack.E2E.Tests.Jira;

public sealed class JiraNavigationE2ETests : SeleniumTestBase
{
    [Fact]
    [Trait("Category", "E2E")]
    public void E2E001_NavigateIssuesDetailsSprintAndWorkload()
    {
        RunWithArtifacts(nameof(E2E001_NavigateIssuesDetailsSprintAndWorkload), () =>
        {
            Login.LoginAsSupervisor();
            Project.OpenSupervisorProject();
            Project.OpenTab("jira");
            Jira.AssertConnectedDataVisible();
            Jira.OpenIssues();
            Jira.OpenFirstIssueAndVerifyModal();
            Jira.CloseIssueModal();
            Jira.OpenSprint();
            Jira.OpenWorkload();
        });
    }
}
