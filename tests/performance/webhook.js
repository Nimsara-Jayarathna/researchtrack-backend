// Authenticated Jira webhook burst. Explicit manual opt-in only; each event
// targets the chosen dedicated Jira project and can schedule actual sync work.
import http from 'k6/http';
import { fail } from 'k6';
import { BASE_URL, headers } from './lib/settings.js';
import { profileOptions } from './lib/profiles.js';
import { record } from './lib/metrics.js';
import { reportSummary } from './lib/report.js';

export const options = profileOptions('webhook');
export function setup() {
  if (__ENV.K6_ENABLE_WEBHOOK !== 'true') fail('Webhook testing requires K6_ENABLE_WEBHOOK=true');
  for (const key of ['K6_JIRA_PROJECT_KEY', 'K6_JIRA_ISSUE_KEY', 'K6_JIRA_WEBHOOK_BEARER']) {
    if (!__ENV[key]) fail(`${key} is required for the webhook profile`);
  }
}
export default function () {
  const projectKey = __ENV.K6_JIRA_PROJECT_KEY;
  const event = {
    webhookEvent: 'jira:issue_updated',
    issue: { id: __ENV.K6_JIRA_ISSUE_ID || '10001',
      key: __ENV.K6_JIRA_ISSUE_KEY, fields: { project: { key: projectKey } } },
  };
  const response = http.post(`${BASE_URL}/api/v1/jira/webhooks`, JSON.stringify(event), {
    headers: headers({
      'Content-Type': 'application/json',
      Authorization: `Bearer ${__ENV.K6_JIRA_WEBHOOK_BEARER}`,
      'X-Atlassian-Webhook-Identifier': `se3112-${Date.now()}-${__VU}-${__ITER}`,
    }),
    tags: { operation: 'jira_webhook' }, timeout: '20s',
  });
  // Jira webhook endpoint returns bare HTTP 200 rather than the API envelope.
  record(response, 'jira_webhook', 200, false);
}
export const handleSummary = reportSummary;
