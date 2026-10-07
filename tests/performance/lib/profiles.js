const p95Override = (fallback) => Number(__ENV.K6_P95_MS || fallback);

export function profileMetadata(profile) {
  switch (profile) {
    case 'smoke': return {
      profile, purpose: 'Fast authenticated deployment-health and latency check',
      p95Ms: p95Override(1500), maxFailureRate: 0.01,
      workload: '2 looping VUs for 20 seconds',
    };
    case 'load': return {
      profile, purpose: 'Expected concurrent read workload and steady-state capacity',
      p95Ms: p95Override(1800), maxFailureRate: 0.02,
      workload: 'Ramping arrival rate 2→4→10 req/s, then cooldown (135 seconds)',
    };
    case 'stress': return {
      profile, purpose: 'Increasing demand to expose saturation and latency degradation',
      p95Ms: p95Override(2500), maxFailureRate: 0.05,
      workload: 'Ramping arrival rate 5→10→25→40 req/s, then cooldown (135 seconds)',
    };
    case 'spike': return {
      profile, purpose: 'Sudden traffic burst and recovery behaviour',
      p95Ms: p95Override(2500), maxFailureRate: 0.05,
      workload: 'Arrival-rate spike 2→40→2 req/s (55 seconds)',
    };
    case 'write': return {
      profile, purpose: 'Bounded create/delete submission-requirement writes',
      p95Ms: p95Override(2500), maxFailureRate: 0.02,
      workload: '1 iteration every 3 seconds for 30 seconds',
    };
    case 'lifecycle': return {
      profile, purpose: 'Full student/supervisor submission version lifecycle',
      p95Ms: p95Override(8000), maxFailureRate: 0.01,
      workload: '1 VU × 1 complete lifecycle, max 3 minutes',
    };
    case 'webhook': return {
      profile, purpose: 'Signed Jira webhook burst and ingestion capacity',
      p95Ms: p95Override(2500), maxFailureRate: 0.02,
      workload: 'Ramping signed webhook arrival rate 1→2→12 req/s (50 seconds)',
    };
    default: throw new Error(`Unknown K6_PROFILE=${profile}`);
  }
}

const duration = (metadata) => ({
  // Custom metrics exclude authentication/setup/warm-up requests from latency gates.
  thresholds: {
    researchtrack_business_failures: [`rate<${metadata.maxFailureRate}`],
    researchtrack_business_duration: [`p(95)<${metadata.p95Ms}`],
    researchtrack_http_429: ['count==0'],
    checks: ['rate>0.99'],
  },
  tags: { suite: 'researchtrack', profile: metadata.profile },
  discardResponseBodies: false, // Response envelopes are checked for validity.
  noConnectionReuse: false,
  // Explicit trend stats make p99/p90 available to handleSummary() and artifacts.
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  summaryTimeUnit: 'ms',
});

export function profileOptions(profile) {
  const metadata = profileMetadata(profile);
  switch (profile) {
    case 'smoke':
      return {
        ...duration(metadata),
        scenarios: { smoke: { executor: 'constant-vus', vus: 2, duration: '20s' } },
      };
    case 'load':
      return {
        ...duration(metadata),
        scenarios: { load: {
          executor: 'ramping-arrival-rate', startRate: 2, timeUnit: '1s',
          preAllocatedVUs: 25, maxVUs: 45,
          stages: [{ target: 4, duration: '30s' }, { target: 10, duration: '45s' },
                   { target: 10, duration: '45s' }, { target: 0, duration: '15s' }],
        } },
      };
    case 'stress':
      return {
        ...duration(metadata),
        scenarios: { stress: {
          executor: 'ramping-arrival-rate', startRate: 5, timeUnit: '1s',
          preAllocatedVUs: 40, maxVUs: 80,
          stages: [{ target: 10, duration: '30s' }, { target: 25, duration: '45s' },
                   { target: 40, duration: '45s' }, { target: 0, duration: '15s' }],
        } },
      };
    case 'spike':
      return {
        ...duration(metadata),
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
        ...duration(metadata),
        scenarios: { write: { executor: 'constant-arrival-rate', rate: 1,
          timeUnit: '3s', duration: '30s', preAllocatedVUs: 2, maxVUs: 4 } },
      };
    case 'lifecycle':
      return {
        ...duration(metadata),
        scenarios: { lifecycle: { executor: 'per-vu-iterations', vus: 1,
          iterations: 1, maxDuration: '3m' } },
      };
    case 'webhook':
      return {
        ...duration(metadata),
        scenarios: { webhook: { executor: 'ramping-arrival-rate',
          startRate: 1, timeUnit: '1s', preAllocatedVUs: 12, maxVUs: 24,
          stages: [{ target: 2, duration: '10s' }, { target: 12, duration: '10s' },
                   { target: 12, duration: '20s' }, { target: 0, duration: '10s' }] } },
      };
    default:
      throw new Error(`Unknown K6_PROFILE=${profile}`);
  }
}
