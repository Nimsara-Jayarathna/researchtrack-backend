import { OPERATION_CATALOG } from './operations.js';

const PROFILE_CONFIG = Object.freeze({
  smoke: {
    purpose: 'Fast authenticated deployment-health and latency check',
    fallbackP95Ms: 1500,
    thresholdEnv: 'K6_SMOKE_P95_MS',
    maxFailureRate: 0.01,
    workload: '2 looping VUs for 45 seconds',
  },
  load: {
    purpose: 'Expected concurrent read workload and steady-state capacity',
    fallbackP95Ms: 1800,
    thresholdEnv: 'K6_LOAD_P95_MS',
    maxFailureRate: 0.02,
    workload: 'Ramping arrival rate 2→4→10 req/s, then cooldown (135 seconds)',
  },
  stress: {
    purpose: 'Increasing demand to expose saturation and latency degradation',
    fallbackP95Ms: 2500,
    thresholdEnv: 'K6_STRESS_P95_MS',
    maxFailureRate: 0.05,
    workload: 'Ramping arrival rate 5→10→25→40 req/s, then cooldown (135 seconds)',
  },
  spike: {
    purpose: 'Sudden traffic burst and recovery behaviour',
    fallbackP95Ms: 2500,
    thresholdEnv: 'K6_SPIKE_P95_MS',
    maxFailureRate: 0.05,
    workload: 'Arrival-rate spike 2→40→2 req/s (55 seconds)',
  },
  write: {
    purpose: 'Bounded create/delete submission-requirement writes',
    fallbackP95Ms: 2500,
    thresholdEnv: 'K6_WRITE_P95_MS',
    maxFailureRate: 0.02,
    workload: '1 iteration every 3 seconds for 30 seconds',
  },
  lifecycle: {
    purpose: 'Full student/supervisor submission version lifecycle',
    fallbackP95Ms: 8000,
    thresholdEnv: 'K6_LIFECYCLE_P95_MS',
    maxFailureRate: 0.01,
    workload: '1 VU × 1 complete lifecycle, max 3 minutes',
  },
  webhook: {
    purpose: 'Signed Jira webhook burst and ingestion capacity',
    fallbackP95Ms: 2500,
    thresholdEnv: 'K6_WEBHOOK_P95_MS',
    maxFailureRate: 0.02,
    workload: 'Ramping signed webhook arrival rate 1→2→12 req/s (50 seconds)',
  },
});

const READ_OPERATIONS = Object.freeze([
  'projects', 'project_details', 'requirements', 'submissions',
  'jira_issues', 'jira_sprint_progress', 'github_activity', 'supervisor_dashboard',
]);
const WRITE_OPERATIONS = Object.freeze(['requirement_create', 'requirement_delete']);
const LIFECYCLE_OPERATIONS = Object.freeze([
  'requirement_create', 'v1_session', 'v1_object_storage', 'v1_complete',
  'review_request_changes', 'v2_session', 'v2_object_storage', 'v2_complete',
  'review_approve', 'verify_version_history', 'archive_test_requirement',
]);
const WEBHOOK_OPERATIONS = Object.freeze(['jira_webhook']);
const INTEGRATION_OPERATIONS = new Set(['jira_issues', 'jira_sprint_progress', 'github_activity']);

function raw(name) {
  return String(__ENV[name] ?? '').trim();
}

function positiveNumber(name, value) {
  const parsed = Number(value);
  if (!Number.isFinite(parsed) || parsed <= 0) {
    throw new Error(`${name} must be a positive number of milliseconds`);
  }
  return parsed;
}

function activeOperations(profile) {
  if (['smoke', 'load', 'stress', 'spike'].includes(profile)) {
    const integrations = __ENV.K6_INCLUDE_INTEGRATIONS === 'true';
    return READ_OPERATIONS.filter(op => integrations || !INTEGRATION_OPERATIONS.has(op));
  }
  if (profile === 'write') return [...WRITE_OPERATIONS];
  if (profile === 'lifecycle') return [...LIFECYCLE_OPERATIONS];
  if (profile === 'webhook') return [...WEBHOOK_OPERATIONS];
  return [];
}

function resolveProfileP95(profile, config) {
  // K6_P95_MS is a deliberately explicit, one-run override supplied by the
  // manual workflow input. Normal CI/CD should use the profile-specific
  // GitHub Repository/Environment variable instead.
  const override = raw('K6_P95_MS');
  if (override) {
    return {
      value: positiveNumber('K6_P95_MS', override),
      source: 'one-run override',
      sourceKey: 'K6_P95_MS',
    };
  }
  const configured = raw(config.thresholdEnv);
  if (configured) {
    return {
      value: positiveNumber(config.thresholdEnv, configured),
      source: 'GitHub/runtime variable',
      sourceKey: config.thresholdEnv,
    };
  }
  return {
    value: config.fallbackP95Ms,
    source: 'built-in fallback',
    sourceKey: null,
  };
}

function parseEndpointThresholdMap() {
  const value = raw('K6_ENDPOINT_P95_THRESHOLDS_JSON');
  if (!value) return {};
  let parsed;
  try { parsed = JSON.parse(value); }
  catch (_) { throw new Error('K6_ENDPOINT_P95_THRESHOLDS_JSON must be valid JSON'); }
  if (!parsed || Array.isArray(parsed) || typeof parsed !== 'object') {
    throw new Error('K6_ENDPOINT_P95_THRESHOLDS_JSON must be a JSON object');
  }
  const result = {};
  for (const [operation, threshold] of Object.entries(parsed)) {
    if (!OPERATION_CATALOG[operation] || operation === 'unknown') {
      throw new Error(`Unknown endpoint threshold operation '${operation}'`);
    }
    result[operation] = positiveNumber(
      `K6_ENDPOINT_P95_THRESHOLDS_JSON.${operation}`,
      threshold,
    );
  }
  return result;
}

export function configuredOperationThresholds(profile) {
  const configured = parseEndpointThresholdMap();
  const active = new Set(activeOperations(profile));
  return Object.fromEntries(Object.entries(configured)
    .filter(([operation]) => active.has(operation))
    .map(([operation, p95Ms]) => [operation, {
      operation,
      p95Ms,
      source: 'GitHub/runtime endpoint map',
      sourceKey: `K6_ENDPOINT_P95_THRESHOLDS_JSON[${operation}]`,
    }]));
}

export function profileMetadata(profile) {
  const config = PROFILE_CONFIG[profile];
  if (!config) throw new Error(`Unknown K6_PROFILE=${profile}`);
  const threshold = resolveProfileP95(profile, config);
  return {
    profile,
    purpose: config.purpose,
    p95Ms: threshold.value,
    p95ThresholdSource: threshold.source,
    p95ThresholdSourceKey: threshold.sourceKey,
    maxFailureRate: config.maxFailureRate,
    workload: config.workload,
    endpointThresholds: configuredOperationThresholds(profile),
  };
}

const commonOptions = (metadata) => {
  const thresholds = {
    researchtrack_business_failures: [`rate<${metadata.maxFailureRate}`],
    researchtrack_business_duration: [`p(95)<${metadata.p95Ms}`],
    researchtrack_http_429: ['count==0'],
    checks: ['rate>0.99'],
  };
  for (const [operation, config] of Object.entries(metadata.endpointThresholds)) {
    thresholds[`researchtrack_op_${operation}_duration`] = [`p(95)<${config.p95Ms}`];
  }
  return {
    // Custom metrics exclude authentication/setup/warm-up requests from latency gates.
    thresholds,
    tags: { suite: 'researchtrack', profile: metadata.profile },
    discardResponseBodies: false, // Response envelopes are checked for validity.
    noConnectionReuse: false,
    // Explicit trend stats make p99/p90 available to handleSummary() and artifacts.
    summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
    summaryTimeUnit: 'ms',
  };
};

export function profileOptions(profile) {
  const metadata = profileMetadata(profile);
  switch (profile) {
    case 'smoke':
      return {
        ...commonOptions(metadata),
        scenarios: { smoke: { executor: 'constant-vus', vus: 2, duration: '45s' } },
      };
    case 'load':
      return {
        ...commonOptions(metadata),
        scenarios: { load: {
          executor: 'ramping-arrival-rate', startRate: 2, timeUnit: '1s',
          preAllocatedVUs: 25, maxVUs: 45,
          stages: [{ target: 4, duration: '30s' }, { target: 10, duration: '45s' },
                   { target: 10, duration: '45s' }, { target: 0, duration: '15s' }],
        } },
      };
    case 'stress':
      return {
        ...commonOptions(metadata),
        scenarios: { stress: {
          executor: 'ramping-arrival-rate', startRate: 5, timeUnit: '1s',
          preAllocatedVUs: 40, maxVUs: 80,
          stages: [{ target: 10, duration: '30s' }, { target: 25, duration: '45s' },
                   { target: 40, duration: '45s' }, { target: 0, duration: '15s' }],
        } },
      };
    case 'spike':
      return {
        ...commonOptions(metadata),
        scenarios: { spike: {
          executor: 'ramping-arrival-rate', startRate: 2, timeUnit: '1s',
          preAllocatedVUs: 40, maxVUs: 80,
          stages: [{ target: 2, duration: '15s' }, { target: 40, duration: '5s' },
                   { target: 40, duration: '20s' }, { target: 2, duration: '5s' },
                   { target: 0, duration: '15s' }],
        } },
      };
    case 'write':
      return {
        ...commonOptions(metadata),
        scenarios: { write: { executor: 'constant-arrival-rate', rate: 1,
          timeUnit: '3s', duration: '30s', preAllocatedVUs: 2, maxVUs: 4 } },
      };
    case 'lifecycle':
      return {
        ...commonOptions(metadata),
        scenarios: { lifecycle: { executor: 'per-vu-iterations', vus: 1,
          iterations: 1, maxDuration: '3m' } },
      };
    case 'webhook':
      return {
        ...commonOptions(metadata),
        scenarios: { webhook: { executor: 'ramping-arrival-rate',
          startRate: 1, timeUnit: '1s', preAllocatedVUs: 12, maxVUs: 24,
          stages: [{ target: 2, duration: '10s' }, { target: 12, duration: '10s' },
                   { target: 12, duration: '20s' }, { target: 0, duration: '10s' }] } },
      };
    default:
      throw new Error(`Unknown K6_PROFILE=${profile}`);
  }
}
