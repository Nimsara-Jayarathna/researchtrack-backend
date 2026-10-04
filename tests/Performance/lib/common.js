import http from 'k6/http';
import { check, sleep } from 'k6';

export const BASE_URL = (__ENV.BASE_URL || '').replace(/\/$/, '');
export const TEST_PATH = __ENV.K6_TEST_PATH || '/health/ready';

export function requireBaseUrl() {
  if (!BASE_URL) {
    throw new Error('BASE_URL is required, for example https://api.researchtrack.example.com');
  }
}

export function requestTarget(tags = {}) {
  const response = http.get(`${BASE_URL}${TEST_PATH}`, {
    tags: { endpoint: TEST_PATH, ...tags },
    timeout: __ENV.K6_REQUEST_TIMEOUT || '10s',
  });

  check(response, {
    'status is 2xx': (r) => r.status >= 200 && r.status < 300,
  });

  sleep(Number(__ENV.K6_SLEEP_SECONDS || 0.2));
}

export function thresholdMs(name, fallback) {
  const value = Number(__ENV[name] || fallback);
  if (!Number.isFinite(value) || value <= 0) {
    throw new Error(`${name} must be a positive number.`);
  }
  return value;
}

export function standardThresholds(p95Ms, failureRate = 0.01) {
  return {
    http_req_failed: [`rate<${failureRate}`],
    http_req_duration: [`p(95)<${p95Ms}`],
    checks: ['rate>0.99'],
  };
}
