import crypto from 'k6/crypto';
import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL, requireBaseUrl, standardThresholds, thresholdMs } from './lib/common.js';

requireBaseUrl();

const webhookSecret = __ENV.GITHUB_WEBHOOK_SECRET || '';
if (!webhookSecret) {
  throw new Error('GITHUB_WEBHOOK_SECRET is required for the signed webhook spike test.');
}

const deliveryId = __ENV.WEBHOOK_DELIVERY_ID || 'researchtrack-performance-probe';
const payload = JSON.stringify({
  zen: 'ResearchTrack signed performance probe',
  hook_id: 0,
  repository: { id: 0 },
});
const digest = crypto.hmac('sha256', webhookSecret, payload, 'hex');

export const options = {
  scenarios: {
    webhook_spike: {
      executor: 'constant-vus',
      vus: Number(__ENV.K6_WEBHOOK_VUS || 25),
      duration: __ENV.K6_WEBHOOK_DURATION || '20s',
    },
  },
  thresholds: standardThresholds(thresholdMs('K6_WEBHOOK_P95_MS', 1000), 0.03),
  tags: { profile: 'webhook-spike' },
};

export default function () {
  const response = http.post(`${BASE_URL}/api/v1/github/webhooks`, payload, {
    headers: {
      'Content-Type': 'application/json',
      'X-GitHub-Event': 'ping',
      'X-GitHub-Delivery': deliveryId,
      'X-Hub-Signature-256': `sha256=${digest}`,
    },
    timeout: __ENV.K6_REQUEST_TIMEOUT || '10s',
    tags: { endpoint: '/api/v1/github/webhooks', profile: 'webhook-spike' },
  });

  check(response, {
    'webhook accepted': (r) => r.status === 202,
  });
}
