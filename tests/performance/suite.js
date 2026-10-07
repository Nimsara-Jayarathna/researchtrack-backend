import http from 'k6/http';
import { sleep } from 'k6';
import { setupSessions, useSession } from './lib/auth.js';
import { BASE_URL, PROJECT_ID, PROFILE, INCLUDE_INTEGRATIONS, headers } from './lib/settings.js';
import { profileOptions } from './lib/profiles.js';
import { record } from './lib/metrics.js';
import { reportSummary } from './lib/report.js';

export const options = profileOptions(PROFILE);
export const setup = setupSessions;
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

export default function (sessions) {
  const role = (__VU % 2 === 0) ? 'supervisor' : 'student';
  useSession(sessions, role);
  const routes = [...common, ...(INCLUDE_INTEGRATIONS ? integrations : []),
    ...(role === 'supervisor' ? [['supervisor_dashboard', '/api/v1/supervisor/dashboard']] : [])];
  // Different VUs and iterations sample a weighted mix of actual project API routes.
  const index = (__VU + __ITER) % routes.length;
  const [name, path] = routes[index];
  const response = http.get(`${BASE_URL}${path}`, {
    headers: headers(),
    tags: { operation: name, role },
    timeout: '20s',
  });
  record(response, name);
  if (PROFILE === 'smoke') sleep(1);
}
