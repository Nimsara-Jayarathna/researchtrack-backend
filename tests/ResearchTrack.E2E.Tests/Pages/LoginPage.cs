using OpenQA.Selenium;
using ResearchTrack.E2E.Tests.Infrastructure;

namespace ResearchTrack.E2E.Tests.Pages;

public sealed class LoginPage
{
    private static readonly By Email = By.Id("login-email");
    private static readonly By Password = By.Id("login-password");
    private static readonly By Submit = By.CssSelector("[data-testid='login-submit']");

    private readonly IWebDriver _driver;
    private readonly SeleniumActions _actions;
    private readonly E2ETestConfiguration _configuration;

    internal LoginPage(IWebDriver driver, SeleniumActions actions, E2ETestConfiguration configuration)
    {
        _driver = driver;
        _actions = actions;
        _configuration = configuration;
    }

    internal void LoginAsSupervisor() => Login(_configuration.SupervisorEmail, _configuration.SupervisorPassword, "/supervisor");

    internal void LoginAsStudent() => Login(_configuration.StudentEmail, _configuration.StudentPassword, "/student");

    private void Login(string email, string password, string expectedPath)
    {
        _driver.Navigate().GoToUrl($"{_configuration.BaseUrl}/login");
        _actions.Type(Email, email);
        _actions.Type(Password, password);
        _actions.Click(Submit);
        _actions.Until(
            driver => Uri.TryCreate(driver.Url, UriKind.Absolute, out var uri) && uri.AbsolutePath.StartsWith(expectedPath, StringComparison.OrdinalIgnoreCase),
            $"Login did not navigate to the expected '{expectedPath}' area. Current URL: {_driver.Url}");
    }
}
