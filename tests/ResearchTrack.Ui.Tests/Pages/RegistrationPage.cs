using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using ResearchTrack.Ui.Tests.Configuration;

namespace ResearchTrack.Ui.Tests.Pages;

public sealed class RegistrationPage
{
    private static readonly By Email = By.Id("registration-email");
    private static readonly By Continue = By.XPath("//button[normalize-space()='Continue']");
    private static readonly By OtpInputs = By.CssSelector("input[aria-label^='OTP digit ']");

    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;
    private readonly TestSettings _settings;

    public RegistrationPage(IWebDriver driver, TestSettings settings)
    {
        _driver = driver;
        _settings = settings;
        _wait = new WebDriverWait(driver, settings.WaitTimeout)
        {
            PollingInterval = TimeSpan.FromMilliseconds(100)
        };
        _wait.IgnoreExceptionTypes(typeof(NoSuchElementException), typeof(StaleElementReferenceException));
    }

    public IWebElement EmailInput => Visible(Email);
    public IWebElement ContinueButton => Visible(Continue);
    public IReadOnlyCollection<IWebElement> OtpDigitInputs => _driver.FindElements(OtpInputs);

    public void Open()
    {
        _driver.Navigate().GoToUrl(_settings.RegistrationUrl);
        _wait.Until(driver => driver.Url.Contains("/register", StringComparison.Ordinal));
        _ = EmailInput;
    }

    public void EnterEmail(string email)
    {
        var input = EmailInput;
        input.Clear();
        input.SendKeys(email);
    }

    public void ContinueRegistration() => ContinueButton.Click();

    public bool HasText(string text) =>
        _driver.FindElement(By.TagName("body")).Text.Contains(text, StringComparison.OrdinalIgnoreCase);

    public bool TryWaitForText(string text, TimeSpan? timeout = null)
    {
        try
        {
            var wait = timeout is null ? _wait : NewWait(timeout.Value);
            return wait.Until(_ => HasText(text));
        }
        catch (WebDriverTimeoutException)
        {
            return false;
        }
    }

    public void WaitForText(string text) =>
        _wait.Until(_ => HasText(text));

    public void WaitForOtpStep()
    {
        WaitForText("Verify your email");
        _wait.Until(driver =>
        {
            var inputs = driver.FindElements(OtpInputs);
            return inputs.Count == 6 && inputs.All(input => input.Displayed);
        });
    }

    public void EnterOtp(string otp)
    {
        if (otp.Length != 6 || otp.Any(character => !char.IsDigit(character)))
        {
            throw new ArgumentException("OTP must contain exactly six digits.", nameof(otp));
        }

        WaitForOtpStep();
        var inputs = OtpDigitInputs.ToArray();
        for (var index = 0; index < inputs.Length; index++)
        {
            inputs[index].SendKeys(otp[index].ToString());
        }
    }

    private IWebElement Visible(By selector) => _wait.Until(driver =>
    {
        var element = driver.FindElement(selector);
        return element.Displayed ? element : null;
    })!;

    private WebDriverWait NewWait(TimeSpan timeout)
    {
        var wait = new WebDriverWait(_driver, timeout)
        {
            PollingInterval = TimeSpan.FromMilliseconds(100)
        };
        wait.IgnoreExceptionTypes(typeof(NoSuchElementException), typeof(StaleElementReferenceException));
        return wait;
    }
}
