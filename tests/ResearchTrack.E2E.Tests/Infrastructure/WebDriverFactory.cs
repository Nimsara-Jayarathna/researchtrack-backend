using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace ResearchTrack.E2E.Tests.Infrastructure;

internal static class WebDriverFactory
{
    internal static IWebDriver Create(E2ETestConfiguration configuration)
    {
        if (!string.Equals(configuration.Browser, "chrome", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported E2E_BROWSER '{configuration.Browser}'. This suite currently supports 'chrome'.");
        }

        var options = new ChromeOptions
        {
            AcceptInsecureCertificates = configuration.AcceptInsecureCertificates,
        };

        options.AddArgument("--window-size=1440,1200");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--no-default-browser-check");
        options.AddArgument("--disable-notifications");
        if (configuration.Headless)
        {
            options.AddArgument("--headless=new");
        }

        var driver = new ChromeDriver(options);
        driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;
        driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(60);
        driver.Manage().Timeouts().AsynchronousJavaScript = TimeSpan.FromSeconds(30);
        return driver;
    }
}
