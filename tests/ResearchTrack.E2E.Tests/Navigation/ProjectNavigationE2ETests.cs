using ResearchTrack.E2E.Tests.Infrastructure;

namespace ResearchTrack.E2E.Tests.Navigation;

public sealed class ProjectNavigationE2ETests : SeleniumTestBase
{
    [Fact]
    [Trait("Category", "E2E")]
    public void E2E004_ProjectNavigationRemainsStableAcrossCoreTabs()
    {
        RunWithArtifacts(nameof(E2E004_ProjectNavigationRemainsStableAcrossCoreTabs), () =>
        {
            Login.LoginAsSupervisor();
            Project.OpenSupervisorProject();

            foreach (var tab in new[] { "overview", "github", "jira", "integrations" })
            {
                Project.OpenTab(tab);
                Project.AssertProjectLoaded("supervisor");
            }
        });
    }
}
