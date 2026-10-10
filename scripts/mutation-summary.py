#!/usr/bin/env python3
"""Create a compact ResearchTrack mutation-baseline summary from Stryker reports.

The summary deliberately keeps Auth's expanded feature scopes separate. A weighted
Auth aggregate is intentionally deferred until survivor hardening/final evidence,
so a strong small component cannot hide a weaker large component.
"""
from __future__ import annotations

import re
import sys
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


def latest_markdown(service_dir: Path) -> Path | None:
    candidates = [
        p
        for p in service_dir.rglob("*.md")
        if "mutation" in p.name.lower() or "report" in p.name.lower()
    ]
    if not candidates:
        return None
    return max(candidates, key=lambda p: p.stat().st_mtime)


def extract_score(report: Path) -> float | None:
    text = report.read_text(encoding="utf-8", errors="replace")
    match = SCORE_RE.search(text)
    return float(match.group(1)) if match else None


def report_row(root: Path, label: str, directory: Path) -> tuple[str, str, str]:
    report = latest_markdown(directory) if directory.exists() else None
    if report is None:
        return label, "Not run", "—"
    score = extract_score(report)
    relative = report.relative_to(root).as_posix()
    return label, f"{score:.2f}%" if score is not None else "See report", relative


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts/mutation")
    root.mkdir(parents=True, exist_ok=True)

    top_rows = [
        report_row(root, "Project selected business rules", root / "project"),
        ("Auth complete feature/service layer", "See Auth subscopes", "auth/"),
        report_row(root, "Submission selected business rules", root / "submission"),
    ]
    auth_rows = [
        report_row(root, label, root / "auth" / folder)
        for label, folder in AUTH_SCOPES
    ]

    output = root / "mutation-summary.md"
    lines = [
        "# ResearchTrack Mutation Testing — Business-Logic Baseline",
        "",
        "No CI break threshold is enforced during the baseline/survivor-analysis phase.",
        "",
        "| Scope | Mutation score | Native Stryker markdown |",
        "|---|---:|---|",
    ]
    lines.extend(f"| {scope} | {score} | `{report}` |" for scope, score, report in top_rows)
    lines += [
        "",
        "## AuthService feature/service-layer breakdown",
        "",
        "Auth is intentionally split into independent mutation subscopes so a small 100% component cannot hide weaker orchestration logic.",
        "",
        "| Auth subscope | Mutation score | Native Stryker markdown |",
        "|---|---:|---|",
    ]
    lines.extend(f"| {scope} | {score} | `{report}` |" for scope, score, report in auth_rows)
    lines += [
        "",
        "## Interpretation",
        "",
        "- `Killed` means an existing test detected the injected fault.",
        "- `Survived` means tests still passed and the mutant needs review.",
        "- `No coverage` means the mutated statement was not exercised by the selected tests.",
        "- Equivalent or non-business-value mutants should be documented, not hidden merely to raise the score.",
        "- Keep `break = 0` during the baseline. Set a regression floor only after survivor hardening and a verified final baseline.",
        "- Auth's final weighted score should be calculated from killed/survived counts after all six subscopes are finalized; do not average the six percentages.",
        "",
    ]
    output.write_text("\n".join(lines), encoding="utf-8")
    print(output.read_text(encoding="utf-8"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
