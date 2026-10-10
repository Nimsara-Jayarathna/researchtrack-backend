#!/usr/bin/env python3
from __future__ import annotations

import json
import sys
from collections import Counter
from dataclasses import dataclass
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


def latest_json(directory: Path) -> Path | None:
    if not directory.exists():
        return None
    files = list(directory.rglob("*.json"))
    files = [p for p in files if "report" in p.name.lower() or "mutation" in p.name.lower()]
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


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts/mutation")
    root.mkdir(parents=True, exist_ok=True)
    all_results: dict[str, list[Result]] = {
        service: [result_for(root, service, label, scope) for label, scope in scopes]
        for service, scopes in SERVICE_SCOPES.items()
    }

    lines = [
        "# ResearchTrack Mutation Testing — Project-Wide Business-Logic Evidence",
        "",
        "Mutation testing is intentionally scoped to high-value validation, authorization, security, state-transition, workflow, synchronization, and orchestration logic. Controllers, DTO-only contracts, generated code, migrations, and routine persistence/transport plumbing are not used to inflate the denominator.",
        "",
        "## Service summary",
        "",
        "| Service | Weighted score | Killed | Survived | Timeout | No coverage |",
        "|---|---:|---:|---:|---:|---:|",
    ]

    project_k = project_s = project_t = project_n = 0
    for service in SERVICE_SCOPES:
        score, k, s, t, n = weighted(all_results[service])
        project_k += k; project_s += s; project_t += t; project_n += n
        lines.append(f"| {DISPLAY[service]} | {fmt_score(score)} | {k if (k+s+t+n) else '—'} | {s if (k+s+t+n) else '—'} | {t if (k+s+t+n) else '—'} | {n if (k+s+t+n) else '—'} |")

    total = project_k + project_s + project_t + project_n
    overall = 100.0 * (project_k + project_t) / total if total else None
    lines += [
        f"| **Overall selected business logic** | **{fmt_score(overall)}** | **{project_k if total else '—'}** | **{project_s if total else '—'}** | **{project_t if total else '—'}** | **{project_n if total else '—'}** |",
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
        "- `Survived` means selected tests still passed and the mutant needs survivor review.",
        "- `No coverage` means the selected mutation harness did not execute the mutant.",
        "- `Ignored` and `Compile error` are shown separately and are not counted as surviving business mutants.",
        "- Weighted service and overall scores use actual mutant counts; percentages are never averaged across unequal subscopes.",
        "- Stryker `break` remains `0` while new service scopes are baselined. Add regression floors only after survivor hardening produces stable final scores.",
        "",
    ]
    out = root / "mutation-summary.md"
    out.write_text("\n".join(lines), encoding="utf-8")
    print(out.read_text(encoding="utf-8"))
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
