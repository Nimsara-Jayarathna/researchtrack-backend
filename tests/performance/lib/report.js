import { REPORT_DIR, PROFILE } from './settings.js';

function read(metrics, name, stat) {
  return metrics[name]?.values?.[stat] ?? null;
}
function fmt(value, unit = '') {
  return value == null ? 'n/a' : `${Number(value).toFixed(2)}${unit}`;
}

export function reportSummary(data) {
  const metrics = data.metrics || {};
  const latency = read(metrics, 'researchtrack_business_duration', 'p(95)');
  const errors = read(metrics, 'researchtrack_business_failures', 'rate');
  const throttle = read(metrics, 'researchtrack_http_429', 'count') || 0;
  const requests = read(metrics, 'researchtrack_business_requests', 'count');
  const failedThresholds = Object.entries(metrics).flatMap(([name, metric]) =>
    Object.entries(metric.thresholds || {})
      .filter(([, item]) => !item.ok).map(([rule]) => `${name}: ${rule}`));
  const passed = failedThresholds.length === 0;
  const markdown = [
    `# ResearchTrack k6 — ${PROFILE}`,
    '', '| Measure | Result |', '|---|---:|',
    `| Business requests | ${requests ?? 'n/a'} |`,
    `| p95 business latency | ${fmt(latency, ' ms')} |`,
    `| Business failure rate | ${fmt(errors == null ? null : errors * 100, '%')} |`,
    `| HTTP 429 responses | ${throttle} |`,
    `| Throughput (HTTP req/s) | ${fmt(read(metrics, 'http_reqs', 'rate'))} |`,
    `| p99 HTTP latency | ${fmt(read(metrics, 'http_req_duration', 'p(99)'), ' ms')} |`,
    `| Threshold gate | **${passed ? 'PASS' : 'FAIL'}** |`,
    '', ...(passed ? [] : ['Failed thresholds:', ...failedThresholds.map(s => `- ${s}`)]),
    '', 'Results measure the current deployment; they are not a CI source-revision guarantee unless the tested revision is also verified.',
  ].join('\n');
  return {
    stdout: `${markdown}\n`,
    [`${REPORT_DIR}/${PROFILE}-summary.json`]: JSON.stringify({
      profile: PROFILE, passed, businessRequests: requests, p95Milliseconds: latency,
      errorRate: errors, http429Count: throttle, failures: failedThresholds,
      metrics,
    }, null, 2),
    [`${REPORT_DIR}/${PROFILE}-report.md`]: markdown + '\n',
  };
}
