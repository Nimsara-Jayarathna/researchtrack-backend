using ResearchTrack.Ui.Tests.Utilities;

namespace ResearchTrack.Ui.Tests.Tests;

public sealed class RegistrationSmokeTests : SeleniumTestBase
{
    [Fact(DisplayName = "TC-SEL-001 Registration page loads")]
    public void Registration_page_loads()
    {
        ExecuteWithFailureScreenshot("TC-SEL-001", "RegistrationPageLoads", () =>
        {
            Registration.Open();

            Assert.Contains("/register", Driver.Url, StringComparison.Ordinal);
            Assert.True(Registration.EmailInput.Displayed);
            Assert.True(Registration.ContinueButton.Displayed);
            Assert.True(Registration.HasText("Enter your email"));
        });
    }
}
