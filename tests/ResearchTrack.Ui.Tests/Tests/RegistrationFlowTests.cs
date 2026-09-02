using ResearchTrack.Ui.Tests.Utilities;

namespace ResearchTrack.Ui.Tests.Tests;

public sealed class RegistrationFlowTests : SeleniumTestBase
{
    [Fact(DisplayName = "TC-SEL-006 Email submission reaches the OTP screen")]
    public void Email_submission_reaches_otp_screen()
    {
        ExecuteWithFailureScreenshot("TC-SEL-006", "EmailToOtp", () =>
        {
            var email = RequireEmailFlowAddress();
            Registration.Open();
            Registration.EnterEmail(email);
            Assert.True(Registration.ContinueButton.Enabled);

            Registration.ContinueRegistration();
            Assert.True(
                Registration.TryWaitForText("Sending verification code", TimeSpan.FromSeconds(3)),
                "The registration loading state was not observed.");
            Registration.WaitForOtpStep();

            Assert.Equal(6, Registration.OtpDigitInputs.Count);
            Assert.True(Registration.HasText("Check your email"));
        });
    }

    [Fact(DisplayName = "TC-SEL-007 Invalid OTP is rejected")]
    public void Invalid_otp_is_rejected()
    {
        ExecuteWithFailureScreenshot("TC-SEL-007", "InvalidOtp", () =>
        {
            var email = RequireEmailFlowAddress();
            Registration.Open();
            Registration.EnterEmail(email);
            Registration.ContinueRegistration();
            Registration.WaitForOtpStep();

            Registration.EnterOtp("000000");
            Registration.WaitForText("Verification failed");
            Assert.True(Registration.HasText("Invalid or expired OTP."));
        });
    }

    [Fact(Skip = "BLOCKED — no test-safe OTP retrieval mechanism currently exists.",
        DisplayName = "TC-SEL-008 Full student registration")]
    public void Full_student_registration_requires_test_safe_otp_retrieval()
    {
    }

    private string RequireEmailFlowAddress()
    {
        if (Settings.EmailFlowEmail is null)
        {
            Assert.Skip(
                "BLOCKED — set RESEARCHTRACK_EMAIL_FLOW_EMAIL to an owned test inbox; the current backend uses real Brevo delivery.");
        }

        return Settings.EmailFlowEmail!;
    }
}
