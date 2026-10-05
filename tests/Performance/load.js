import { requireBaseUrl, requestTarget, standardThresholds, thresholdMs } from './lib/common.js';

requireBaseUrl();

export const options = {
  stages: [
    { duration: __ENV.K6_LOAD_RAMP_UP || '45s', target: Number(__ENV.K6_LOAD_VUS || 20) },
    { duration: __ENV.K6_LOAD_HOLD || '2m', target: Number(__ENV.K6_LOAD_VUS || 20) },
    { duration: __ENV.K6_LOAD_RAMP_DOWN || '30s', target: 0 },
  ],
  thresholds: standardThresholds(thresholdMs('K6_LOAD_P95_MS', 750), 0.02),
  tags: { profile: 'load' },
};

export default function () {
  requestTarget({ profile: 'load' });
}
