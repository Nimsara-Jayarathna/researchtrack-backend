# ResearchTrack Full-System Selenium Suite

This suite was built against the uploaded `researchtrack-web-develop` and `researchtrack-backend-develop` projects. It targets the deployed ResearchTrack test environment by default and uses the actual React routes, IDs, button text, role guards, project tabs, registration flow, project wizard, lifecycle selector, account modal, meetings, files, GitHub/Jira tabs, and project/member APIs represented in the codebase.

## What is covered

- Public landing page, Login/Register entry points, Privacy/Terms/Support
- Guest protection for Student/Supervisor routes and legacy route redirects
- Login client validation, invalid credentials, role-based redirects, session refresh, logout
- Forgot-password and reset-link invalid states
- Registration email syntax, institutional-domain restriction, student identifier restriction, duplicate email, OTP invalid/valid progression
- Account profile modal and change-password validation
- Student project list/search, role authorization, project details, all 7 project tabs, mobile navigation
- Supervisor dashboard, project list/search/filter, role authorization, create-project wizard validation, all 8 project tabs, lifecycle states, team/milestones/files/meetings/integrations, mobile navigation
- Browser-console severe-error smoke checks and password masking
- Opt-in end-to-end CRUD: project creation, meeting channel validation/creation, file upload selection, lifecycle update + restore

## Safety model

The normal run is safe for a shared QA environment. Tests that create/update data are marked `destructive` and are skipped unless `RUN_DESTRUCTIVE=true`. OTP registration is also skipped unless `RUN_REGISTRATION_FLOW=true`.

## Setup (macOS / Linux)

```bash
python3 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
cp .env.selenium.example .env.selenium
# Fill test credentials in .env.selenium
./run_tests.sh
```

Selenium 4 uses Selenium Manager, so with Chrome installed you normally do not need to manually download ChromeDriver.

## Useful runs

```bash
# Public + negative tests only
pytest -v -m "public or registration or system" test_researchtrack_full_system.py

# Student regression
pytest -v -m student test_researchtrack_full_system.py

# Supervisor regression
pytest -v -m supervisor test_researchtrack_full_system.py

# Everything except mutating tests
pytest -v -m "not destructive" test_researchtrack_full_system.py --html=artifacts/report.html --self-contained-html

# Full regression including writes (use a disposable QA dataset)
RUN_DESTRUCTIVE=true pytest -v test_researchtrack_full_system.py --html=artifacts/full-report.html --self-contained-html
```

## Environment variables you should fill

`STUDENT_EMAIL`, `STUDENT_PASSWORD`, `SUPERVISOR_EMAIL`, and `SUPERVISOR_PASSWORD` unlock authenticated test coverage. `PROJECT_MEMBER_EMAIL` should be an existing registered student visible to the supervisor if you enable project creation. For OTP registration, use a dedicated disposable institutional email and set `RUN_REGISTRATION_FLOW=true`; populate `REGISTRATION_OTP` with the current received OTP when running the valid-OTP case.

## Evidence produced

- HTML result report via `pytest-html`
- Failure screenshots under `artifacts/screenshots/`
- Clear test IDs (`TC_AUTH_...`, `TC_REG_...`, `TC_STUDENT_...`, etc.) suitable for mapping into a QA report/Jira evidence table

## Important project observation

The uploaded frontend contains GitHub, Jira, meeting, and file UI flows. The uploaded backend currently has implemented controllers for Auth and Project services, while the other service projects are present but do not expose comparable controller implementations in this snapshot. The Selenium suite therefore validates those frontend integration surfaces where they are present and conditionally skips data-dependent operations when the deployed backend/test data cannot support them.
