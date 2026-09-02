using OpenQA.Selenium;
using ResearchTrack.Ui.Tests.Configuration;
using ResearchTrack.Ui.Tests.Drivers;
using ResearchTrack.Ui.Tests.Pages;
using Xunit.Sdk;

namespace ResearchTrack.Ui.Tests.Utilities;

public abstract class SeleniumTestBase : IDisposable
{
    private bool _disposed;

    protected SeleniumTestBase()
    {
        Settings = TestSettings.Load();
        Driver = WebDriverFactory.Create(Settings);
        Registration = new RegistrationPage(Driver, Settings);
    }

    protected TestSettings Settings { get; }
    protected IWebDriver Driver { get; }
    protected RegistrationPage Registration { get; }

    protected void ExecuteWithFailureScreenshot(string testId, string scenario, Action test)
    {
        try
        {
            test();
        }
        catch (SkipException)
        {
            throw;
        }
        catch
        {
            _ = ScreenshotHelper.Capture(Driver, testId, scenario);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            Driver.Quit();
        }
        finally
        {
            Driver.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
