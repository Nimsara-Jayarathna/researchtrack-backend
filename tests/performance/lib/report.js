import {
  BASE_URL, PROJECT_ID, PROFILE, INCLUDE_INTEGRATIONS,
  REPORT_DIR, BYPASS_ENABLED, WARMUP_PASSES, EXECUTION_MODE,
} from './settings.js';
import { profileMetadata } from './profiles.js';
import { OPERATION_CATALOG, OPERATION_KEYS } from './operations.js';

function metric(data, name) {
  return data.metrics?.[name] || null;
}
function read(data, name, stat, fallback = null) {
  return metric(data, name)?.values?.[stat] ?? fallback;
}
function count(data, name) {
  return read(data, name, 'count', 0) || 0;
}
function rate(data, name) {
  return read(data, name, 'rate', null);
}
function fmt(value, digits = 2, unit = '') {
  return value == null || Number.isNaN(Number(value)) ? 'n/a' : `${Number(value).toFixed(digits)}${unit}`;
}
function fmtInt(value) {
  return value == null ? 'n/a' : `${Math.round(Number(value))}`;
}
function fmtPct(value) {
  return value == null ? 'n/a' : `${(Number(value) * 100).toFixed(2)}%`;
}
function escapeHtml(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;').replaceAll("'", '&#39;');
}
function csv(value) {
  const text = String(value ?? '');
  return `"${text.replaceAll('"', '""')}"`;
}
function safeOrigin(url) {
  try { return new URL(url).origin; }
  catch (_) { return url; }
}
function gateText(endpoint) {
  if (endpoint.targetP95Ms == null) return 'Not gated';
  if (endpoint.thresholdPassed == null) return 'No sample';
  return endpoint.thresholdPassed ? 'PASS' : 'FAIL';
}

function thresholdRows(data) {
  const rows = [];
  for (const [name, m] of Object.entries(data.metrics || {})) {
    for (const [rule, result] of Object.entries(m.thresholds || {})) {
      rows.push({ metric: name, rule, passed: Boolean(result.ok) });
    }
  }
  return rows;
}

function endpointRows(data, endpointThresholds) {
  const rows = [];
  for (const operation of OPERATION_KEYS) {
    if (operation === 'unknown') continue;
    const prefix = `researchtrack_op_${operation}`;
    const requests = count(data, `${prefix}_requests`);
    if (!requests) continue;
    const failures = rate(data, `${prefix}_failures`) ?? 0;
    const p95 = read(data, `${prefix}_duration`, 'p(95)');
    const threshold = endpointThresholds[operation] || null;
    rows.push({
      operation,
      label: OPERATION_CATALOG[operation]?.label || operation,
      category: OPERATION_CATALOG[operation]?.category || 'Other',
      method: OPERATION_CATALOG[operation]?.method || 'HTTP',
      requests,
      failureRate: failures,
      successRate: Math.max(0, 1 - failures),
      avg: read(data, `${prefix}_duration`, 'avg'),
      min: read(data, `${prefix}_duration`, 'min'),
      p50: read(data, `${prefix}_duration`, 'med'),
      p90: read(data, `${prefix}_duration`, 'p(90)'),
      p95,
      p99: read(data, `${prefix}_duration`, 'p(99)'),
      max: read(data, `${prefix}_duration`, 'max'),
      http2xx: count(data, `${prefix}_http_2xx`),
      http3xx: count(data, `${prefix}_http_3xx`),
      http4xx: count(data, `${prefix}_http_4xx`),
      http5xx: count(data, `${prefix}_http_5xx`),
      http429: count(data, `${prefix}_http_429`),
      networkErrors: count(data, `${prefix}_network_errors`),
      targetP95Ms: threshold?.p95Ms ?? null,
      thresholdSource: threshold?.sourceKey ?? null,
      thresholdPassed: threshold && p95 != null ? p95 < threshold.p95Ms : null,
    });
  }
  return rows.sort((a, b) => (b.p95 ?? -1) - (a.p95 ?? -1));
}

function overview(data, endpoints, thresholds, profile) {
  const testRunDurationMs = data.state?.testRunDurationMs ?? null;
  const businessCount = count(data, 'researchtrack_business_requests');
  const businessRate = read(data, 'researchtrack_business_requests', 'rate',
    testRunDurationMs ? businessCount / (testRunDurationMs / 1000) : null);
  return {
    generatedAtUtc: new Date().toISOString(),
    profile: PROFILE,
    purpose: profile.purpose,
    workload: profile.workload,
    targetP95Ms: profile.p95Ms,
    targetP95Source: profile.p95ThresholdSource,
    targetP95SourceKey: profile.p95ThresholdSourceKey,
    maxFailureRate: profile.maxFailureRate,
    baseUrl: safeOrigin(BASE_URL),
    projectId: PROJECT_ID,
    includeIntegrations: INCLUDE_INTEGRATIONS,
    gatewayBypassEnabled: BYPASS_ENABLED,
    warmupPasses: WARMUP_PASSES,
    executionMode: EXECUTION_MODE,
    sourceRunId: __ENV.K6_GITHUB_RUN_ID || null,
    sourceRunAttempt: __ENV.K6_GITHUB_RUN_ATTEMPT || null,
    sourceSha: __ENV.K6_GITHUB_SHA || null,
    sourceWorkflow: __ENV.K6_GITHUB_WORKFLOW || null,
    testRunDurationMs,
    passed: thresholds.every(t => t.passed),
    businessRequests: businessCount,
    businessThroughput: businessRate,
    businessFailureRate: rate(data, 'researchtrack_business_failures'),
    p50: read(data, 'researchtrack_business_duration', 'med'),
    p90: read(data, 'researchtrack_business_duration', 'p(90)'),
    p95: read(data, 'researchtrack_business_duration', 'p(95)'),
    p99: read(data, 'researchtrack_business_duration', 'p(99)'),
    avg: read(data, 'researchtrack_business_duration', 'avg'),
    min: read(data, 'researchtrack_business_duration', 'min'),
    max: read(data, 'researchtrack_business_duration', 'max'),
    http429: count(data, 'researchtrack_http_429'),
    http2xx: count(data, 'researchtrack_business_http_2xx'),
    http3xx: count(data, 'researchtrack_business_http_3xx'),
    http4xx: count(data, 'researchtrack_business_http_4xx'),
    http5xx: count(data, 'researchtrack_business_http_5xx'),
    networkErrors: count(data, 'researchtrack_business_network_errors'),
    checksRate: rate(data, 'checks'),
    iterations: count(data, 'iterations'),
    droppedIterations: count(data, 'dropped_iterations'),
    maxVUs: read(data, 'vus_max', 'max', read(data, 'vus_max', 'value')),
    dataReceivedBytes: count(data, 'data_received'),
    dataSentBytes: count(data, 'data_sent'),
    endpointCount: endpoints.length,
  };
}

function thresholdSourceText(o) {
  return o.targetP95SourceKey
    ? `${o.targetP95Source} (${o.targetP95SourceKey})`
    : o.targetP95Source;
}

function markdownReport(o, endpoints, thresholds) {
  const slowest = endpoints.slice(0, 5);
  const failedEndpoints = endpoints.filter(e => e.failureRate > 0 || e.http4xx > 0 || e.http5xx > 0 || e.networkErrors > 0);
  const configuredEndpointGates = endpoints.filter(e => e.targetP95Ms != null);
  const jiraIssues = endpoints.find(e => e.operation === 'jira_issues');
  const lines = [
    `# ResearchTrack k6 — ${o.profile}`,
    '',
    `**Performance gate: ${o.passed ? 'PASS ✅' : 'FAIL ❌'}**`,
    '',
    `Generated: ${o.generatedAtUtc}  `,
    `Target: ${o.baseUrl}  `,
    `Project: \`${o.projectId}\`  `,
    `Purpose: ${o.purpose}  `,
    `Workload: ${o.workload}  `,
    `Execution mode: ${o.executionMode} | Warm-up passes: ${o.warmupPasses} | Integrations: ${o.includeIntegrations} | Gateway exemption: ${o.gatewayBypassEnabled}`,
    '',
    '## Executive summary',
    '',
    '| Measure | Result | Target / meaning |',
    '|---|---:|---|',
    `| Business requests | ${fmtInt(o.businessRequests)} | Measured requests only |`,
    `| Throughput | ${fmt(o.businessThroughput, 2, ' req/s')} | Business requests only |`,
    `| Business failure rate | ${fmtPct(o.businessFailureRate)} | < ${(o.maxFailureRate * 100).toFixed(2)}% |`,
    `| p50 latency | ${fmt(o.p50, 2, ' ms')} | Median |`,
    `| p90 latency | ${fmt(o.p90, 2, ' ms')} | Tail trend |`,
    `| p95 latency | ${fmt(o.p95, 2, ' ms')} | < ${o.targetP95Ms.toFixed(0)} ms |`,
    `| p99 latency | ${fmt(o.p99, 2, ' ms')} | Extreme tail |`,
    `| Average latency | ${fmt(o.avg, 2, ' ms')} | Informational |`,
    `| Min / max latency | ${fmt(o.min, 2, ' ms')} / ${fmt(o.max, 2, ' ms')} | Range |`,
    `| HTTP 429 responses | ${fmtInt(o.http429)} | 0 |`,
    `| Correctness checks | ${fmtPct(o.checksRate)} | > 99% |`,
    `| Iterations / dropped | ${fmtInt(o.iterations)} / ${fmtInt(o.droppedIterations)} | Scheduler health |`,
    `| Max active VUs | ${fmtInt(o.maxVUs)} | Workload context |`,
    '',
    '## Runtime threshold configuration',
    '',
    `- **Overall ${o.profile} p95:** ${o.targetP95Ms.toFixed(0)} ms — source: ${thresholdSourceText(o)}.`,
    `- **Correctness gates:** business failure rate < ${(o.maxFailureRate * 100).toFixed(2)}%, checks > 99%, HTTP 429 = 0.`,
    ...(configuredEndpointGates.length
      ? configuredEndpointGates.map(e => `- **${e.label} p95:** ${e.targetP95Ms.toFixed(0)} ms — source: \`${e.thresholdSource}\`.`)
      : ['- **Endpoint-specific p95 gates:** none configured for operations measured in this run.']),
    '',
    '## HTTP outcome distribution',
    '',
    '| Outcome | Count |', '|---|---:|',
    `| 2xx | ${fmtInt(o.http2xx)} |`,
    `| 3xx | ${fmtInt(o.http3xx)} |`,
    `| 4xx | ${fmtInt(o.http4xx)} |`,
    `| 5xx | ${fmtInt(o.http5xx)} |`,
    `| Network errors (status 0) | ${fmtInt(o.networkErrors)} |`,
    `| 429 (subset of 4xx) | ${fmtInt(o.http429)} |`,
    '',
    '## Endpoint performance',
    '',
    '| Operation | Method | Requests | Failure | Avg | p50 | p90 | p95 | p99 | Max | Target p95 | Gate | 4xx | 5xx | 429 |',
    '|---|:---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|:---:|---:|---:|---:|',
    ...endpoints.map(e => `| ${e.label} | ${e.method} | ${e.requests} | ${fmtPct(e.failureRate)} | ${fmt(e.avg, 1)} | ${fmt(e.p50, 1)} | ${fmt(e.p90, 1)} | ${fmt(e.p95, 1)} | ${fmt(e.p99, 1)} | ${fmt(e.max, 1)} | ${e.targetP95Ms == null ? '—' : fmt(e.targetP95Ms, 0)} | ${gateText(e)} | ${e.http4xx} | ${e.http5xx} | ${e.http429} |`),
    '',
    '## Slowest endpoints by p95',
    '',
    ...(slowest.length ? slowest.map((e, i) => `${i + 1}. **${e.label}** — p95 ${fmt(e.p95, 2, ' ms')}, p99 ${fmt(e.p99, 2, ' ms')}, failures ${fmtPct(e.failureRate)}`) : ['No measured endpoint data.']),
    '',
    '## Jira issues diagnostic',
    '',
    ...(jiraIssues
      ? [`Jira issues recorded **${jiraIssues.requests}** measured request(s): p50 ${fmt(jiraIssues.p50, 2, ' ms')}, p90 ${fmt(jiraIssues.p90, 2, ' ms')}, p95 ${fmt(jiraIssues.p95, 2, ' ms')}, p99 ${fmt(jiraIssues.p99, 2, ' ms')}. Endpoint gate: **${gateText(jiraIssues)}**${jiraIssues.targetP95Ms == null ? ' (configure it after baseline runs if desired).' : ` against ${jiraIssues.targetP95Ms.toFixed(0)} ms.`}`]
      : ['Jira issues was not measured in this run (integration routes disabled or profile does not use it).']),
    '',
    '## Failed / unhealthy endpoints',
    '',
    ...(failedEndpoints.length ? failedEndpoints.map(e => `- **${e.label}** — failure ${fmtPct(e.failureRate)}, 4xx=${e.http4xx}, 5xx=${e.http5xx}, network=${e.networkErrors}`) : ['No endpoint recorded a business failure, 4xx, 5xx, or network error.']),
    '',
    '## Threshold evaluation',
    '',
    '| Metric | Rule | Result |', '|---|---|:---:|',
    ...thresholds.map(t => `| \`${t.metric}\` | \`${t.rule}\` | ${t.passed ? 'PASS ✅' : 'FAIL ❌'} |`),
    '',
    '## Interpretation notes',
    '',
    '- Business latency excludes login and warm-up requests; endpoint tables are based on explicit ResearchTrack business-operation metrics.',
    '- Profile p95 thresholds are runtime configuration. Change the corresponding GitHub Actions variable after baseline review; no source-code push is required.',
    '- Optional endpoint p95 gates come from `K6_ENDPOINT_P95_THRESHOLDS_JSON`; this is useful for the observed Jira Issues tail without forcing every endpoint to share the same target.',
    '- Warm-up requests execute before measured traffic to reduce cold-start/JIT/connection-pool distortion. They still exercise the deployed system but are not included in the custom business gate.',
    '- A threshold failure is a valid test result; reports are generated and Azure/Gateway restoration still runs.',
    ...(o.executionMode === 'automatic-post-deploy'
      ? ['- This Smoke run was invoked automatically after the Production deployment job succeeded in the same workflow. The source SHA identifies that deployment workflow revision; impacted-service image selection may still preserve unchanged service images from earlier revisions.']
      : ['- Manual results measure the current deployment. The GitHub source SHA is traceability metadata, not proof that every deployed service image was built from that SHA.']),
  ];
  if (o.sourceRunId || o.sourceSha) {
    lines.splice(10, 0, `GitHub workflow: ${o.sourceWorkflow || 'n/a'} | run ${o.sourceRunId || 'n/a'} attempt ${o.sourceRunAttempt || 'n/a'} | source SHA: \`${o.sourceSha || 'n/a'}\``);
  }
  return lines.join('\n') + '\n';
}

function htmlReport(o, endpoints, thresholds) {
  const configuredEndpointGates = endpoints.filter(e => e.targetP95Ms != null);
  const jiraIssues = endpoints.find(e => e.operation === 'jira_issues');
  const endpointHtml = endpoints.map(e => {
    const gate = gateText(e);
    const gateClass = gate === 'PASS' ? 'pass' : gate === 'FAIL' ? 'fail' : 'neutral';
    return `
    <tr class="${e.failureRate > 0 ? 'bad' : ''}">
      <td><strong>${escapeHtml(e.label)}</strong><br><small>${escapeHtml(e.category)}</small></td>
      <td>${escapeHtml(e.method)}</td><td>${e.requests}</td><td>${fmtPct(e.failureRate)}</td>
      <td>${fmt(e.avg, 1)}</td><td>${fmt(e.p50, 1)}</td><td>${fmt(e.p90, 1)}</td>
      <td class="${e.targetP95Ms != null && e.thresholdPassed === false ? 'warn' : ''}">${fmt(e.p95, 1)}</td>
      <td>${fmt(e.p99, 1)}</td><td>${fmt(e.max, 1)}</td>
      <td>${e.targetP95Ms == null ? '—' : fmt(e.targetP95Ms, 0)}</td><td><span class="pill ${gateClass}">${gate}</span></td>
      <td>${e.http4xx}</td><td>${e.http5xx}</td><td>${e.http429}</td>
    </tr>`;
  }).join('');
  const thresholdHtml = thresholds.map(t => `<tr><td><code>${escapeHtml(t.metric)}</code></td><td><code>${escapeHtml(t.rule)}</code></td><td><span class="pill ${t.passed ? 'pass' : 'fail'}">${t.passed ? 'PASS' : 'FAIL'}</span></td></tr>`).join('');
  const slowest = endpoints.slice(0, 5).map(e => `<li><strong>${escapeHtml(e.label)}</strong>: p95 ${fmt(e.p95, 1, ' ms')} · p99 ${fmt(e.p99, 1, ' ms')} · failure ${fmtPct(e.failureRate)}</li>`).join('');
  const endpointGateHtml = configuredEndpointGates.length
    ? `<ul>${configuredEndpointGates.map(e => `<li><strong>${escapeHtml(e.label)}</strong>: ${fmt(e.targetP95Ms, 0, ' ms')} <code>${escapeHtml(e.thresholdSource)}</code></li>`).join('')}</ul>`
    : '<p class="muted">No endpoint-specific p95 gates configured for operations measured in this run.</p>';
  const jiraHtml = jiraIssues
    ? `<p><strong>${jiraIssues.requests} measured request(s)</strong> · p50 ${fmt(jiraIssues.p50, 1, ' ms')} · p90 ${fmt(jiraIssues.p90, 1, ' ms')} · p95 ${fmt(jiraIssues.p95, 1, ' ms')} · p99 ${fmt(jiraIssues.p99, 1, ' ms')} · endpoint gate <strong>${escapeHtml(gateText(jiraIssues))}</strong>${jiraIssues.targetP95Ms == null ? ' (not yet configured)' : ` against ${fmt(jiraIssues.targetP95Ms, 0, ' ms')}`}.</p>`
    : '<p class="muted">Jira issues was not measured in this run.</p>';
  return `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>ResearchTrack k6 ${escapeHtml(o.profile)} report</title>
<style>
:root{font-family:Inter,ui-sans-serif,system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;color:#1f2937;background:#f8fafc}body{margin:0;padding:32px}.wrap{max-width:1320px;margin:auto}.hero,.card{background:white;border:1px solid #e5e7eb;border-radius:14px;padding:22px;margin:0 0 18px;box-shadow:0 1px 2px rgba(0,0,0,.04)}h1{margin:0 0 8px;font-size:30px}h2{font-size:19px;margin:0 0 14px}.muted{color:#64748b}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px}.metric{border:1px solid #e5e7eb;border-radius:10px;padding:14px}.metric b{display:block;font-size:22px;margin-top:4px}.pill{display:inline-block;padding:5px 10px;border-radius:999px;font-weight:700;font-size:12px}.pass{background:#dcfce7;color:#166534}.fail{background:#fee2e2;color:#991b1b}.neutral{background:#e2e8f0;color:#475569}.warn{background:#fff7ed;color:#9a3412;font-weight:700}table{width:100%;border-collapse:collapse;font-size:13px}th,td{padding:9px 10px;border-bottom:1px solid #e5e7eb;text-align:right;white-space:nowrap}th:first-child,td:first-child{text-align:left}th{background:#f8fafc;position:sticky;top:0}.table{overflow:auto}.bad{background:#fff7f7}code{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:12px}.footer{font-size:12px;color:#64748b;line-height:1.6}@media print{body{padding:0;background:white}.hero,.card{box-shadow:none;break-inside:avoid}th{position:static}}
</style></head><body><div class="wrap">
<section class="hero"><span class="pill ${o.passed ? 'pass' : 'fail'}">${o.passed ? 'PASS' : 'FAIL'}</span><h1>ResearchTrack k6 — ${escapeHtml(o.profile)}</h1><p class="muted">${escapeHtml(o.purpose)} · ${escapeHtml(o.generatedAtUtc)}</p><p><strong>Workload:</strong> ${escapeHtml(o.workload)}<br><strong>Target:</strong> ${escapeHtml(o.baseUrl)} · <strong>Project:</strong> ${escapeHtml(o.projectId)}<br><strong>Execution:</strong> ${escapeHtml(o.executionMode)} · <strong>Warm-up:</strong> ${o.warmupPasses} pass(es) · <strong>Integrations:</strong> ${o.includeIntegrations} · <strong>Gateway exemption:</strong> ${o.gatewayBypassEnabled}</p></section>
<section class="card"><h2>Executive metrics</h2><div class="grid">
<div class="metric">Business requests<b>${fmtInt(o.businessRequests)}</b></div>
<div class="metric">Throughput<b>${fmt(o.businessThroughput,2,' req/s')}</b></div>
<div class="metric">Failure rate<b>${fmtPct(o.businessFailureRate)}</b></div>
<div class="metric">p50<b>${fmt(o.p50,1,' ms')}</b></div>
<div class="metric">p90<b>${fmt(o.p90,1,' ms')}</b></div>
<div class="metric ${o.p95 != null && o.p95 > o.targetP95Ms ? 'warn' : ''}">p95 / target<b>${fmt(o.p95,1,' ms')} / ${o.targetP95Ms.toFixed(0)} ms</b></div>
<div class="metric">p99<b>${fmt(o.p99,1,' ms')}</b></div>
<div class="metric">HTTP 429<b>${fmtInt(o.http429)}</b></div>
</div></section>
<section class="card"><h2>Runtime threshold configuration</h2><p><strong>Overall ${escapeHtml(o.profile)} p95:</strong> ${o.targetP95Ms.toFixed(0)} ms · source: ${escapeHtml(thresholdSourceText(o))}</p><p><strong>Correctness gates:</strong> failure rate &lt; ${(o.maxFailureRate * 100).toFixed(2)}%, checks &gt; 99%, HTTP 429 = 0.</p>${endpointGateHtml}</section>
<section class="card"><h2>Endpoint performance</h2><div class="table"><table><thead><tr><th>Operation</th><th>Method</th><th>Req</th><th>Fail</th><th>Avg</th><th>p50</th><th>p90</th><th>p95</th><th>p99</th><th>Max</th><th>Target p95</th><th>Gate</th><th>4xx</th><th>5xx</th><th>429</th></tr></thead><tbody>${endpointHtml || '<tr><td colspan="15">No endpoint metrics.</td></tr>'}</tbody></table></div><p class="muted">Latency columns are milliseconds. Endpoint gates appear only when configured through the runtime endpoint threshold map.</p></section>
<section class="card"><h2>Jira issues diagnostic</h2>${jiraHtml}</section>
<section class="card"><h2>Slowest endpoints by p95</h2><ol>${slowest || '<li>No measured endpoint data.</li>'}</ol></section>
<section class="card"><h2>HTTP outcomes</h2><div class="grid"><div class="metric">2xx<b>${o.http2xx}</b></div><div class="metric">3xx<b>${o.http3xx}</b></div><div class="metric">4xx<b>${o.http4xx}</b></div><div class="metric">5xx<b>${o.http5xx}</b></div><div class="metric">Network errors<b>${o.networkErrors}</b></div><div class="metric">Dropped iterations<b>${o.droppedIterations}</b></div></div></section>
<section class="card"><h2>Threshold evaluation</h2><div class="table"><table><thead><tr><th>Metric</th><th>Rule</th><th>Result</th></tr></thead><tbody>${thresholdHtml}</tbody></table></div></section>
<section class="card footer"><h2>Methodology notes</h2><p>Business metrics exclude authentication and warm-up traffic. Profile p95 thresholds come from runtime GitHub variables when configured, otherwise the documented fallback is used. Optional endpoint-specific p95 gates come from <code>K6_ENDPOINT_P95_THRESHOLDS_JSON</code>, allowing Jira Issues to have a justified target without weakening fast core routes.</p><p>Threshold failures remain valid evidence: reports are written before k6 exits non-zero, and the Azure/Gateway restoration path still runs.</p><p>GitHub workflow: ${escapeHtml(o.sourceWorkflow || 'n/a')} · run ${escapeHtml(o.sourceRunId || 'n/a')} · attempt ${escapeHtml(o.sourceRunAttempt || 'n/a')} · source SHA ${escapeHtml(o.sourceSha || 'n/a')}.</p></section>
</div></body></html>`;
}

function endpointCsv(endpoints) {
  const header = ['operation','label','category','method','requests','failure_rate','success_rate','avg_ms','p50_ms','p90_ms','p95_ms','p99_ms','max_ms','target_p95_ms','threshold_source','threshold_gate','http_2xx','http_3xx','http_4xx','http_5xx','http_429','network_errors'];
  return [header.map(csv).join(','), ...endpoints.map(e => [
    e.operation,e.label,e.category,e.method,e.requests,e.failureRate,e.successRate,
    e.avg,e.p50,e.p90,e.p95,e.p99,e.max,e.targetP95Ms,e.thresholdSource,gateText(e),
    e.http2xx,e.http3xx,e.http4xx,e.http5xx,e.http429,e.networkErrors,
  ].map(csv).join(','))].join('\n') + '\n';
}

export function reportSummary(data) {
  const profile = profileMetadata(PROFILE);
  const endpoints = endpointRows(data, profile.endpointThresholds);
  const thresholds = thresholdRows(data);
  const o = overview(data, endpoints, thresholds, profile);
  const markdown = markdownReport(o, endpoints, thresholds);
  const html = htmlReport(o, endpoints, thresholds);
  const detailed = {
    schemaVersion: 3,
    overview: o,
    thresholdConfiguration: {
      overallP95Ms: profile.p95Ms,
      overallSource: profile.p95ThresholdSource,
      overallSourceKey: profile.p95ThresholdSourceKey,
      endpointP95: profile.endpointThresholds,
      correctness: {
        maxFailureRate: profile.maxFailureRate,
        minimumCheckRateExclusive: 0.99,
        maxHttp429: 0,
      },
    },
    thresholds,
    endpoints,
    rawMetrics: data.metrics || {},
  };
  return {
    stdout: `${markdown}\n`,
    [`${REPORT_DIR}/${PROFILE}-summary.json`]: JSON.stringify(detailed, null, 2),
    [`${REPORT_DIR}/${PROFILE}-report.md`]: markdown,
    [`${REPORT_DIR}/${PROFILE}-report.html`]: html,
    [`${REPORT_DIR}/${PROFILE}-endpoints.csv`]: endpointCsv(endpoints),
  };
}
