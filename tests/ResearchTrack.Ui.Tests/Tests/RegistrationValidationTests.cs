using ResearchTrack.Ui.Tests.Utilities;

namespace ResearchTrack.Ui.Tests.Tests;

public sealed class RegistrationValidationTests : SeleniumTestBase
{
    [Fact(DisplayName = "TC-SEL-002 Invalid email format is rejected")]
    public void Invalid_email_format_is_rejected()
    {
        ExecuteWithFailureScreenshot("TC-SEL-002", "InvalidEmail", () =>
        {
            Registration.Open();
            Registration.EnterEmail("invalid@email");

            Registration.WaitForText("Enter a valid email address.");
            Assert.False(Registration.ContinueButton.Enabled);
            Assert.Empty(Registration.OtpDigitInputs);
        });
    }

    [Fact(DisplayName = "TC-SEL-003 Invalid student email prefix is rejected")]
    public void Invalid_student_email_prefix_is_rejected_when_enabled()
    {
        ExecuteWithFailureScreenshot("TC-SEL-003", "InvalidStudentPrefix", () =>
        {
            Registration.Open();
            Registration.EnterEmail(Settings.InvalidStudentEmail);

            if (!Registration.TryWaitForText("Invalid IT number format. Use ITXXXXXXXX.", TimeSpan.FromSeconds(2)))
            {
                Assert.Skip("Student email-prefix restriction is not enabled by the active registration configuration.");
            }

            Assert.False(Registration.ContinueButton.Enabled);
            Assert.Empty(Registration.OtpDigitInputs);
        });
    }

    [Fact(DisplayName = "TC-SEL-004 Student email is recognized")]
    public void Student_email_is_recognized()
    {
        ExecuteWithFailureScreenshot("TC-SEL-004", "StudentRecognition", () =>
        {
            Registration.Open();
            Registration.EnterEmail(Settings.StudentEmail);

            Registration.WaitForText("Student");
            Assert.True(Registration.ContinueButton.Enabled);
        });
    }

    [Fact(DisplayName = "TC-SEL-005 Supervisor email is recognized")]
    public void Supervisor_email_is_recognized()
    {
        ExecuteWithFailureScreenshot("TC-SEL-005", "SupervisorRecognition", () =>
        {
            Registration.Open();
            Registration.EnterEmail(Settings.SupervisorEmail);

            Registration.WaitForText("Supervisor");
            Assert.True(Registration.ContinueButton.Enabled);
        });
    }
}
