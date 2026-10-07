import { check } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';
import { OPERATION_KEYS, operationMetricKey } from './operations.js';

export const businessDuration = new Trend('researchtrack_business_duration', true);
export const businessFailures = new Rate('researchtrack_business_failures');
export const limitedResponses = new Counter('researchtrack_http_429');
export const businessRequests = new Counter('researchtrack_business_requests');
export const business2xx = new Counter('researchtrack_business_http_2xx');
export const business3xx = new Counter('researchtrack_business_http_3xx');
export const business4xx = new Counter('researchtrack_business_http_4xx');
export const business5xx = new Counter('researchtrack_business_http_5xx');
export const businessNetworkErrors = new Counter('researchtrack_business_network_errors');

// k6 custom metrics must be created in the init context.  Pre-creating a
// stable metric family for every known operation gives handleSummary()
// endpoint-level percentiles and status/error counts without external tooling.
export const operationMetrics = Object.fromEntries(OPERATION_KEYS.map((key) => [key, {
  duration: new Trend(`researchtrack_op_${key}_duration`, true),
  failures: new Rate(`researchtrack_op_${key}_failures`),
  requests: new Counter(`researchtrack_op_${key}_requests`),
  http2xx: new Counter(`researchtrack_op_${key}_http_2xx`),
  http3xx: new Counter(`researchtrack_op_${key}_http_3xx`),
  http4xx: new Counter(`researchtrack_op_${key}_http_4xx`),
  http5xx: new Counter(`researchtrack_op_${key}_http_5xx`),
  http429: new Counter(`researchtrack_op_${key}_http_429`),
  networkErrors: new Counter(`researchtrack_op_${key}_network_errors`),
}]));

export function responseIsOkay(response, expected = 200, envelope = true) {
  let validEnvelope = true;
  if (envelope && (expected === 200 || expected === 201)) {
    try { validEnvelope = response.json('success') === true; }
    catch (_) { validEnvelope = false; }
  }
  return response.status === expected && validEnvelope;
}

function recordStatusCounters(response, counters) {
  const status = Number(response.status || 0);
  counters.http2xx.add(status >= 200 && status < 300 ? 1 : 0);
  counters.http3xx.add(status >= 300 && status < 400 ? 1 : 0);
  counters.http4xx.add(status >= 400 && status < 500 ? 1 : 0);
  counters.http5xx.add(status >= 500 && status < 600 ? 1 : 0);
  counters.http429.add(status === 429 ? 1 : 0);
  counters.networkErrors.add(status === 0 ? 1 : 0);
}

export function record(response, name, expected = 200, envelope = true) {
  // Both HTTP and response-envelope correctness are required.
  const okay = responseIsOkay(response, expected, envelope);
  const operation = operationMetricKey(name);
  const op = operationMetrics[operation];

  check(response, { [`${name}: status and response envelope`]: () => okay }, { operation: name });
  businessDuration.add(response.timings.duration, { operation: name });
  businessRequests.add(1, { operation: name });
  businessFailures.add(!okay, { operation: name });
  limitedResponses.add(response.status === 429 ? 1 : 0, { operation: name });

  const status = Number(response.status || 0);
  business2xx.add(status >= 200 && status < 300 ? 1 : 0);
  business3xx.add(status >= 300 && status < 400 ? 1 : 0);
  business4xx.add(status >= 400 && status < 500 ? 1 : 0);
  business5xx.add(status >= 500 && status < 600 ? 1 : 0);
  businessNetworkErrors.add(status === 0 ? 1 : 0);

  op.duration.add(response.timings.duration, { operation: name });
  op.requests.add(1, { operation: name });
  op.failures.add(!okay, { operation: name });
  recordStatusCounters(response, op);
  return okay;
}
