using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using ResearchTrack.Ui.Tests.Configuration;

namespace ResearchTrack.Ui.Tests.Drivers;

public static class WebDriverFactory
{
    public static IWebDriver Create(TestSettings settings)
    {
        var options = new ChromeOptions();
        if (settings.Headless)
        {
            options.AddArgument("--headless=new");
        }

        options.AddArgument("--window-size=1440,1000");
        options.AddArgument("--disable-search-engine-choice-screen");
        options.AddArgument("--no-default-browser-check");
        options.AddArgument("--disable-popup-blocking");

        // No driver path is supplied: Selenium Manager resolves a Chrome-compatible
        // driver through Selenium WebDriver itself.
        var driver = new ChromeDriver(options);
        driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;
        driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);
        driver.Manage().Window.Size = new System.Drawing.Size(1440, 1000);
        return driver;
    }
}
