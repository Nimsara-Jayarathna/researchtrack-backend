using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace ResearchTrack.E2E.Tests.Infrastructure;

public sealed class SeleniumActions
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    internal SeleniumActions(IWebDriver driver, TimeSpan timeout)
    {
        _driver = driver;
        _wait = new WebDriverWait(driver, timeout)
        {
            PollingInterval = TimeSpan.FromMilliseconds(200),
        };
        _wait.IgnoreExceptionTypes(typeof(NoSuchElementException), typeof(StaleElementReferenceException));
    }

    internal IWebElement Visible(By by) => _wait.Until(driver =>
    {
        var element = driver.FindElement(by);
        return element.Displayed ? element : null;
    }) ?? throw new WebDriverTimeoutException($"Element never became visible: {by}");

    internal IWebElement Clickable(By by) => _wait.Until(driver =>
    {
        var element = driver.FindElement(by);
        return element.Displayed && element.Enabled ? element : null;
    }) ?? throw new WebDriverTimeoutException($"Element never became clickable: {by}");

    internal void Click(By by)
    {
        var element = Clickable(by);
        try
        {
            element.Click();
        }
        catch (ElementClickInterceptedException)
        {
            ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", element);
        }
    }

    internal void Type(By by, string value)
    {
        var element = Visible(by);
        element.Clear();
        element.SendKeys(value);
    }

    internal bool Exists(By by) => _driver.FindElements(by).Any(element =>
    {
        try
        {
            return element.Displayed;
        }
        catch (StaleElementReferenceException)
        {
            return false;
        }
    });

    internal void Until(Func<IWebDriver, bool> condition, string message)
    {
        try
        {
            _wait.Until(condition);
        }
        catch (WebDriverTimeoutException exception)
        {
            throw new WebDriverTimeoutException(message, exception);
        }
    }

    internal void UntilText(By by, string expectedText)
    {
        Until(
            driver =>
            {
                var element = driver.FindElement(by);
                return element.Displayed && element.Text.Contains(expectedText, StringComparison.OrdinalIgnoreCase);
            },
            $"Element {by} did not contain expected text '{expectedText}'.");
    }

    internal void UntilNotVisible(By by)
    {
        Until(
            driver => driver.FindElements(by).Count == 0 || driver.FindElements(by).All(element => !element.Displayed),
            $"Element remained visible: {by}");
    }
}
