const duration = (name, threshold, errorRate) => ({
  // Custom metrics exclude authentication/setup requests from latency gates.
  thresholds: {
    researchtrack_business_failures: [`rate<${errorRate}`],
    researchtrack_business_duration: [`p(95)<${threshold}`],
    researchtrack_http_429: ['count==0'],
    checks: ['rate>0.99'],
  },
  tags: { suite: 'researchtrack', profile: name },
  discardResponseBodies: false, // Response envelopes are checked for validity.
  noConnectionReuse: false,
});

export function profileOptions(profile) {
  switch (profile) {
    case 'smoke':
      return {
        ...duration(profile, Number(__ENV.K6_P95_MS || 1500), 0.01),
        scenarios: { smoke: { executor: 'constant-vus', vus: 2, duration: '20s' } },
      };
    case 'load':
      return {
        ...duration(profile, Number(__ENV.K6_P95_MS || 1800), 0.02),
        scenarios: { load: {
          executor: 'ramping-arrival-rate', startRate: 2, timeUnit: '1s',
          preAllocatedVUs: 25, maxVUs: 45,
          stages: [{ target: 4, duration: '30s' }, { target: 10, duration: '45s' },
                   { target: 10, duration: '45s' }, { target: 0, duration: '15s' }],
        } },
      };
    case 'stress':
      return {
        ...duration(profile, Number(__ENV.K6_P95_MS || 2500), 0.05),
        scenarios: { stress: {
          executor: 'ramping-arrival-rate', startRate: 5, timeUnit: '1s',
          preAllocatedVUs: 40, maxVUs: 80,
          stages: [{ target: 10, duration: '30s' }, { target: 25, duration: '45s' },
                   { target: 40, duration: '45s' }, { target: 0, duration: '15s' }],
        } },
      };
    case 'spike':
      return {
        ...duration(profile, Number(__ENV.K6_P95_MS || 2500), 0.05),
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
        ...duration(profile, Number(__ENV.K6_P95_MS || 2500), 0.02),
        scenarios: { write: { executor: 'constant-arrival-rate', rate: 1,
          timeUnit: '3s', duration: '30s', preAllocatedVUs: 2, maxVUs: 4 } },
      };
    case 'lifecycle':
      return {
        ...duration(profile, Number(__ENV.K6_P95_MS || 8000), 0.01),
        scenarios: { lifecycle: { executor: 'per-vu-iterations', vus: 1,
          iterations: 1, maxDuration: '3m' } },
      };
    case 'webhook':
      return {
        ...duration(profile, Number(__ENV.K6_P95_MS || 2500), 0.02),
        scenarios: { webhook: { executor: 'ramping-arrival-rate',
          startRate: 1, timeUnit: '1s', preAllocatedVUs: 12, maxVUs: 24,
          stages: [{ target: 2, duration: '10s' }, { target: 12, duration: '10s' },
                   { target: 12, duration: '20s' }, { target: 0, duration: '10s' }] } },
      };
    default:
      throw new Error(`Unknown K6_PROFILE=${profile}`);
  }
}
