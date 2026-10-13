#!/usr/bin/env python3
"""Fail-closed GitHub evidence checks for release CI reuse and Sonar gate.

Only the trusted main commit is eligible. Online failures mean execute CI or
reject the release, never accept a stale/untrusted run. No PR artifacts, mutable
status names or user-controlled workflow URLs are trusted.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

REPO = "Nimsara-Jayarathna/researchtrack-backend"
SHA = re.compile(r"[0-9a-f]{40}\Z")
API = "https://api.github.com/repos/" + REPO


class EvidenceError(Exception):
    pass


def get(path: str, token: str) -> dict | list:
    if not path.startswith("/") or "//" in path or ".." in path:
        raise EvidenceError("Invalid GitHub endpoint")
    url = API + path
    request = urllib.request.Request(url, headers={
        "Authorization": "Bearer " + token,
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "researchtrack-release-evidence",
    })
    with urllib.request.urlopen(request, timeout=20) as result:
        return json.load(result)


def trusted_context(env: dict, fetch=get) -> tuple[str, str]:
    sha = env.get("SOURCE_SHA", "")
    if (env.get("SOURCE_REPOSITORY") != REPO or
            env.get("SOURCE_REF") != "refs/heads/main" or
            env.get("SOURCE_EVENT") not in ("push", "workflow_dispatch") or
            not SHA.fullmatch(sha)):
        raise EvidenceError("Untrusted main-branch source")
    token = env.get("GH_TOKEN", "")
    if not token:
        raise EvidenceError("Missing read-only GitHub token")
    branch = fetch("/branches/main", token)
    if branch.get("commit", {}).get("sha") != sha:
        raise EvidenceError("Release SHA does not equal current main head")
    return sha, token


def tree_for(sha: str, token: str, fetch=get) -> str:
    if not SHA.fullmatch(sha):
        raise EvidenceError("Invalid commit SHA")
    commit = fetch("/git/commits/" + sha, token)
    tree = commit.get("tree", {}).get("sha", "")
    if not SHA.fullmatch(tree):
        raise EvidenceError("Missing immutable Git tree")
    return tree


def workflow_runs(name: str, token: str, fetch=get, **filters) -> list[dict]:
    if name not in ("backend-ci.yml", "backend-code-quality.yml", "backend-sonarqube.yml"):
        raise EvidenceError("Unrecognized workflow")
    query = urllib.parse.urlencode(dict(per_page=100, **filters))
    response = fetch("/actions/workflows/" + name + "/runs?" + query, token)
    return response.get("workflow_runs", [])


def successful_jobs(run: dict, required: tuple[str, ...], token: str, fetch=get) -> bool:
    run_id = run.get("id")
    if not isinstance(run_id, int) or run_id <= 0:
        return False
    response = fetch(f"/actions/runs/{run_id}/jobs?per_page=100&filter=latest", token)
    if response.get("total_count", 101) > 100:
        return False  # Do not trust incomplete paging.
    jobs = response.get("jobs", [])
    for needle in required:
        if not any(job.get("conclusion") == "success" and
                   (job.get("name", "") == needle or
                    job.get("name", "").endswith(" / " + needle))
                   for job in jobs):
            return False
    return True


def validated_check_suite_tree(run: dict, expected_tree: str, token: str, fetch=get) -> bool:
    """GitHub's immutable check-suite commit must match released source.

    PR Actions often check out a synthetic test-merge revision, not the PR
    head. The associated *check suite* supplies the actually checked commit.
    Do not trust only the PR-head tree if it differs from the test-merge tree.
    """
    suite = run.get("check_suite_id")
    if not isinstance(suite, int) or suite <= 0:
        return False
    detail = fetch(f"/check-suites/{suite}", token)
    actual = detail.get("head_sha", "")
    return SHA.fullmatch(actual) is not None and tree_for(actual, token, fetch) == expected_tree


def matching_runs(name: str, sha: str, token: str, fetch=get) -> list[dict]:
    return [run for run in workflow_runs(name, token, fetch, head_sha=sha)
            if run.get("head_sha") == sha and
            run.get("head_repository", {}).get("full_name") == REPO and
            run.get("status") == "completed" and run.get("conclusion") == "success"]


def ci_reusable_from_pr(sha: str, token: str, fetch=get) -> tuple[bool, str]:
    """Require matching immutable trees of BOTH PR checks; otherwise run CI.

    Strict head-revision tree equality deliberately rejects stale PR merge
    results and untrusted forks. This optimizes only content-identical merges.
    """
    main_tree = tree_for(sha, token, fetch)
    pulls = fetch("/commits/" + sha + "/pulls?per_page=100", token)
    if not isinstance(pulls, list) or len(pulls) >= 100:
        return False, "No unambiguous associated merged PR"
    for pr in pulls:
        if (not pr.get("merged_at") or pr.get("base", {}).get("ref") != "main" or
                pr.get("base", {}).get("repo", {}).get("full_name") != REPO or
                pr.get("head", {}).get("repo", {}).get("full_name") != REPO):
            continue
        head = pr.get("head", {}).get("sha", "")
        if not SHA.fullmatch(head):
            continue
        try:
            if tree_for(head, token, fetch) != main_tree:
                continue
            number = pr.get("number")
            for workflow, required in (
                ("backend-ci.yml", ("Restore, Build and Test", "kafka-integration")),
                ("backend-code-quality.yml", ("code-quality",)),
            ):
                candidates = [run for run in matching_runs(workflow, head, token, fetch)
                              if run.get("event") in ("pull_request", "merge_group") and
                              any(p.get("number") == number for p in run.get("pull_requests", []))]
                if not any(validated_check_suite_tree(run, main_tree, token, fetch) and
                           successful_jobs(run, required, token, fetch)
                           for run in candidates):
                    break
            else:
                return True, "Two successful PR check suites verified against identical tested Git tree"
        except (ValueError, TypeError, KeyError, urllib.error.URLError, EvidenceError):
            continue
    return False, "No fully attested identical-tree PR CI evidence"


def ci_reusable_from_existing_release(sha: str, token: str, fetch=get) -> tuple[bool, str]:
    """One completed release validation can serve both infrastructure and app.

    Trust only successful REQUIRED jobs from a previously started trusted main
    workflow run for the identical SHA, not the parent's final conclusion
    (infrastructure might fail after CI). Never trust caller-provided artifacts.
    """
    for caller in ("backend-deploy-production.yml", "azure-infrastructure.yml"):
        query = urllib.parse.urlencode(dict(head_sha=sha, per_page=100))
        runs = fetch("/actions/workflows/" + caller + "/runs?" + query, token).get("workflow_runs", [])
        if len(runs) >= 100:
            continue
        for run in runs:
            if (run.get("head_sha") != sha or run.get("head_branch") != "main" or
                    run.get("head_repository", {}).get("full_name") != REPO or
                    run.get("event") not in ("push", "workflow_dispatch") or
                    run.get("id") == int(os.environ.get("GITHUB_RUN_ID") or "0")):
                continue
            if successful_jobs(run, ("Restore, Build and Test", "kafka-integration",
                                     "Verify trusted source after required CI"), token, fetch):
                return True, "Prior trusted release already validated the identical main SHA"
    return False, "No completed same-SHA release validation"


def reuse_ci(env: dict, fetch=get) -> tuple[bool, str]:
    try:
        sha, token = trusted_context(env, fetch)
        # Manual release invocations intentionally recompute unit and Kafka CI.
        if env.get("SOURCE_EVENT") == "workflow_dispatch":
            return False, "Manual release requires full CI"
        prior, reason = ci_reusable_from_existing_release(sha, token, fetch)
        if prior:
            return True, reason
        return ci_reusable_from_pr(sha, token, fetch)
    except Exception:
        # Network/API errors and unknown shapes must run CI, not accept evidence.
        return False, "Cannot verify trusted CI evidence; run full CI"


def sonar_complete(sha: str, token: str, fetch=get) -> bool:
    for run in matching_runs("backend-sonarqube.yml", sha, token, fetch):
        if run.get("event") not in ("push", "workflow_dispatch"):
            continue
        if successful_jobs(run, ("SonarScanner for .NET 10",), token, fetch):
            return True
    return False


def await_sonar(env: dict, fetch=get, *, attempts=48, delay=20, sleep=time.sleep) -> bool:
    sha, token = trusted_context(env, fetch)
    for attempt in range(attempts):
        # Recheck main at end of polling so a superseded release never proceeds.
        try:
            if sonar_complete(sha, token, fetch):
                trusted_context(env, fetch)
                return True
        except (urllib.error.URLError, KeyError, ValueError, EvidenceError):
            pass  # Transient API failure: retry, never treat as success.
        if attempt + 1 < attempts:
            sleep(delay)
    return False


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("probe-ci", "require-sonar"))
    args = parser.parse_args()
    if args.mode == "probe-ci":
        reuse, reason = reuse_ci(dict(os.environ))
        output = os.environ.get("GITHUB_OUTPUT")
        if output:
            with open(output, "a", encoding="utf-8") as file:
                file.write("reuse=" + str(reuse).lower() + "\n")
        print("Release CI: " + ("verified PR evidence reusable" if reuse else "full CI required") + ". " + reason)
        return 0
    try:
        if await_sonar(dict(os.environ)):
            print("PASS: same-commit SonarQube scan completed with enforced quality gate")
            return 0
    except Exception:
        pass
    print("ERROR: No successful, enforced SonarQube analysis for current trusted main SHA", file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
