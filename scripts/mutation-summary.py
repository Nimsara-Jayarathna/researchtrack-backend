#!/usr/bin/env python3
"""Generate ResearchTrack mutation evidence from Stryker JSON/Markdown reports.

JSON is preferred because it provides killed/survived counts as well as the score.
Ignored and compile-error mutants are reported separately and are never silently
counted as surviving business mutants.
"""
from __future__ import annotations

import json
import re
import sys
from collections import Counter
from dataclasses import dataclass
from pathlib import Path

SCORE_RE = re.compile(r"final mutation score is\s+([0-9]+(?:\.[0-9]+)?)%", re.I)
AUTH_SCOPES = (
    ("Password policy", "password-policy"),
    ("Authentication", "authentication"),
    ("Password reset", "password-reset"),
    ("Registration", "registration"),
    ("User account", "user-account"),
    ("User directory", "user-directory"),
)


@dataclass(frozen=True)
class Result:
    label: str
    score: float | None
    killed: int | None
    survived: int | None
    ignored: int | None
    compile_error: int | None
    report: str


def latest_file(directory: Path, suffix: str) -> Path | None:
    if not directory.exists():
        return None
    candidates = [p for p in directory.rglob(f"*{suffix}") if "mutation" in p.name.lower() or "report" in p.name.lower()]
    return max(candidates, key=lambda p: p.stat().st_mtime) if candidates else None


def json_counts(report: Path) -> Counter[str]:
    data = json.loads(report.read_text(encoding="utf-8", errors="replace"))
    counts: Counter[str] = Counter()
    for file_data in data.get("files", {}).values():
        for mutant in file_data.get("mutants", []):
            counts[str(mutant.get("status", "Unknown"))] += 1
    return counts


def markdown_score(report: Path | None) -> float | None:
    if report is None:
        return None
    text = report.read_text(encoding="utf-8", errors="replace")
    match = SCORE_RE.search(text)
    return float(match.group(1)) if match else None


def result_for(root: Path, label: str, directory: Path) -> Result:
    json_report = latest_file(directory, ".json")
    md_report = latest_file(directory, ".md")
    if json_report is None and md_report is None:
        return Result(label, None, None, None, None, None, "—")

    counts = json_counts(json_report) if json_report else Counter()
    killed = counts.get("Killed", 0) if json_report else None
    survived = counts.get("Survived", 0) if json_report else None
    ignored = counts.get("Ignored", 0) if json_report else None
    compile_error = counts.get("CompileError", 0) if json_report else None

    denominator = (killed or 0) + (survived or 0)
    score = (100.0 * (killed or 0) / denominator) if json_report and denominator else markdown_score(md_report)
    report = (md_report or json_report).relative_to(root).as_posix()
    return Result(label, score, killed, survived, ignored, compile_error, report)


def fmt_score(score: float | None) -> str:
    return "Not run" if score is None else f"{score:.2f}%"


def fmt_count(value: int | None) -> str:
    return "—" if value is None else str(value)


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts/mutation")
    root.mkdir(parents=True, exist_ok=True)

    project = result_for(root, "Project selected business rules", root / "project")
    submission = result_for(root, "Submission selected business rules", root / "submission")
    auth = [result_for(root, label, root / "auth" / folder) for label, folder in AUTH_SCOPES]

    auth_killed = sum(r.killed or 0 for r in auth)
    auth_survived = sum(r.survived or 0 for r in auth)
    auth_denom = auth_killed + auth_survived
    auth_score = 100.0 * auth_killed / auth_denom if auth_denom else None

    output = root / "mutation-summary.md"
    lines = [
        "# ResearchTrack Mutation Testing — Business-Logic Evidence",
        "",
        "Mutation testing is restricted to selected high-value business/service logic. Ignored and compile-error mutants are shown separately and are not disguised as surviving mutants.",
        "",
        "| Scope | Score | Killed | Survived | Report |",
        "|---|---:|---:|---:|---|",
        f"| {project.label} | {fmt_score(project.score)} | {fmt_count(project.killed)} | {fmt_count(project.survived)} | `{project.report}` |",
        f"| Auth selected feature/service layer (weighted) | {fmt_score(auth_score)} | {auth_killed if auth_denom else '—'} | {auth_survived if auth_denom else '—'} | `auth/` |",
        f"| {submission.label} | {fmt_score(submission.score)} | {fmt_count(submission.killed)} | {fmt_count(submission.survived)} | `{submission.report}` |",
        "",
        "## AuthService breakdown",
        "",
        "Auth is split into independent subscopes so strong small rule classes cannot hide weaker orchestration logic.",
        "",
        "| Auth subscope | Score | Killed | Survived | Ignored | Compile error | Report |",
        "|---|---:|---:|---:|---:|---:|---|",
    ]
    for r in auth:
        lines.append(
            f"| {r.label} | {fmt_score(r.score)} | {fmt_count(r.killed)} | {fmt_count(r.survived)} | "
            f"{fmt_count(r.ignored)} | {fmt_count(r.compile_error)} | `{r.report}` |"
        )

    lines += [
        "",
        "## Interpretation",
        "",
        "- `Killed`: a test detected the injected behavioral fault.",
        "- `Survived`: all selected tests still passed; review whether this is a real assertion gap or an equivalent/non-business mutation.",
        "- `Ignored`: Stryker intentionally excluded the mutant (for example by scope/block filtering).",
        "- `Compile error`: the generated mutation could not compile and is not treated as a surviving test weakness.",
        "- The Auth weighted score is calculated from the actual killed/survived counts, never by averaging the six percentages.",
        "- Keep `break = 0` until survivor hardening is complete and a stable final baseline has been rerun.",
        "",
    ]
    output.write_text("\n".join(lines), encoding="utf-8")
    print(output.read_text(encoding="utf-8"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
