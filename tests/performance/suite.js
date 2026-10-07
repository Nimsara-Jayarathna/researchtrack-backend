import http from 'k6/http';
import { sleep, fail } from 'k6';
import { setupSessions, useSession } from './lib/auth.js';
import {
  BASE_URL, PROJECT_ID, PROFILE, INCLUDE_INTEGRATIONS,
  WARMUP_PASSES, headers,
} from './lib/settings.js';
import { profileOptions } from './lib/profiles.js';
import { record, responseIsOkay } from './lib/metrics.js';
import { reportSummary } from './lib/report.js';

export const options = profileOptions(PROFILE);
export const handleSummary = reportSummary;

const common = [
  ['projects', '/api/v1/projects'],
  ['project_details', `/api/v1/projects/${PROJECT_ID}`],
  ['requirements', `/api/v1/projects/${PROJECT_ID}/submissions/requirements`],
  ['submissions', `/api/v1/projects/${PROJECT_ID}/submissions`],
];
const integrations = [
  ['jira_issues', `/api/v1/projects/${PROJECT_ID}/jira/issues`],
  ['jira_sprint_progress', `/api/v1/projects/${PROJECT_ID}/jira/sprint-progress`],
  ['github_activity', `/api/v1/projects/${PROJECT_ID}/github/activity?page=1&size=10`],
];

function routesFor(role) {
  return [
    ...common,
    ...(INCLUDE_INTEGRATIONS ? integrations : []),
    ...(role === 'supervisor' ? [['supervisor_dashboard', '/api/v1/supervisor/dashboard']] : []),
  ];
}

function warmUp(sessions) {
  if (WARMUP_PASSES <= 0) return;
  console.log(`ResearchTrack warm-up: ${WARMUP_PASSES} pass(es), integrations=${INCLUDE_INTEGRATIONS}`);
  for (let pass = 1; pass <= WARMUP_PASSES; pass += 1) {
    for (const role of ['supervisor', 'student']) {
      useSession(sessions, role);
      for (const [name, path] of routesFor(role)) {
        const response = http.get(`${BASE_URL}${path}`, {
          headers: headers(),
          tags: { operation: name, role, phase: 'warmup' },
          timeout: '20s',
        });
        // Warm-up traffic is deliberately excluded from custom business metrics,
        // but a broken endpoint should stop the run before measured traffic begins.
        if (!responseIsOkay(response, 200, true)) {
          fail(`Warm-up failed: ${role}/${name} returned status=${response.status}`);
        }
      }
    }
  }
}

export function setup() {
  const sessions = setupSessions();
  warmUp(sessions);
  return sessions;
}

export default function (sessions) {
  const role = (__VU % 2 === 0) ? 'supervisor' : 'student';
  useSession(sessions, role);
  const routes = routesFor(role);
  // Different VUs and iterations sample a deterministic mix of actual project API routes.
  const index = (__VU + __ITER) % routes.length;
  const [name, path] = routes[index];
  const response = http.get(`${BASE_URL}${path}`, {
    headers: headers(),
    tags: { operation: name, role, phase: 'measured' },
    timeout: '20s',
  });
  record(response, name);
  if (PROFILE === 'smoke') sleep(1);
}
