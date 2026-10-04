import { requireBaseUrl, requestTarget, standardThresholds, thresholdMs } from './lib/common.js';

requireBaseUrl();

const peak = Number(__ENV.K6_STRESS_PEAK_VUS || 50);

export const options = {
  stages: [
    { duration: __ENV.K6_STRESS_STAGE_1 || '45s', target: Math.max(5, Math.floor(peak * 0.4)) },
    { duration: __ENV.K6_STRESS_STAGE_2 || '45s', target: Math.max(10, Math.floor(peak * 0.7)) },
    { duration: __ENV.K6_STRESS_STAGE_3 || '1m', target: peak },
    { duration: __ENV.K6_STRESS_HOLD || '1m', target: peak },
    { duration: __ENV.K6_STRESS_RAMP_DOWN || '45s', target: 0 },
  ],
  thresholds: standardThresholds(thresholdMs('K6_STRESS_P95_MS', 1200), 0.05),
  tags: { profile: 'stress' },
};

export default function () {
  requestTarget({ profile: 'stress' });
}
