#!/usr/bin/env bash
# Pure Node regression for the k6 handleSummary report formatter. It uses a
# synthetic k6 summary object, so CI can verify HTML/Markdown/CSV/JSON report
# generation without installing k6 or touching Azure/VPS infrastructure.
set -Eeuo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
cat > "$work/test.mjs" <<'EOFJS'
import { pathToFileURL } from 'node:url';
import path from 'node:path';

globalThis.__ENV = {
  K6_BASE_URL: 'https://api.example.test',
  K6_PROJECT_ID: '01234567-89ab-cdef-0123-456789abcdef',
  K6_PROFILE: 'smoke',
  K6_INCLUDE_INTEGRATIONS: 'true',
  K6_RATE_LIMIT_BYPASS_ENABLED: 'true',
  K6_PERFORMANCE_TOKEN: 'synthetic-token-not-a-secret-1234567890',
  K6_WARMUP_PASSES: '1',
  K6_SUPERVISOR_EMAIL: 'supervisor@example.test',
  K6_SUPERVISOR_PASSWORD: 'synthetic',
  K6_STUDENT_EMAIL: 'student@example.test',
  K6_STUDENT_PASSWORD: 'synthetic',
  K6_GITHUB_RUN_ID: '12345',
  K6_GITHUB_RUN_ATTEMPT: '2',
  K6_GITHUB_SHA: '0123456789abcdef',
};
const reportUrl = pathToFileURL(path.join(process.env.REPORT_TEST_REPO, 'tests/performance/lib/report.js')).href;
const { reportSummary } = await import(reportUrl);
const trend = { values: { avg: 500, min: 100, med: 400, 'p(90)': 700, 'p(95)': 900, 'p(99)': 1100, max: 1200 }, thresholds: { 'p(95)<1500': { ok: true } } };
const metrics = {
  researchtrack_business_duration: trend,
  researchtrack_business_failures: { values: { rate: 0 }, thresholds: { 'rate<0.01': { ok: true } } },
  researchtrack_http_429: { values: { count: 0 }, thresholds: { 'count==0': { ok: true } } },
  researchtrack_business_requests: { values: { count: 30, rate: 1.5 } },
  researchtrack_business_http_2xx: { values: { count: 30 } },
  researchtrack_business_http_3xx: { values: { count: 0 } },
  researchtrack_business_http_4xx: { values: { count: 0 } },
  researchtrack_business_http_5xx: { values: { count: 0 } },
  researchtrack_business_network_errors: { values: { count: 0 } },
  checks: { values: { rate: 1 }, thresholds: { 'rate>0.99': { ok: true } } },
  iterations: { values: { count: 30 } }, dropped_iterations: { values: { count: 0 } },
  vus_max: { values: { max: 2 } }, data_received: { values: { count: 1000 } }, data_sent: { values: { count: 500 } },
};
for (const op of ['projects', 'jira_issues']) {
  const prefix = `researchtrack_op_${op}`;
  metrics[`${prefix}_requests`] = { values: { count: 15 } };
  metrics[`${prefix}_failures`] = { values: { rate: 0 } };
  metrics[`${prefix}_duration`] = trend;
  for (const status of ['2xx','3xx','4xx','5xx','429']) metrics[`${prefix}_http_${status}`] = { values: { count: status === '2xx' ? 15 : 0 } };
  metrics[`${prefix}_network_errors`] = { values: { count: 0 } };
}
const output = reportSummary({ metrics, state: { testRunDurationMs: 20000 } });
const required = ['./smoke-summary.json','./smoke-report.md','./smoke-report.html','./smoke-endpoints.csv'];
for (const key of required) if (!(key in output)) throw new Error(`missing report output: ${key}`);
if (!output.stdout.includes('Endpoint performance')) throw new Error('Markdown lacks endpoint diagnostics');
if (!output['./smoke-report.html'].includes('Slowest endpoints by p95')) throw new Error('HTML lacks slow-endpoint section');
if (!output['./smoke-endpoints.csv'].includes('p99_ms')) throw new Error('CSV lacks p99 column');
const json = JSON.parse(output['./smoke-summary.json']);
if (json.schemaVersion !== 2 || json.endpoints.length !== 2 || json.overview.p99 !== 1100) throw new Error('Detailed JSON schema is incomplete');
console.log('Detailed k6 report formatter regression passed.');
EOFJS
REPORT_TEST_REPO="$repo" node "$work/test.mjs"
