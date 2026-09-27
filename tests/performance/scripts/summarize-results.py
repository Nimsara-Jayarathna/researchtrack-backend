#!/usr/bin/env python3
"""Create status-aware QA evidence from a JMeter CSV result file."""

from __future__ import annotations

import argparse
import csv
import math
import sys
from collections import Counter
from pathlib import Path


def percentile(values: list[int], percent: float) -> int:
    if not values:
        return 0
    ordered = sorted(values)
    index = max(0, math.ceil((percent / 100) * len(ordered)) - 1)
    return ordered[index]


def category(response_code: str) -> str:
    try:
        status = int(response_code)
    except ValueError:
        return "other"
    if 200 <= status <= 299:
        return "2xx"
    if status == 401:
        return "401"
    if status == 403:
        return "403"
    if status == 429:
        return "429"
    if 500 <= status <= 599:
        return "5xx"
    return "other"


def rate(count: int, total: int) -> float:
    return (count * 100.0 / total) if total else 0.0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("jtl", type=Path)
    parser.add_argument("output_dir", type=Path)
    parser.add_argument("scenario")
    parser.add_argument("users", type=int)
    parser.add_argument("duration_seconds", type=int)
    args = parser.parse_args()

    with args.jtl.open(newline="", encoding="utf-8") as handle:
        reader = csv.DictReader(handle)
        required = {"timeStamp", "elapsed", "label", "responseCode", "success"}
        if reader.fieldnames is None or not required.issubset(reader.fieldnames):
            missing = sorted(required.difference(reader.fieldnames or []))
            raise SystemExit(f"JTL is missing required CSV columns: {', '.join(missing)}")
        all_rows = list(reader)

    auth_rows = [row for row in all_rows if row["label"].startswith("AUTH ")]
    rows = [row for row in all_rows if not row["label"].startswith("AUTH ")]
    if not rows:
        for row in auth_rows:
            if row["success"].lower() != "true":
                detail = row.get("failureMessage") or row.get("responseMessage") or "unknown error"
                print(
                    f"Authentication setup failed: HTTP {row['responseCode']} - {detail}",
                    file=sys.stderr,
                )
        raise SystemExit("JTL contains no workload samples")

    elapsed = [int(row["elapsed"]) for row in rows]
    counts = Counter(category(row["responseCode"]) for row in rows)
    failures = sum(row["success"].lower() != "true" for row in rows)
    start_ms = min(int(row["timeStamp"]) for row in rows)
    end_ms = max(int(row["timeStamp"]) + int(row["elapsed"]) for row in rows)
    measured_seconds = max((end_ms - start_ms) / 1000.0, 0.001)
    total = len(rows)

    summary = {
        "scenario": args.scenario,
        "users": args.users,
        "configured_duration_seconds": args.duration_seconds,
        "measured_seconds": round(measured_seconds, 3),
        "samples": total,
        "average_ms": round(sum(elapsed) / total, 2),
        "median_ms": percentile(elapsed, 50),
        "p90_ms": percentile(elapsed, 90),
        "p95_ms": percentile(elapsed, 95),
        "maximum_ms": max(elapsed),
        "throughput_rps": round(total / measured_seconds, 3),
        "error_percent": round(rate(failures, total), 3),
        "http_2xx_count": counts["2xx"],
        "http_2xx_rate_percent": round(rate(counts["2xx"], total), 3),
        "http_401_count": counts["401"],
        "http_401_rate_percent": round(rate(counts["401"], total), 3),
        "http_403_count": counts["403"],
        "http_403_rate_percent": round(rate(counts["403"], total), 3),
        "http_429_count": counts["429"],
        "http_429_rate_percent": round(rate(counts["429"], total), 3),
        "http_5xx_count": counts["5xx"],
        "http_5xx_rate_percent": round(rate(counts["5xx"], total), 3),
        "http_other_count": counts["other"],
        "http_other_rate_percent": round(rate(counts["other"], total), 3),
        "authentication_setup_samples": len(auth_rows),
        "authentication_setup_failures": sum(
            row["success"].lower() != "true" for row in auth_rows
        ),
    }

    args.output_dir.mkdir(parents=True, exist_ok=True)
    csv_path = args.output_dir / "status-summary.csv"
    with csv_path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(summary))
        writer.writeheader()
        writer.writerow(summary)

    markdown_path = args.output_dir / "status-summary.md"
    markdown_path.write_text(
        "\n".join(
            [
                f"# {args.scenario} status-aware summary",
                "",
                "| Metric | Value |",
                "| --- | ---: |",
                f"| Users | {args.users} |",
                f"| Configured duration | {args.duration_seconds} s |",
                f"| Workload samples | {total} |",
                f"| Average | {summary['average_ms']} ms |",
                f"| Median | {summary['median_ms']} ms |",
                f"| P90 | {summary['p90_ms']} ms |",
                f"| P95 | {summary['p95_ms']} ms |",
                f"| Maximum | {summary['maximum_ms']} ms |",
                f"| Throughput | {summary['throughput_rps']} requests/s |",
                f"| JMeter error rate | {summary['error_percent']}% |",
                f"| 2xx | {counts['2xx']} ({summary['http_2xx_rate_percent']}%) |",
                f"| 401 authentication | {counts['401']} ({summary['http_401_rate_percent']}%) |",
                f"| 403 authorization | {counts['403']} ({summary['http_403_rate_percent']}%) |",
                f"| 429 rate limited | {counts['429']} ({summary['http_429_rate_percent']}%) |",
                f"| 5xx server failure | {counts['5xx']} ({summary['http_5xx_rate_percent']}%) |",
                f"| Other status | {counts['other']} ({summary['http_other_rate_percent']}%) |",
                f"| Authentication setup failures | {summary['authentication_setup_failures']} |",
                "",
                "A 429 is a controlled protection response, not a 5xx backend failure. "
                "It remains an unsuccessful business request and is reported separately.",
                "",
            ]
        ),
        encoding="utf-8",
    )

    print(f"Status summary: {markdown_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
