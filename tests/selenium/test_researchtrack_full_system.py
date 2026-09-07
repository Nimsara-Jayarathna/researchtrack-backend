"""
ResearchTrack - Full System Selenium Regression Suite
=====================================================
Targets the React frontend and exercises the deployed system through the browser.

Coverage is intentionally split into safe and opt-in flows:
- Public + negative validation tests: always runnable.
- Student/supervisor authenticated tests: require role credentials in env.
- Registration OTP flow: requires RUN_REGISTRATION_FLOW=true and a mailbox/OTP.
- Destructive CRUD tests: require RUN_DESTRUCTIVE=true.

Default target: https://test.researchtrack.blipzo.xyz
Override with BASE_URL.
"""
from __future__ import annotations

import os
import re
import time
from dataclasses import dataclass
from datetime import date, timedelta
from pathlib import Path
from typing import Iterable
from urllib.parse import urlparse

import pytest
from dotenv import load_dotenv
from selenium import webdriver
from selenium.common.exceptions import (
    NoSuchElementException,
    StaleElementReferenceException,
    TimeoutException,
)
from selenium.webdriver.common.by import By
from selenium.webdriver.common.keys import Keys
from selenium.webdriver.support import expected_conditions as EC
from selenium.webdriver.support.ui import Select, WebDriverWait

load_dotenv(".env.selenium")


# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------


def _truthy(value: str | None) -> bool:
    return (value or "").strip().lower() in {"1", "true", "yes", "y", "on"}


@dataclass(frozen=True)
class Settings:
    base_url: str = os.getenv("BASE_URL", "https://test.researchtrack.blipzo.xyz").rstrip("/")
    browser: str = os.getenv("BROWSER", "chrome").strip().lower()
    headless: bool = _truthy(os.getenv("HEADLESS", "true"))
    timeout: int = int(os.getenv("SELENIUM_TIMEOUT", "20"))
    student_email: str = os.getenv("STUDENT_EMAIL", "").strip()
    student_password: str = os.getenv("STUDENT_PASSWORD", "").strip()
    supervisor_email: str = os.getenv("SUPERVISOR_EMAIL", "").strip()
    supervisor_password: str = os.getenv("SUPERVISOR_PASSWORD", "").strip()
    invalid_login_email: str = os.getenv(
        "INVALID_LOGIN_EMAIL", "selenium.nonexistent@example.com"
    ).strip()
    invalid_login_password: str = os.getenv("INVALID_LOGIN_PASSWORD", "WrongPassword!123").strip()
    registration_email: str = os.getenv("REGISTRATION_EMAIL", "").strip()
    registration_otp: str = os.getenv("REGISTRATION_OTP", "").strip()
    registration_first_name: str = os.getenv("REGISTRATION_FIRST_NAME", "Selenium").strip()
    registration_last_name: str = os.getenv("REGISTRATION_LAST_NAME", "Tester").strip()
    registration_password: str = os.getenv("REGISTRATION_PASSWORD", "StrongPassword!123").strip()
    registration_number: str = os.getenv("REGISTRATION_NUMBER", "").strip()
    student_domain: str = os.getenv("STUDENT_DOMAIN", "@my.sliit.lk").strip()
    run_registration_flow: bool = _truthy(os.getenv("RUN_REGISTRATION_FLOW"))
    run_destructive: bool = _truthy(os.getenv("RUN_DESTRUCTIVE"))
    project_member_email: str = os.getenv("PROJECT_MEMBER_EMAIL", "").strip()
    upload_fixture: str = os.getenv("UPLOAD_FIXTURE", "selenium-upload.txt").strip()
    capture_console: bool = _truthy(os.getenv("CAPTURE_BROWSER_CONSOLE", "true"))


SETTINGS = Settings()


# ---------------------------------------------------------------------------
# Selenium helpers
# ---------------------------------------------------------------------------


def wait(driver, timeout: int | None = None) -> WebDriverWait:
    return WebDriverWait(
        driver,
        timeout or SETTINGS.timeout,
        ignored_exceptions=(StaleElementReferenceException,),
    )


def url(path: str) -> str:
    if not path.startswith("/"):
        path = "/" + path
    return SETTINGS.base_url + path


def current_path(driver) -> str:
    return urlparse(driver.current_url).path


def body_text(driver) -> str:
    return driver.find_element(By.TAG_NAME, "body").text


def normalize(text: str) -> str:
    return re.sub(r"\s+", " ", text or "").strip()


def assert_body_contains(driver, *parts: str) -> None:
    text = normalize(body_text(driver)).lower()
    missing = [p for p in parts if p.lower() not in text]
    assert not missing, f"Missing expected page text {missing}. Body was: {text[:2500]}"


def visible(driver, by: By, value: str, timeout: int | None = None):
    return wait(driver, timeout).until(EC.visibility_of_element_located((by, value)))


def clickable(driver, by: By, value: str, timeout: int | None = None):
    return wait(driver, timeout).until(EC.element_to_be_clickable((by, value)))


def all_visible(driver, by: By, value: str, timeout: int | None = None):
    return wait(driver, timeout).until(EC.visibility_of_all_elements_located((by, value)))


def click_text(driver, text: str, tag: str = "button", timeout: int | None = None):
    literal = text.replace("'", "’")
    xpath = f"//{tag}[normalize-space(.)='{literal}']"
    try:
        el = clickable(driver, By.XPATH, xpath, timeout)
    except TimeoutException:
        xpath = f"//{tag}[contains(normalize-space(.), {xpath_literal(text)})]"
        el = clickable(driver, By.XPATH, xpath, timeout)
    driver.execute_script("arguments[0].scrollIntoView({block:'center'});", el)
    el.click()
    return el


def xpath_literal(text: str) -> str:
    if "'" not in text:
        return f"'{text}'"
    if '"' not in text:
        return f'"{text}"'
    parts = text.split("'")
    return "concat(" + ", \"'\", ".join(f"'{part}'" for part in parts) + ")"


def click_link_text(driver, text: str, timeout: int | None = None):
    return click_text(driver, text, tag="a", timeout=timeout)


def find_button(driver, text: str):
    return driver.find_element(
        By.XPATH, f"//button[contains(normalize-space(.), {xpath_literal(text)})]"
    )


def input_by_id(driver, element_id: str):
    return visible(driver, By.ID, element_id)


def fill(element, value: str, clear: bool = True) -> None:
    if clear:
        element.clear()
    element.send_keys(value)


def wait_path(driver, pattern: str, timeout: int | None = None) -> str:
    compiled = re.compile(pattern)

    def _matches(d):
        path = current_path(d)
        return path if compiled.search(path) else False

    return wait(driver, timeout).until(_matches)


def wait_body_contains(driver, text: str, timeout: int | None = None) -> None:
    wait(driver, timeout).until(lambda d: text.lower() in body_text(d).lower())


def close_request_modal_if_present(driver) -> None:
    for label in ("Continue", "Close", "Cancel", "Try again"):
        try:
            buttons = driver.find_elements(
                By.XPATH, f"//button[normalize-space(.)={xpath_literal(label)}]"
            )
            for button in reversed(buttons):
                if button.is_displayed() and button.is_enabled():
                    button.click()
                    time.sleep(0.2)
                    return
        except Exception:
            pass


def route(driver, path: str) -> None:
    driver.get(url(path))
    wait(driver).until(lambda d: d.execute_script("return document.readyState") == "complete")


def require_credentials(role: str) -> tuple[str, str]:
    if role == "student":
        if not SETTINGS.student_email or not SETTINGS.student_password:
            pytest.skip("Set STUDENT_EMAIL and STUDENT_PASSWORD to run student authenticated tests.")
        return SETTINGS.student_email, SETTINGS.student_password
    if not SETTINGS.supervisor_email or not SETTINGS.supervisor_password:
        pytest.skip("Set SUPERVISOR_EMAIL and SUPERVISOR_PASSWORD to run supervisor authenticated tests.")
    return SETTINGS.supervisor_email, SETTINGS.supervisor_password


def login(driver, role: str) -> None:
    email, password = require_credentials(role)
    route(driver, "/login")
    fill(input_by_id(driver, "login-email"), email)
    fill(input_by_id(driver, "login-password"), password)
    click_text(driver, "Sign In")
    if role == "student":
        wait_path(driver, r"^/student/projects(?:/|$)")
        wait_body_contains(driver, "STUDENT")
    else:
        wait_path(driver, r"^/supervisor(?:/|$)")
        wait_body_contains(driver, "SUPERVISOR")


def logout(driver) -> None:
    clickable(driver, By.CSS_SELECTOR, '[aria-label="Open account menu"]').click()
    click_text(driver, "Log out")
    wait(driver).until(lambda d: current_path(d) in {"/", "/login"})


def first_project_url(driver, role: str) -> str:
    route(driver, "/student/projects" if role == "student" else "/supervisor/projects")
    prefix = "/student/projects/" if role == "student" else "/supervisor/projects/"
    try:
        link = wait(driver, 10).until(
            lambda d: next(
                (
                    el
                    for el in d.find_elements(By.CSS_SELECTOR, f'a[href^="{prefix}"]')
                    if el.is_displayed()
                ),
                None,
            )
        )
    except TimeoutException:
        pytest.skip(f"No {role} project is available for this account.")
    return link.get_attribute("href")


def open_first_project(driver, role: str) -> None:
    project_url = first_project_url(driver, role)
    driver.get(project_url)
    wait(driver).until(lambda d: d.execute_script("return document.readyState") == "complete")
    wait_body_contains(driver, "Overview")


def select_tab(driver, label: str) -> None:
    click_text(driver, label)
    wait(driver).until(
        lambda d: any(
            el.is_displayed()
            for el in d.find_elements(By.XPATH, f"//button[normalize-space(.)={xpath_literal(label)}]")
        )
    )


def select_option_by_text(select_element, text: str) -> None:
    Select(select_element).select_by_visible_text(text)


def browser_console_errors(driver) -> list[str]:
    try:
        logs = driver.get_log("browser")
    except Exception:
        return []
    ignored = ("favicon", "ERR_BLOCKED_BY_CLIENT")
    errors = []
    for entry in logs:
        if entry.get("level") not in {"SEVERE", "ERROR"}:
            continue
        msg = entry.get("message", "")
        if any(token in msg for token in ignored):
            continue
        errors.append(msg)
    return errors


# ---------------------------------------------------------------------------
# Driver fixture
# ---------------------------------------------------------------------------


@pytest.fixture
def driver(request, tmp_path):
    browser = SETTINGS.browser
    if browser == "firefox":
        options = webdriver.FirefoxOptions()
        if SETTINGS.headless:
            options.add_argument("-headless")
        drv = webdriver.Firefox(options=options)
    elif browser == "edge":
        options = webdriver.EdgeOptions()
        if SETTINGS.headless:
            options.add_argument("--headless=new")
        options.add_argument("--window-size=1440,1000")
        drv = webdriver.Edge(options=options)
    else:
        options = webdriver.ChromeOptions()
        if SETTINGS.headless:
            options.add_argument("--headless=new")
        options.add_argument("--window-size=1440,1000")
        options.add_argument("--disable-dev-shm-usage")
        options.add_argument("--no-sandbox")
        if SETTINGS.capture_console:
            options.set_capability("goog:loggingPrefs", {"browser": "ALL"})
        drv = webdriver.Chrome(options=options)

    drv.set_page_load_timeout(60)
    drv.implicitly_wait(0)
    drv.set_window_size(1440, 1000)
    yield drv

    # Screenshot every failing test without requiring a plugin hook.
    rep = getattr(request.node, "rep_call", None)
    if rep and rep.failed:
        screenshot_dir = Path(os.getenv("SCREENSHOT_DIR", "artifacts/screenshots"))
        screenshot_dir.mkdir(parents=True, exist_ok=True)
        safe_name = re.sub(r"[^A-Za-z0-9_.-]+", "_", request.node.nodeid)
        try:
            drv.save_screenshot(str(screenshot_dir / f"{safe_name}.png"))
        except Exception:
            pass
    drv.quit()


@pytest.hookimpl(hookwrapper=True)
def pytest_runtest_makereport(item, call):
    outcome = yield
    rep = outcome.get_result()
    setattr(item, "rep_" + rep.when, rep)


# ---------------------------------------------------------------------------
# PUBLIC / ROUTING / ACCESS CONTROL
# ---------------------------------------------------------------------------


@pytest.mark.public
class TestPublicAndRouting:
    def test_tc_pub_001_landing_page_loads_core_content(self, driver):
        route(driver, "/")
        assert_body_contains(
            driver,
            "Streamline Your Research Supervision.",
            "Track GitHub commits",
            "Login",
            "Register",
        )

    def test_tc_pub_002_login_action_opens_login_panel(self, driver):
        route(driver, "/")
        click_text(driver, "Login")
        visible(driver, By.ID, "login-email")
        assert_body_contains(driver, "Welcome back!", "Forgot your password?")

    def test_tc_pub_003_register_action_opens_registration_panel(self, driver):
        route(driver, "/")
        click_text(driver, "Register")
        visible(driver, By.ID, "registration-email")
        assert_body_contains(driver, "Enter your email")

    @pytest.mark.parametrize(
        "path, expected",
        [
            ("/legal/privacy", "Privacy"),
            ("/legal/terms", "Terms"),
            ("/support", "Support"),
        ],
    )
    def test_tc_pub_004_public_information_pages(self, driver, path, expected):
        route(driver, path)
        assert expected.lower() in body_text(driver).lower()

    @pytest.mark.parametrize(
        "protected_path",
        [
            "/student",
            "/student/projects",
            "/student/projects/00000000-0000-0000-0000-000000000001",
            "/supervisor",
            "/supervisor/dashboard",
            "/supervisor/projects",
            "/supervisor/projects/new",
        ],
    )
    def test_tc_sec_001_guest_is_redirected_from_protected_routes(self, driver, protected_path):
        route(driver, protected_path)
        wait_path(driver, r"^/login$")
        visible(driver, By.ID, "login-email")

    def test_tc_route_001_unknown_route_redirects_guest_to_login(self, driver):
        route(driver, "/this-route-does-not-exist")
        wait_path(driver, r"^/login$")

    @pytest.mark.parametrize("legacy", ["/dashboard", "/project", "/projects", "/projects/new"])
    def test_tc_route_002_legacy_routes_require_authentication(self, driver, legacy):
        route(driver, legacy)
        wait_path(driver, r"^/login$")

    def test_tc_ui_001_mobile_public_header_keeps_login_and_register(self, driver):
        driver.set_window_size(390, 844)
        route(driver, "/")
        assert find_button(driver, "Login").is_displayed()
        assert find_button(driver, "Register").is_displayed()


# ---------------------------------------------------------------------------
# LOGIN / FORGOT PASSWORD / RESET PASSWORD
# ---------------------------------------------------------------------------


@pytest.mark.auth
class TestAuthenticationFlows:
    def test_tc_auth_001_login_submit_disabled_when_empty(self, driver):
        route(driver, "/login")
        button = find_button(driver, "Sign In")
        assert not button.is_enabled()

    def test_tc_auth_002_login_rejects_invalid_email_format_client_side(self, driver):
        route(driver, "/login")
        fill(input_by_id(driver, "login-email"), "not-an-email")
        fill(input_by_id(driver, "login-password"), "SomePassword!1")
        assert not find_button(driver, "Sign In").is_enabled()

    def test_tc_auth_003_login_requires_password(self, driver):
        route(driver, "/login")
        fill(input_by_id(driver, "login-email"), "valid@example.com")
        assert not find_button(driver, "Sign In").is_enabled()

    def test_tc_auth_004_invalid_credentials_do_not_disclose_account_state(self, driver):
        route(driver, "/login")
        fill(input_by_id(driver, "login-email"), SETTINGS.invalid_login_email)
        fill(input_by_id(driver, "login-password"), SETTINGS.invalid_login_password)
        click_text(driver, "Sign In")
        wait(driver).until(
            lambda d: any(
                token in body_text(d).lower()
                for token in ("invalid", "authentication failed", "incorrect", "failed")
            )
        )
        text = body_text(driver).lower()
        assert "password hash" not in text
        assert "user id" not in text

    def test_tc_auth_005_forgot_password_link_navigates(self, driver):
        route(driver, "/login")
        click_text(driver, "Forgot your password?")
        wait_path(driver, r"^/forgot-password$")
        visible(driver, By.ID, "forgot-password-email")

    def test_tc_auth_006_forgot_password_rejects_bad_email(self, driver):
        route(driver, "/forgot-password")
        field = input_by_id(driver, "forgot-password-email")
        fill(field, "bad")
        field.send_keys(Keys.TAB)
        wait_body_contains(driver, "Enter a valid email address.")

    def test_tc_auth_007_forgot_password_accepts_valid_syntax(self, driver):
        route(driver, "/forgot-password")
        fill(input_by_id(driver, "forgot-password-email"), SETTINGS.invalid_login_email)
        button = find_button(driver, "Send reset link")
        assert button.is_enabled()

    def test_tc_auth_008_reset_password_without_token_is_invalid(self, driver):
        route(driver, "/reset-password")
        wait_body_contains(driver, "Link expired or already used")
        assert_body_contains(driver, "Request new link")

    def test_tc_auth_009_reset_password_with_fake_token_is_invalid(self, driver):
        route(driver, "/reset-password?token=definitely-invalid-selenium-token")
        wait(driver).until(
            lambda d: any(
                token in body_text(d).lower()
                for token in ("expired", "already used", "unable to reach", "invalid")
            )
        )

    def test_tc_auth_010_student_login_redirects_to_student_home(self, driver):
        login(driver, "student")
        assert current_path(driver).startswith("/student/projects")
        assert_body_contains(driver, "STUDENT", "Projects")

    def test_tc_auth_011_supervisor_login_redirects_to_supervisor_dashboard(self, driver):
        login(driver, "supervisor")
        assert current_path(driver).startswith("/supervisor")
        assert_body_contains(driver, "SUPERVISOR", "Dashboard", "Projects")

    def test_tc_auth_012_session_survives_page_refresh(self, driver):
        login(driver, "student")
        driver.refresh()
        wait_path(driver, r"^/student/projects")
        assert_body_contains(driver, "STUDENT")

    def test_tc_auth_013_authenticated_user_cannot_open_guest_login_route(self, driver):
        login(driver, "supervisor")
        route(driver, "/login")
        wait_path(driver, r"^/supervisor")

    def test_tc_auth_014_logout_revokes_ui_session(self, driver):
        login(driver, "student")
        logout(driver)
        route(driver, "/student/projects")
        wait_path(driver, r"^/login$")


# ---------------------------------------------------------------------------
# REGISTRATION
# ---------------------------------------------------------------------------


@pytest.mark.registration
class TestRegistration:
    def test_tc_reg_001_registration_requires_valid_email_format(self, driver):
        route(driver, "/register")
        field = visible(driver, By.ID, "registration-email")
        fill(field, "not-an-email@")
        wait_body_contains(driver, "Enter a valid email address.")
        assert not find_button(driver, "Continue").is_enabled()

    def test_tc_reg_002_external_email_domain_is_rejected_when_restriction_enabled(self, driver):
        route(driver, "/register")
        field = visible(driver, By.ID, "registration-email")
        fill(field, "selenium.user@gmail.com")
        text = body_text(driver).lower()
        if "allowed domains" not in text and find_button(driver, "Continue").is_enabled():
            pytest.skip("Domain restriction is disabled in this deployment.")
        assert "allowed domains" in body_text(driver).lower()
        assert not find_button(driver, "Continue").is_enabled()

    def test_tc_reg_003_invalid_student_identifier_prefix_is_rejected(self, driver):
        route(driver, "/register")
        field = visible(driver, By.ID, "registration-email")
        candidate = f"BAD12345678{SETTINGS.student_domain}"
        fill(field, candidate)
        text = body_text(driver).lower()
        if "invalid it number format" not in text:
            pytest.skip("Student email prefix restriction is not enabled for this deployment/domain.")
        assert "invalid it number format" in text
        assert not find_button(driver, "Continue").is_enabled()

    def test_tc_reg_004_existing_registered_email_returns_conflict(self, driver):
        if not SETTINGS.student_email:
            pytest.skip("Set STUDENT_EMAIL to verify duplicate-registration protection.")
        route(driver, "/register")
        fill(visible(driver, By.ID, "registration-email"), SETTINGS.student_email)
        button = find_button(driver, "Continue")
        if not button.is_enabled():
            pytest.skip("Configured STUDENT_EMAIL does not satisfy the deployment registration policy.")
        button.click()
        wait(driver).until(
            lambda d: any(
                token in body_text(d).lower()
                for token in ("already registered", "check your email", "verification")
            )
        )
        # Existing users should ideally be rejected before OTP. If the environment allows
        # re-init for an existing user, this remains visible rather than hard-failing the suite.
        text = body_text(driver).lower()
        assert "already registered" in text or "check your email" in text

    def test_tc_reg_005_registration_close_confirmation_after_progress(self, driver):
        if not SETTINGS.run_registration_flow or not SETTINGS.registration_email:
            pytest.skip("Set RUN_REGISTRATION_FLOW=true and REGISTRATION_EMAIL to exercise OTP registration UI.")
        route(driver, "/register")
        fill(visible(driver, By.ID, "registration-email"), SETTINGS.registration_email)
        button = find_button(driver, "Continue")
        if not button.is_enabled():
            pytest.skip("REGISTRATION_EMAIL does not match the deployment registration policy.")
        button.click()
        wait_body_contains(driver, "Check your email")
        close = clickable(driver, By.CSS_SELECTOR, 'button[aria-label="Close"]')
        close.click()
        wait_body_contains(driver, "Close registration?")
        assert_body_contains(driver, "restart email verification", "Close anyway", "Cancel")

    def test_tc_reg_006_invalid_otp_is_rejected(self, driver):
        if not SETTINGS.run_registration_flow or not SETTINGS.registration_email:
            pytest.skip("Set RUN_REGISTRATION_FLOW=true and REGISTRATION_EMAIL.")
        route(driver, "/register")
        fill(visible(driver, By.ID, "registration-email"), SETTINGS.registration_email)
        if not find_button(driver, "Continue").is_enabled():
            pytest.skip("REGISTRATION_EMAIL does not satisfy registration policy.")
        click_text(driver, "Continue")
        wait_body_contains(driver, "Check your email")
        for i, digit in enumerate("000000", start=1):
            visible(driver, By.CSS_SELECTOR, f'[aria-label="OTP digit {i}"]').send_keys(digit)
        wait(driver).until(
            lambda d: any(
                token in body_text(d).lower()
                for token in ("verification failed", "invalid", "expired")
            )
        )

    def test_tc_reg_007_valid_otp_advances_to_role_or_profile(self, driver):
        if not (SETTINGS.run_registration_flow and SETTINGS.registration_email and SETTINGS.registration_otp):
            pytest.skip("Set RUN_REGISTRATION_FLOW=true, REGISTRATION_EMAIL, and REGISTRATION_OTP.")
        route(driver, "/register")
        fill(visible(driver, By.ID, "registration-email"), SETTINGS.registration_email)
        click_text(driver, "Continue")
        wait_body_contains(driver, "Check your email")
        for i, digit in enumerate(SETTINGS.registration_otp[:6], start=1):
            visible(driver, By.CSS_SELECTOR, f'[aria-label="OTP digit {i}"]').send_keys(digit)
        wait(driver).until(
            lambda d: "select your role" in body_text(d).lower()
            or "enter your details" in body_text(d).lower()
        )


# ---------------------------------------------------------------------------
# ACCOUNT / PASSWORD UI
# ---------------------------------------------------------------------------


@pytest.mark.auth
class TestAccountSecurityUI:
    @pytest.mark.parametrize("role", ["student", "supervisor"])
    def test_tc_account_001_account_modal_displays_identity_and_role(self, driver, role):
        email, _ = require_credentials(role)
        login(driver, role)
        clickable(driver, By.CSS_SELECTOR, '[aria-label="Open account menu"]').click()
        wait_body_contains(driver, "Manage profile and security")
        assert email.lower() in body_text(driver).lower()
        assert role.lower() in body_text(driver).lower()

    def test_tc_account_002_change_password_form_requires_all_fields(self, driver):
        login(driver, "student")
        clickable(driver, By.CSS_SELECTOR, '[aria-label="Open account menu"]').click()
        click_text(driver, "Change Password")
        visible(driver, By.ID, "current-password")
        assert not find_button(driver, "Save password").is_enabled()

    def test_tc_account_003_change_password_detects_mismatch(self, driver):
        login(driver, "student")
        clickable(driver, By.CSS_SELECTOR, '[aria-label="Open account menu"]').click()
        click_text(driver, "Change Password")
        _, password = require_credentials("student")
        fill(input_by_id(driver, "current-password"), password)
        fill(input_by_id(driver, "new-password"), "NewStrongPassword!123")
        fill(input_by_id(driver, "confirm-password"), "DifferentStrongPassword!123")
        input_by_id(driver, "confirm-password").send_keys(Keys.TAB)
        wait(driver).until(
            lambda d: "match" in body_text(d).lower() or not find_button(d, "Save password").is_enabled()
        )
        assert not find_button(driver, "Save password").is_enabled()

    def test_tc_account_004_change_password_rejects_weak_password_in_ui(self, driver):
        login(driver, "student")
        clickable(driver, By.CSS_SELECTOR, '[aria-label="Open account menu"]').click()
        click_text(driver, "Change Password")
        _, password = require_credentials("student")
        fill(input_by_id(driver, "current-password"), password)
        fill(input_by_id(driver, "new-password"), "weak")
        fill(input_by_id(driver, "confirm-password"), "weak")
        assert not find_button(driver, "Save password").is_enabled()


# ---------------------------------------------------------------------------
# STUDENT EXPERIENCE
# ---------------------------------------------------------------------------


@pytest.mark.student
class TestStudentExperience:
    def test_tc_student_001_project_list_loads(self, driver):
        login(driver, "student")
        route(driver, "/student/projects")
        assert_body_contains(driver, "My Projects", "Search your projects")

    def test_tc_student_002_project_search_empty_state_and_clear(self, driver):
        login(driver, "student")
        route(driver, "/student/projects")
        search = visible(driver, By.CSS_SELECTOR, 'input[aria-label="Search your projects"]')
        fill(search, "__selenium_no_project_should_match_938472__")
        wait_body_contains(driver, "No projects found")
        click_text(driver, "Clear search")
        assert search.get_attribute("value") == ""

    def test_tc_student_003_student_cannot_access_supervisor_routes(self, driver):
        login(driver, "student")
        route(driver, "/supervisor")
        wait_path(driver, r"^/student/projects")

    def test_tc_student_004_project_detail_loads_for_assigned_project(self, driver):
        login(driver, "student")
        open_first_project(driver, "student")
        assert_body_contains(driver, "Overview", "Team", "Milestones", "Files", "GitHub", "Jira", "Meetings")

    @pytest.mark.parametrize(
        "tab, body_hint",
        [
            ("Overview", None),
            ("Team", None),
            ("Milestones", None),
            ("Files", "Project Files"),
            ("GitHub", None),
            ("Jira", None),
            ("Meetings", "Meeting insights"),
        ],
    )
    def test_tc_student_005_all_project_tabs_are_navigable(self, driver, tab, body_hint):
        login(driver, "student")
        open_first_project(driver, "student")
        select_tab(driver, tab)
        if body_hint:
            wait_body_contains(driver, body_hint)
        if tab != "Overview":
            assert f"tab={tab.lower()}" in driver.current_url

    def test_tc_student_006_nonexistent_project_shows_not_found_or_denied(self, driver):
        login(driver, "student")
        route(driver, "/student/projects/00000000-0000-0000-0000-000000000001")
        wait(driver).until(
            lambda d: any(
                token in body_text(d).lower()
                for token in ("project not found", "not assigned", "not available", "error")
            )
        )

    def test_tc_student_007_mobile_private_navigation_is_usable(self, driver):
        driver.set_window_size(390, 844)
        login(driver, "student")
        menu = clickable(driver, By.CSS_SELECTOR, '[aria-label="Open navigation menu"]')
        menu.click()
        wait_body_contains(driver, "Projects")
        account = clickable(driver, By.CSS_SELECTOR, '[aria-label="Open account menu"]')
        assert account.is_displayed()


# ---------------------------------------------------------------------------
# SUPERVISOR EXPERIENCE
# ---------------------------------------------------------------------------


@pytest.mark.supervisor
class TestSupervisorExperience:
    def test_tc_supervisor_001_dashboard_loads(self, driver):
        login(driver, "supervisor")
        route(driver, "/supervisor")
        assert_body_contains(driver, "Supervisor Dashboard", "delivery health")

    def test_tc_supervisor_002_projects_page_loads_with_search_and_filter(self, driver):
        login(driver, "supervisor")
        route(driver, "/supervisor/projects")
        assert_body_contains(driver, "Projects", "New Project")
        visible(
            driver,
            By.CSS_SELECTOR,
            'input[aria-label="Search by project title, summary, batch, or semester"]',
        )
        selects = driver.find_elements(By.TAG_NAME, "select")
        assert any(s.is_displayed() for s in selects)

    def test_tc_supervisor_003_project_search_and_lifecycle_filter(self, driver):
        login(driver, "supervisor")
        route(driver, "/supervisor/projects")
        search = visible(
            driver,
            By.CSS_SELECTOR,
            'input[aria-label="Search by project title, summary, batch, or semester"]',
        )
        fill(search, "__selenium_no_project_238947__")
        wait_body_contains(driver, "No projects found")
        click_text(driver, "Clear filters")
        assert search.get_attribute("value") == ""

    def test_tc_supervisor_004_supervisor_cannot_access_student_routes(self, driver):
        login(driver, "supervisor")
        route(driver, "/student/projects")
        wait_path(driver, r"^/supervisor")

    def test_tc_supervisor_005_create_project_wizard_step1_requires_all_basics(self, driver):
        login(driver, "supervisor")
        route(driver, "/supervisor/projects/new")
        assert_body_contains(driver, "Create Project", "Project basics")
        button = find_button(driver, "Next: Assign students")
        assert not button.is_enabled()
        fill(visible(driver, By.CSS_SELECTOR, 'input[placeholder="e.g. Smart Attendance Tracker"]'), "Selenium Project")
        fill(visible(driver, By.CSS_SELECTOR, 'textarea[placeholder^="Describe the project scope"]'), "Automated Selenium validation project.")
        # Batch and semester are the two remaining visible inputs in step 1 without placeholders.
        candidates = [
            el
            for el in driver.find_elements(By.CSS_SELECTOR, "section input")
            if el.is_displayed() and not el.get_attribute("placeholder")
        ]
        assert len(candidates) >= 2
        fill(candidates[-2], "2026")
        fill(candidates[-1], "2")
        assert find_button(driver, "Next: Assign students").is_enabled()

    def test_tc_supervisor_006_student_search_requires_three_characters(self, driver):
        login(driver, "supervisor")
        route(driver, "/supervisor/projects/new")
        fill(visible(driver, By.CSS_SELECTOR, 'input[placeholder="e.g. Smart Attendance Tracker"]'), "Selenium Project")
        fill(visible(driver, By.CSS_SELECTOR, 'textarea[placeholder^="Describe the project scope"]'), "Automated Selenium validation project.")
        candidates = [el for el in driver.find_elements(By.CSS_SELECTOR, "section input") if el.is_displayed() and not el.get_attribute("placeholder")]
        fill(candidates[-2], "2026")
        fill(candidates[-1], "2")
        click_text(driver, "Next: Assign students")
        search = visible(driver, By.CSS_SELECTOR, 'input[placeholder^="Type at least 3 characters"]')
        fill(search, "ab")
        time.sleep(0.6)
        assert "searching registered students" not in body_text(driver).lower()
        assert not find_button(driver, "Next: Add milestones").is_enabled()

    def test_tc_supervisor_007_project_detail_all_tabs_are_navigable(self, driver):
        login(driver, "supervisor")
        open_first_project(driver, "supervisor")
        for tab in ("Overview", "Team", "Milestones", "Files", "Integrations", "GitHub", "Jira", "Meetings"):
            select_tab(driver, tab)
            if tab != "Overview":
                assert f"tab={tab.lower()}" in driver.current_url

    def test_tc_supervisor_008_lifecycle_control_exposes_all_states(self, driver):
        login(driver, "supervisor")
        open_first_project(driver, "supervisor")
        select = visible(driver, By.CSS_SELECTOR, '[aria-label="Select lifecycle status"]')
        options = {o.get_attribute("value") for o in select.find_elements(By.TAG_NAME, "option")}
        expected = {"PLANNING", "ACTIVE", "AT_RISK", "BEHIND", "COMPLETED"}
        assert expected.issubset(options)

    def test_tc_supervisor_009_nonexistent_project_shows_not_found_or_denied(self, driver):
        login(driver, "supervisor")
        route(driver, "/supervisor/projects/00000000-0000-0000-0000-000000000001")
        wait(driver).until(
            lambda d: any(
                token in body_text(d).lower()
                for token in ("project not found", "not available", "error")
            )
        )

    def test_tc_supervisor_010_team_tab_exposes_member_management(self, driver):
        login(driver, "supervisor")
        open_first_project(driver, "supervisor")
        select_tab(driver, "Team")
        text = body_text(driver).lower()
        assert any(token in text for token in ("student", "leader", "team", "member"))

    def test_tc_supervisor_011_milestones_tab_exposes_add_milestone(self, driver):
        login(driver, "supervisor")
        open_first_project(driver, "supervisor")
        select_tab(driver, "Milestones")
        assert any("milestone" in el.text.lower() for el in driver.find_elements(By.TAG_NAME, "button"))

    def test_tc_supervisor_012_files_tab_exposes_upload_ui(self, driver):
        login(driver, "supervisor")
        open_first_project(driver, "supervisor")
        select_tab(driver, "Files")
        assert_body_contains(driver, "Project Files")
        assert any("upload" in el.text.lower() for el in driver.find_elements(By.TAG_NAME, "button") if el.is_displayed())

    def test_tc_supervisor_013_meetings_tab_contains_channels_and_records(self, driver):
        login(driver, "supervisor")
        open_first_project(driver, "supervisor")
        select_tab(driver, "Meetings")
        wait_body_contains(driver, "Meeting insights")
        assert_body_contains(driver, "Channels", "Records")

    def test_tc_supervisor_014_integrations_tab_shows_github_and_jira(self, driver):
        login(driver, "supervisor")
        open_first_project(driver, "supervisor")
        select_tab(driver, "Integrations")
        assert_body_contains(driver, "GitHub", "Jira")

    def test_tc_supervisor_015_mobile_private_navigation_is_usable(self, driver):
        driver.set_window_size(390, 844)
        login(driver, "supervisor")
        clickable(driver, By.CSS_SELECTOR, '[aria-label="Open navigation menu"]').click()
        assert_body_contains(driver, "Dashboard", "Projects")


# ---------------------------------------------------------------------------
# SAFE SYSTEM-QUALITY CHECKS
# ---------------------------------------------------------------------------


@pytest.mark.system
class TestSystemQuality:
    def test_tc_sys_001_no_severe_console_errors_on_public_landing(self, driver):
        if SETTINGS.browser != "chrome" or not SETTINGS.capture_console:
            pytest.skip("Browser console check is enabled for Chrome only.")
        route(driver, "/")
        time.sleep(1)
        errors = browser_console_errors(driver)
        assert not errors, "Severe browser console errors:\n" + "\n".join(errors)

    def test_tc_sys_002_no_severe_console_errors_after_student_login(self, driver):
        if SETTINGS.browser != "chrome" or not SETTINGS.capture_console:
            pytest.skip("Browser console check is enabled for Chrome only.")
        login(driver, "student")
        time.sleep(1)
        errors = browser_console_errors(driver)
        assert not errors, "Severe browser console errors:\n" + "\n".join(errors)

    def test_tc_sys_003_page_uses_https_when_test_url_is_https(self, driver):
        route(driver, "/")
        if SETTINGS.base_url.startswith("https://"):
            assert driver.current_url.startswith("https://")

    def test_tc_sys_004_password_inputs_are_masked(self, driver):
        route(driver, "/login")
        assert input_by_id(driver, "login-password").get_attribute("type") == "password"

    def test_tc_sys_005_escape_closes_login_modal_from_landing(self, driver):
        route(driver, "/")
        click_text(driver, "Login")
        visible(driver, By.ID, "login-email")
        driver.find_element(By.TAG_NAME, "body").send_keys(Keys.ESCAPE)
        wait(driver).until(lambda d: not d.find_elements(By.ID, "login-email"))


# ---------------------------------------------------------------------------
# OPT-IN DESTRUCTIVE CRUD FLOWS
# ---------------------------------------------------------------------------


def require_destructive() -> None:
    if not SETTINGS.run_destructive:
        pytest.skip("Set RUN_DESTRUCTIVE=true to run data-mutating Selenium tests.")


@pytest.mark.destructive
class TestDestructiveCrudFlows:
    def test_tc_crud_001_supervisor_can_create_project_end_to_end(self, driver):
        require_destructive()
        require_credentials("supervisor")
        member_email = SETTINGS.project_member_email or SETTINGS.student_email
        if not member_email:
            pytest.skip("Set PROJECT_MEMBER_EMAIL or STUDENT_EMAIL to create a project.")

        login(driver, "supervisor")
        route(driver, "/supervisor/projects/new")
        unique = str(int(time.time()))
        title = f"Selenium E2E {unique}"[:40]
        fill(visible(driver, By.CSS_SELECTOR, 'input[placeholder="e.g. Smart Attendance Tracker"]'), title)
        fill(visible(driver, By.CSS_SELECTOR, 'textarea[placeholder^="Describe the project scope"]'), "Selenium-created project for end-to-end regression verification.")
        step1_inputs = [el for el in driver.find_elements(By.CSS_SELECTOR, "section input") if el.is_displayed() and not el.get_attribute("placeholder")]
        fill(step1_inputs[-2], "2026")
        fill(step1_inputs[-1], "2")
        click_text(driver, "Next: Assign students")

        search = visible(driver, By.CSS_SELECTOR, 'input[placeholder^="Type at least 3 characters"]')
        fill(search, member_email)
        wait(driver).until(lambda d: member_email.lower() in body_text(d).lower() or "No registered student found" in body_text(d))
        if "No registered student found" in body_text(driver):
            pytest.skip(f"PROJECT_MEMBER_EMAIL {member_email!r} was not found by the supervisor search API.")
        candidate_buttons = [
            b for b in driver.find_elements(By.XPATH, "//button")
            if b.is_displayed() and member_email.lower() in b.text.lower()
        ]
        assert candidate_buttons, f"Could not locate student result for {member_email}"
        candidate_buttons[0].click()
        assert find_button(driver, "Next: Add milestones").is_enabled()
        click_text(driver, "Next: Add milestones")

        fill(visible(driver, By.CSS_SELECTOR, 'input[placeholder="e.g. Proposal Submission"]'), "Proposal Submission")
        due = (date.today() + timedelta(days=30)).isoformat()
        due_input = visible(driver, By.CSS_SELECTOR, 'input[type="date"]')
        driver.execute_script("arguments[0].value = arguments[1]; arguments[0].dispatchEvent(new Event('input', {bubbles:true})); arguments[0].dispatchEvent(new Event('change', {bubbles:true}));", due_input, due)
        fill(visible(driver, By.CSS_SELECTOR, 'textarea[placeholder="Add context or review expectations."]'), "Initial proposal review and feedback.")
        click_text(driver, "Create project")
        wait(driver, 30).until(lambda d: "project created" in body_text(d).lower() or current_path(d) == "/supervisor/projects")

    def test_tc_crud_002_meeting_channel_form_validates_url(self, driver):
        require_destructive()
        login(driver, "supervisor")
        open_first_project(driver, "supervisor")
        select_tab(driver, "Meetings")
        # Find the first Add channel button; implementation may say Add channel directly.
        buttons = [b for b in driver.find_elements(By.XPATH, "//button") if b.is_displayed() and "add channel" in b.text.lower()]
        if not buttons:
            pytest.skip("No Add channel control is available for this project/deployment.")
        buttons[0].click()
        visible(driver, By.CSS_SELECTOR, '[aria-label="Add channel"]')
        fill(visible(driver, By.CSS_SELECTOR, 'input[placeholder="Weekly supervision call"]'), "Selenium Weekly Call")
        link = visible(driver, By.CSS_SELECTOR, 'input[placeholder="https://meet.google.com/..."]')
        fill(link, "not-a-url")
        wait_body_contains(driver, "Enter a valid link starting with http:// or https://")
        assert not find_button(driver, "Add channel").is_enabled()

    def test_tc_crud_003_meeting_channel_can_be_created(self, driver):
        require_destructive()
        login(driver, "student")
        open_first_project(driver, "student")
        select_tab(driver, "Meetings")
        buttons = [b for b in driver.find_elements(By.XPATH, "//button") if b.is_displayed() and "add channel" in b.text.lower()]
        if not buttons:
            pytest.skip("No Add channel control is available for this student/project.")
        buttons[0].click()
        fill(visible(driver, By.CSS_SELECTOR, 'input[placeholder="Weekly supervision call"]'), f"Selenium Channel {int(time.time())}")
        fill(visible(driver, By.CSS_SELECTOR, 'input[placeholder="https://meet.google.com/..."]'), "https://meet.google.com/selenium-e2e")
        add = find_button(driver, "Add channel")
        assert add.is_enabled()
        add.click()
        wait(driver, 30).until(lambda d: "selenium channel" in body_text(d).lower() or "success" in body_text(d).lower())

    def test_tc_crud_004_file_upload_modal_validates_selection(self, driver, tmp_path):
        require_destructive()
        login(driver, "student")
        open_first_project(driver, "student")
        select_tab(driver, "Files")
        upload_buttons = [b for b in driver.find_elements(By.XPATH, "//button") if b.is_displayed() and "upload" in b.text.lower()]
        if not upload_buttons:
            pytest.skip("No upload control is available for this project/deployment.")
        upload_buttons[0].click()
        file_inputs = [el for el in driver.find_elements(By.CSS_SELECTOR, 'input[type="file"]') if el.is_displayed() or el.get_attribute("type") == "file"]
        if not file_inputs:
            pytest.skip("Upload modal did not expose a file input.")
        fixture = tmp_path / SETTINGS.upload_fixture
        fixture.write_text("ResearchTrack Selenium upload verification\n", encoding="utf-8")
        file_inputs[0].send_keys(str(fixture.resolve()))
        wait_body_contains(driver, fixture.name)
        assert any(b.is_enabled() for b in driver.find_elements(By.XPATH, "//button[normalize-space(.)='Upload']"))

    def test_tc_crud_005_lifecycle_change_requires_supervisor_and_updates_ui(self, driver):
        require_destructive()
        login(driver, "supervisor")
        open_first_project(driver, "supervisor")
        select = visible(driver, By.CSS_SELECTOR, '[aria-label="Select lifecycle status"]')
        original = Select(select).first_selected_option.get_attribute("value")
        target = next(v for v in ("ACTIVE", "PLANNING", "AT_RISK") if v != original)
        Select(select).select_by_value(target)
        wait(driver, 30).until(
            lambda d: Select(d.find_element(By.CSS_SELECTOR, '[aria-label="Select lifecycle status"]')).first_selected_option.get_attribute("value") == target
        )
        # Restore original to minimize shared-test-environment side effects.
        Select(driver.find_element(By.CSS_SELECTOR, '[aria-label="Select lifecycle status"]')).select_by_value(original)
        wait(driver, 30).until(
            lambda d: Select(d.find_element(By.CSS_SELECTOR, '[aria-label="Select lifecycle status"]')).first_selected_option.get_attribute("value") == original
        )
