#!/usr/bin/env bash
set -euo pipefail
mkdir -p artifacts/screenshots
pytest -v test_researchtrack_full_system.py \
  --html=artifacts/researchtrack-selenium-report.html \
  --self-contained-html "$@"
