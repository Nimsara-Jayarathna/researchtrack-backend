import { requireBaseUrl, requestTarget, standardThresholds, thresholdMs } from './lib/common.js';

requireBaseUrl();

export const options = {
  vus: Number(__ENV.K6_SMOKE_VUS || 5),
  duration: __ENV.K6_SMOKE_DURATION || '15s',
  thresholds: standardThresholds(thresholdMs('K6_SMOKE_P95_MS', 500)),
  tags: { profile: 'smoke' },
};

export default function () {
  requestTarget({ profile: 'smoke' });
}
