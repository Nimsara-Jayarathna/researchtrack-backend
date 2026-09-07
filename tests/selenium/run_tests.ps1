New-Item -ItemType Directory -Force -Path artifacts\screenshots | Out-Null
pytest -v test_researchtrack_full_system.py `
  --html=artifacts/researchtrack-selenium-report.html `
  --self-contained-html @args
