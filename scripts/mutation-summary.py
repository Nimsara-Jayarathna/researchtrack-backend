#!/usr/bin/env python3
"""Create a compact ResearchTrack mutation-baseline summary from Stryker reports.

The parser intentionally reads Stryker's Markdown reporter output instead of
binding to the full JSON result schema. This keeps the evidence script simple
and resilient while still preserving the native HTML/JSON reports for detail.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

SERVICES = ("project", "auth", "submission")
SCORE_RE = re.compile(r"final mutation score is\s+([0-9]+(?:\.[0-9]+)?)%", re.I)


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


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts/mutation")
    root.mkdir(parents=True, exist_ok=True)
    rows: list[tuple[str, str, str]] = []

    for service in SERVICES:
        service_dir = root / service
        report = latest_markdown(service_dir) if service_dir.exists() else None
        if report is None:
            rows.append((service.title(), "Not run", "—"))
            continue
        score = extract_score(report)
        relative = report.relative_to(root).as_posix()
        rows.append((service.title(), f"{score:.2f}%" if score is not None else "See report", relative))

    output = root / "mutation-summary.md"
    lines = [
        "# ResearchTrack Mutation Testing — Initial Business-Rule Baseline",
        "",
        "This is the untouched baseline for Pamudi's mutation-testing work. No CI break threshold is enforced yet.",
        "",
        "| Scope | Mutation score | Native Stryker markdown |",
        "|---|---:|---|",
    ]
    lines.extend(f"| {scope} | {score} | `{report}` |" for scope, score, report in rows)
    lines += [
        "",
        "## Interpretation",
        "",
        "- `Killed` means an existing test detected the injected fault.",
        "- `Survived` means tests still passed and the mutant needs review.",
        "- `No coverage` means the mutated statement was not exercised by the selected tests.",
        "- Equivalent or non-business-value mutants should be documented, not hidden merely to raise the score.",
        "- Keep `break = 0` during the baseline. Set a regression floor only after the survivor-hardening rerun.",
        "",
    ]
    output.write_text("\n".join(lines), encoding="utf-8")
    print(output.read_text(encoding="utf-8"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
