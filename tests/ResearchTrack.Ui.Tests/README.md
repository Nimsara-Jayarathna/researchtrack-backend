# ResearchTrack Selenium UI tests

These xUnit tests exercise the existing React registration flow in Google Chrome. They do not start or modify the application, bypass OTP verification, or ship a ChromeDriver binary. Selenium Manager resolves the driver automatically.

## Prerequisites

- Google Chrome
- ResearchTrack frontend (default `http://localhost:5173`)
- ResearchTrack gateway (default `http://localhost:5000`)
- Auth service and its registration configuration

Run visible Chrome:

```bash
dotnet test tests/ResearchTrack.Ui.Tests
```

Run headless with URL overrides:

```bash
SELENIUM_HEADLESS=true \
RESEARCHTRACK_FRONTEND_URL=http://localhost:5173 \
RESEARCHTRACK_API_URL=http://localhost:5000 \
dotnet test tests/ResearchTrack.Ui.Tests
```

Optional test-data overrides are `RESEARCHTRACK_STUDENT_EMAIL`, `RESEARCHTRACK_INVALID_STUDENT_EMAIL`, and `RESEARCHTRACK_SUPERVISOR_EMAIL`. Tests that send an OTP require `RESEARCHTRACK_EMAIL_FLOW_EMAIL` to be an owned test inbox, preventing accidental email delivery during routine local runs.

Full registration remains skipped because the current application has no safe development/test OTP retrieval mechanism. Failure screenshots are written to the ignored `TestResults/SeleniumScreenshots` directory.
