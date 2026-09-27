using OpenQA.Selenium;
using ResearchTrack.E2E.Tests.Infrastructure;

namespace ResearchTrack.E2E.Tests.Pages;

public sealed class ProjectPage
{
    private static readonly By Root = By.CssSelector("[data-testid='project-details-root']");

    private readonly IWebDriver _driver;
    private readonly SeleniumActions _actions;
    private readonly E2ETestConfiguration _configuration;

    internal ProjectPage(IWebDriver driver, SeleniumActions actions, E2ETestConfiguration configuration)
    {
        _driver = driver;
        _actions = actions;
        _configuration = configuration;
    }

    internal void OpenSupervisorProject()
    {
        _driver.Navigate().GoToUrl(_configuration.SupervisorProjectUrl);
        AssertProjectLoaded("supervisor");
    }

    internal void OpenStudentProject()
    {
        _driver.Navigate().GoToUrl(_configuration.StudentProjectUrl);
        AssertProjectLoaded("student");
    }

    internal void OpenTab(string value)
    {
        var selector = By.CssSelector($"[data-testid='project-tab-{value}']");
        _actions.Click(selector);
        _actions.Until(
            driver => string.Equals(driver.FindElement(selector).GetAttribute("aria-selected"), "true", StringComparison.OrdinalIgnoreCase),
            $"Project tab '{value}' did not become active.");
    }

    internal void AssertProjectLoaded(string expectedRole)
    {
        var root = _actions.Visible(Root);
        Assert.Equal(_configuration.ProjectId, root.GetAttribute("data-project-id"));
        Assert.Equal(expectedRole, root.GetAttribute("data-project-role"));
    }


}
