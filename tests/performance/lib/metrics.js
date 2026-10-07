import { check } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';

export const businessDuration = new Trend('researchtrack_business_duration', true);
export const businessFailures = new Rate('researchtrack_business_failures');
export const limitedResponses = new Counter('researchtrack_http_429');
export const businessRequests = new Counter('researchtrack_business_requests');

export function record(response, name, expected = 200, envelope = true) {
  // Both HTTP and response-envelope correctness are required.
  let validEnvelope = true;
  if (envelope && (expected === 200 || expected === 201)) {
    try { validEnvelope = response.json('success') === true; }
    catch (_) { validEnvelope = false; }
  }
  const okay = response.status === expected && validEnvelope;
  check(response, { [`${name}: status and response envelope`]: () => okay }, { operation: name });
  businessDuration.add(response.timings.duration, { operation: name });
  businessRequests.add(1, { operation: name });
  businessFailures.add(!okay, { operation: name });
  limitedResponses.add(response.status === 429 ? 1 : 0, { operation: name });
  return okay;
}
