#!/usr/bin/env bash
set -euo pipefail

if [[ ! -x .venv/bin/pytest ]]; then
  if ! command -v python3 >/dev/null 2>&1; then
    echo "Python 3 is required to run the Selenium tests." >&2
    exit 127
  fi

  echo "Setting up the local Selenium test environment..."
  python3 -m venv .venv
  .venv/bin/python -m pip install -r requirements.txt
fi

pytest_bin=.venv/bin/pytest
mkdir -p artifacts/screenshots
"$pytest_bin" -v test_researchtrack_full_system.py \
  --html=artifacts/researchtrack-selenium-report.html \
  --self-contained-html "$@"
