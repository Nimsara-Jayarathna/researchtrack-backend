#!/usr/bin/env python3
"""Conservative, read-only workload filter for ResearchTrack GitHub Actions.

This is NOT a security or release-eligibility gate. It can only suppress work
for positively identified non-relevant changes. Unknown history/files = run.
Production still uses its existing exact-SHA CI + release-policy check.
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys

SHA = re.compile(r"[0-9a-f]{40}\Z")
MODES = ("application", "sonar", "devops")


def run_git(*arguments: str) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(["git", *arguments], stdout=subprocess.PIPE,
                          stderr=subprocess.DEVNULL, check=False)


def source_only(path: str) -> bool:
    """Source/tests/docs edits never change our offline deployment scripts."""
    return (path.startswith(("src/", "tests/", "docs/", "config/testing/"))
            and not path.startswith("tests/performance/")) or (
            path in {"ResearchTrack.sln", "Directory.Build.props", "Directory.Packages.props",
                     "global.json", ".editorconfig", ".csharpierrc", ".csharpierignore"}
            or path.endswith(".md"))


def unnecessary_for_application(path: str) -> bool:
    """Whitelist ONLY paths proven not to change deployed application/runtime state."""
    if path.endswith(".md") or path.startswith(("docs/", "tests/", "config/testing/",
                                                "scripts/code-quality/", ".github/ISSUE_TEMPLATE/")):
        return True
    if path in {".gitignore", ".gitattributes", ".csharpierrc", ".csharpierignore",
                ".github/CODEOWNERS", ".github/pull_request_template.md",
                "scripts/coverage.sh"}:
        return True
    if path.startswith(".github/workflows/"):
        return path in {
            ".github/workflows/backend-ci.yml",
            ".github/workflows/backend-code-quality.yml",
            ".github/workflows/backend-sonarqube.yml",
            ".github/workflows/backend-mutation.yml",
            ".github/workflows/kafka-integration-tests.yml",
            ".github/workflows/azure-performance-k6.yml",
            ".github/workflows/azure-performance-k6-reusable.yml",
        }
    if path.startswith(("deploy/azure/modules/", "deploy/azure/parameters/",
                        "deploy/azure/vm/", "deploy/monitoring/")):
        return True  # Infrastructure reconciler owns these paths.
    if path == "deploy/azure/main.bicep":
        return True
    return False


def unnecessary_for_sonar(path: str) -> bool:
    if path.endswith(".md") or path.startswith(("docs/", "tests/performance/", ".github/ISSUE_TEMPLATE/")):
        return True
    if path in {".gitignore", ".gitattributes", ".github/CODEOWNERS"}:
        return True
    if path.startswith(".github/workflows/") and path != ".github/workflows/backend-sonarqube.yml":
        return True
    if path.startswith(("deploy/monitoring/", "deploy/azure/vm/")):
        return True
    return False


def determine(mode: str, event: str, base: str, head: str, force: bool) -> tuple[bool, str, list[str]]:
    if force or event == "workflow_dispatch":
        return True, "manual invocation/force override", []
    if mode == "devops" and event != "pull_request":
        return True, "non-PR validation must be complete", []
    if mode != "devops" and event != "push":
        return True, "unexpected event; conservative execution", []
    if not (SHA.fullmatch(base or "") and SHA.fullmatch(head or "")):
        return True, "missing or invalid Git commit SHA", []
    if base == "0" * 40 or head == base:
        return True, "initial/unchanged Git history: conservatively run", []
    if run_git("cat-file", "-e", base + "^{commit}").returncode != 0:
        return True, "before commit unavailable; conservatively run", []
    if run_git("cat-file", "-e", head + "^{commit}").returncode != 0:
        return True, "head commit unavailable; conservatively run", []
    if run_git("merge-base", "--is-ancestor", base, head).returncode != 0:
        return True, "non-ancestor/force push; conservatively run", []
    result = run_git("diff", "--name-only", "-z", "--no-ext-diff", base, head)
    if result.returncode:
        return True, "Git diff failed; conservatively run", []
    try:
        paths = [p.decode("utf-8", errors="strict") for p in result.stdout.split(b"\0") if p]
    except UnicodeDecodeError:
        return True, "unexpected filename encoding", []
    if not paths:
        return True, "empty diff is ambiguous", []
    safe = {"application": unnecessary_for_application,
            "sonar": unnecessary_for_sonar,
            "devops": source_only}[mode]
    relevant = [path for path in paths if not safe(path)]
    if relevant:
        return True, f"{len(relevant)} relevant or unclassified file(s)", paths
    return False, f"all {len(paths)} changed file(s) confirmed irrelevant", paths


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mode", choices=MODES, required=True)
    parser.add_argument("--event", required=True)
    parser.add_argument("--base", default="")
    parser.add_argument("--head", default="")
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()
    try:
        execute, reason, paths = determine(args.mode, args.event, args.base, args.head, args.force)
    except Exception as exc:
        # Classification errors MUST NEVER silently suppress work.
        execute, reason, paths = True, "classifier error: " + type(exc).__name__, []
    decision = "true" if execute else "false"
    print(f"{args.mode}: execute={decision} ({reason})")
    for path in paths[:20]:
        print("  " + repr(path))
    if len(paths) > 20:
        print(f"  ...and {len(paths) - 20} additional paths")
    output = os.environ.get("GITHUB_OUTPUT")
    if output:
        with open(output, "a", encoding="utf8") as stream:
            stream.write(f"execute={decision}\n")
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf8") as stream:
            stream.write(f"\n### Change scope — {args.mode}\nRun: **{decision}**. {reason}.\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
