using ResearchTrack.E2E.Tests.Infrastructure;

namespace ResearchTrack.E2E.Tests.Jira;

public sealed class JiraAuthorizationE2ETests : SeleniumTestBase
{
    [Fact]
    [Trait("Category", "E2E")]
    public void E2E003_StudentHasReadOnlyJiraExperience()
    {
        RunWithArtifacts(nameof(E2E003_StudentHasReadOnlyJiraExperience), () =>
        {
            Login.LoginAsStudent();
            Project.OpenStudentProject();
            Project.OpenTab("jira");
            Jira.AssertStudentReadOnlyControls();
        });
    }
}
