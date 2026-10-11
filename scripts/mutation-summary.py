#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from collections import Counter
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

SERVICE_SCOPES = {
    "auth": [
        ("Password policy", "password-policy"),
        ("Authentication", "authentication"),
        ("Password reset", "password-reset"),
        ("Registration", "registration"),
        ("User account", "user-account"),
        ("User directory", "user-directory"),
    ],
    "project": [
        ("Rules", "rules"),
        ("Project service", "project-service"),
        ("Supervisor dashboard", "dashboard"),
    ],
    "submission": [
        ("Rules", "rules"),
        ("Requirement service", "requirement-service"),
        ("Submission service", "submission-service"),
    ],
    "github": [
        ("Repository URL", "repository-url"),
        ("Webhook signature", "webhook-signature"),
        ("Access request token", "access-token"),
        ("Installation state", "installation-state"),
        ("Repository link", "repository-link"),
        ("Webhook event processor", "webhook-event"),
        ("Reconciliation", "reconciliation"),
    ],
    "jira": [
        ("Issue mapper", "issue-mapper"),
        ("Sync state transitions", "sync-state"),
        ("Issue query", "issue-query"),
        ("Sprint progress", "sprint-progress"),
        ("Webhook", "webhook"),
    ],
    "gateway": [
        ("Performance rate-limit policy", "performance-rate-limit"),
        ("Trusted proxy forwarding", "trusted-proxy"),
    ],
    "meeting": [
        ("Meeting channel service", "channel-service"),
        ("Meeting record service", "record-service"),
    ],
}

DISPLAY = {
    "auth": "AuthService",
    "project": "ProjectService",
    "submission": "SubmissionService",
    "github": "GitHubService",
    "jira": "JiraService",
    "gateway": "Gateway",
    "meeting": "MeetingService",
}

@dataclass(frozen=True)
class Result:
    service: str
    label: str
    scope: str
    score: float | None
    killed: int | None
    survived: int | None
    timeout: int | None
    no_coverage: int | None
    ignored: int | None
    compile_error: int | None
    report: str


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate weighted ResearchTrack mutation evidence.")
    parser.add_argument("artifact_root", nargs="?", default="artifacts/mutation")
    parser.add_argument("--policy", default=None, help="Regression policy JSON. Defaults to config/testing/mutation-policy.json relative to repo root.")
    parser.add_argument("--enforce", action="store_true", help="Return non-zero if required reports are missing or weighted regression floors fail.")
    return parser.parse_args()


def latest_json(directory: Path) -> Path | None:
    if not directory.exists():
        return None
    files = [p for p in directory.rglob("*.json") if "report" in p.name.lower() or "mutation" in p.name.lower()]
    return max(files, key=lambda p: p.stat().st_mtime) if files else None


def result_for(root: Path, service: str, label: str, scope: str) -> Result:
    directory = root / service / scope
    report = latest_json(directory)
    if report is None:
        return Result(service, label, scope, None, None, None, None, None, None, None, "—")
    data = json.loads(report.read_text(encoding="utf-8", errors="replace"))
    counts: Counter[str] = Counter()
    for file_data in data.get("files", {}).values():
        for mutant in file_data.get("mutants", []):
            counts[str(mutant.get("status", "Unknown"))] += 1
    killed = counts.get("Killed", 0)
    survived = counts.get("Survived", 0)
    timeout = counts.get("Timeout", 0)
    no_coverage = counts.get("NoCoverage", 0)
    ignored = counts.get("Ignored", 0)
    compile_error = counts.get("CompileError", 0)
    detected = killed + timeout
    denominator = detected + survived + no_coverage
    score = 100.0 * detected / denominator if denominator else None
    md = next(iter(sorted(directory.rglob("*.md"))), None)
    shown = (md or report).relative_to(root).as_posix()
    return Result(service, label, scope, score, killed, survived, timeout, no_coverage, ignored, compile_error, shown)


def fmt(v):
    return "—" if v is None else str(v)


def fmt_score(v):
    return "Not run" if v is None else f"{v:.2f}%"


def weighted(results: list[Result]):
    killed = sum(r.killed or 0 for r in results)
    timeout = sum(r.timeout or 0 for r in results)
    survived = sum(r.survived or 0 for r in results)
    no_cov = sum(r.no_coverage or 0 for r in results)
    denom = killed + timeout + survived + no_cov
    return ((100.0 * (killed + timeout) / denom) if denom else None, killed, survived, timeout, no_cov)


def default_policy_path(root: Path) -> Path:
    # Resolve from this script's repository location rather than from the artifact
    # path. This is robust under Windows Git Bash/MSYS, where an argument such as
    # /e/Projects/... is not interpreted by native Windows Python the same way
    # Bash interprets it.
    repo = Path(__file__).resolve().parents[1]
    return repo / "config" / "testing" / "mutation-policy.json"


def load_policy(path: Path, *, required: bool = False) -> dict:
    if not path.exists():
        if required:
            raise FileNotFoundError(f"Mutation regression policy not found: {path}")
        return {"overallFloor": None, "serviceFloors": {}}
    return json.loads(path.read_text(encoding="utf-8"))


def gate(score: float | None, floor: float | None) -> str:
    if floor is None:
        return "N/A"
    if score is None:
        return "MISSING"
    return "PASS" if score + 1e-9 >= floor else "FAIL"


def main() -> int:
    args = parse_args()
    root = Path(args.artifact_root)
    root.mkdir(parents=True, exist_ok=True)
    policy_path = Path(args.policy) if args.policy else default_policy_path(root)
    policy = load_policy(policy_path, required=args.enforce)
    service_floors: dict[str, float] = {k: float(v) for k, v in policy.get("serviceFloors", {}).items()}
    overall_floor = policy.get("overallFloor")
    overall_floor = float(overall_floor) if overall_floor is not None else None

    all_results: dict[str, list[Result]] = {
        service: [result_for(root, service, label, scope) for label, scope in scopes]
        for service, scopes in SERVICE_SCOPES.items()
    }

    lines = [
        "# ResearchTrack Mutation Testing — Final Project-Wide Business-Logic Evidence",
        "",
        f"Generated: {datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M UTC')}",
        f"Regression policy: `{policy_path}`",
        "",
        "Mutation testing is intentionally scoped to high-value validation, authorization, security, state-transition, workflow, synchronization, and orchestration logic. Controllers, DTO-only contracts, generated code, migrations, and routine persistence/transport plumbing are excluded from the mutation denominator.",
        "",
        "## Service summary and regression gates",
        "",
        "| Service | Weighted score | Regression floor | Gate | Killed | Survived | Timeout | No coverage |",
        "|---|---:|---:|:---:|---:|---:|---:|---:|",
    ]

    project_k = project_s = project_t = project_n = 0
    service_weighted: dict[str, tuple] = {}
    failures: list[str] = []
    for service in SERVICE_SCOPES:
        score, k, s, t, n = weighted(all_results[service])
        service_weighted[service] = (score, k, s, t, n)
        project_k += k; project_s += s; project_t += t; project_n += n
        floor = service_floors.get(service)
        status = gate(score, floor)
        if args.enforce and status != "PASS":
            failures.append(f"{DISPLAY[service]}: {status} (score={fmt_score(score)}, floor={floor})")
        lines.append(
            f"| {DISPLAY[service]} | {fmt_score(score)} | {fmt_score(floor)} | **{status}** | "
            f"{k if (k+s+t+n) else '—'} | {s if (k+s+t+n) else '—'} | {t if (k+s+t+n) else '—'} | {n if (k+s+t+n) else '—'} |"
        )

    total = project_k + project_s + project_t + project_n
    overall = 100.0 * (project_k + project_t) / total if total else None
    overall_status = gate(overall, overall_floor)
    if args.enforce and overall_status != "PASS":
        failures.append(f"Overall: {overall_status} (score={fmt_score(overall)}, floor={overall_floor})")
    lines += [
        f"| **Overall selected business logic** | **{fmt_score(overall)}** | **{fmt_score(overall_floor)}** | **{overall_status}** | **{project_k if total else '—'}** | **{project_s if total else '—'}** | **{project_t if total else '—'}** | **{project_n if total else '—'}** |",
        "",
        "Regression floors are deliberately below the hardened baselines. They are intended to detect meaningful test-quality regressions without requiring every equivalent or low-value mutant to be killed.",
        "",
    ]

    for service, results in all_results.items():
        lines += [
            f"## {DISPLAY[service]} breakdown",
            "",
            "| Subscope | Score | Killed | Survived | Timeout | No coverage | Ignored | Compile error | Report |",
            "|---|---:|---:|---:|---:|---:|---:|---:|---|",
        ]
        for r in results:
            lines.append(
                f"| {r.label} | {fmt_score(r.score)} | {fmt(r.killed)} | {fmt(r.survived)} | {fmt(r.timeout)} | {fmt(r.no_coverage)} | {fmt(r.ignored)} | {fmt(r.compile_error)} | `{r.report}` |"
            )
        lines.append("")

    lines += [
        "## Interpretation",
        "",
        "- `Killed` and `Timeout` are detected mutants.",
        "- `Survived` means selected tests still passed; remaining survivors require review, not automatic score chasing.",
        "- `No coverage` means the selected mutation harness did not execute the mutant.",
        "- `Ignored` and `Compile error` are reported transparently and are not counted as surviving business mutants.",
        "- Weighted service and overall scores use actual mutant counts; percentages are never averaged across unequal subscopes.",
        "- Per-subscope Stryker `break` thresholds are evidence-based regression floors derived from hardened baselines.",
        "- The project-wide gate is enforced only on the complete `all` run, where every service report is regenerated in the same execution.",
        "",
        "## Final evidence checklist",
        "",
        "- Normal xUnit unit/mock suites pass with `Category!=DatabaseIntegration`.",
        "- Dedicated mutation harnesses pass before Stryker starts.",
        "- HTML, JSON and Markdown Stryker reports are retained per subscope.",
        "- `mutation-summary.md` records weighted service and overall scores plus regression-gate status.",
        "- The final project-wide command is `bash ./scripts/mutation-baseline.sh all`.",
        "",
    ]
    if failures:
        lines += ["## Gate failures", ""] + [f"- {item}" for item in failures] + [""]

    out = root / "mutation-summary.md"
    out.write_text("\n".join(lines), encoding="utf-8")
    print(out.read_text(encoding="utf-8"))
    return 1 if args.enforce and failures else 0

if __name__ == "__main__":
    raise SystemExit(main())
