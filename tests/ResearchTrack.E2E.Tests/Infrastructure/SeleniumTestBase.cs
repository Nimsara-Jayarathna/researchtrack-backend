using OpenQA.Selenium;
using ResearchTrack.E2E.Tests.Pages;

namespace ResearchTrack.E2E.Tests.Infrastructure;

public abstract class SeleniumTestBase : IDisposable
{
    private bool _disposed;

    protected SeleniumTestBase()
    {
        Configuration = E2ETestConfiguration.Load();
        Driver = WebDriverFactory.Create(Configuration);
        Actions = new SeleniumActions(Driver, TimeSpan.FromSeconds(Configuration.TimeoutSeconds));
        Login = new LoginPage(Driver, Actions, Configuration);
        Project = new ProjectPage(Driver, Actions, Configuration);
        Jira = new JiraPage(Driver, Actions);
    }

    protected E2ETestConfiguration Configuration { get; }
    protected IWebDriver Driver { get; }
    protected SeleniumActions Actions { get; }
    protected LoginPage Login { get; }
    protected ProjectPage Project { get; }
    protected JiraPage Jira { get; }

    protected void RunWithArtifacts(string testName, Action test)
    {
        try
        {
            test();
        }
        catch (Exception exception)
        {
            ScreenshotHelper.Capture(Driver, testName, exception);
            throw;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || _disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            Driver.Quit();
        }
        finally
        {
            Driver.Dispose();
        }
    }
}
