using OpenQA.Selenium;
using ResearchTrack.E2E.Tests.Infrastructure;

namespace ResearchTrack.E2E.Tests.Pages;

public sealed class JiraPage
{
    private static readonly By ProjectData = By.CssSelector("[data-testid='jira-project-data']");
    private static readonly By IssuesTab = By.CssSelector("[data-testid='jira-view-issues']");
    private static readonly By SprintTab = By.CssSelector("[data-testid='jira-view-sprint']");
    private static readonly By WorkloadTab = By.CssSelector("[data-testid='jira-view-workload']");
    private static readonly By IssuesView = By.CssSelector("[data-testid='jira-issues-view']");
    private static readonly By SprintView = By.CssSelector("[data-testid='jira-sprint-view']");
    private static readonly By WorkloadView = By.CssSelector("[data-testid='jira-workload-view']");
    private static readonly By FirstIssue = By.CssSelector("[data-testid='jira-issue-open']");
    private static readonly By IssueModal = By.CssSelector("[data-testid='jira-issue-modal']");
    private static readonly By IssueModalClose = By.CssSelector("[data-testid='jira-issue-modal-close']");
    private static readonly By Refresh = By.CssSelector("[data-testid='jira-refresh']");
    private static readonly By Search = By.CssSelector("[data-testid='jira-issue-search']");

    private readonly IWebDriver _driver;
    private readonly SeleniumActions _actions;

    internal JiraPage(IWebDriver driver, SeleniumActions actions)
    {
        _driver = driver;
        _actions = actions;
    }

    internal void AssertConnectedDataVisible() => _actions.Visible(ProjectData);

    internal void OpenIssues()
    {
        _actions.Click(IssuesTab);
        _actions.Visible(IssuesView);
    }

    internal void OpenFirstIssueAndVerifyModal()
    {
        _actions.Click(FirstIssue);
        _actions.Visible(IssueModal);
    }

    internal void CloseIssueModal()
    {
        _actions.Click(IssueModalClose);
        _actions.UntilNotVisible(IssueModal);
    }

    internal void OpenSprint()
    {
        _actions.Click(SprintTab);
        _actions.Visible(SprintView);
    }

    internal void OpenWorkload()
    {
        _actions.Click(WorkloadTab);
        _actions.Visible(WorkloadView);
    }

    internal void RefreshAndWait()
    {
        _actions.Click(Refresh);
        _actions.Until(
            driver =>
            {
                var button = driver.FindElement(Refresh);
                return button.Enabled && button.Text.Contains("Refresh Jira", StringComparison.OrdinalIgnoreCase);
            },
            "Jira refresh did not complete within the configured timeout.");
    }

    internal void AssertIssueStatus(string issueKey, string expectedStatus)
    {
        OpenIssues();
        _actions.Type(Search, issueKey);
        var status = By.CssSelector($"[data-testid='jira-issue-status-{CssEscape(issueKey)}']");
        _actions.UntilText(status, expectedStatus);
    }

    internal void AssertStudentReadOnlyControls()
    {
        AssertConnectedDataVisible();
        Assert.False(_actions.Exists(Refresh), "Student Jira view unexpectedly exposed the supervisor refresh control.");
        Assert.DoesNotContain(
            _driver.FindElements(By.TagName("button")),
            button => button.Displayed &&
                      (button.Text.Contains("Connect Jira", StringComparison.OrdinalIgnoreCase) ||
                       button.Text.Contains("Disconnect", StringComparison.OrdinalIgnoreCase)));
    }

    private static string CssEscape(string value) => value.Replace("'", "\\'", StringComparison.Ordinal);
}
