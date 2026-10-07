// Bounded database-write test: creates then deletes its own submission requirement.
// Requires a dedicated research project and an assigned test student.
import http from 'k6/http';
import { sleep, fail } from 'k6';
import { setupSessions, useSession } from './lib/auth.js';
import { BASE_URL, PROJECT_ID, headers } from './lib/settings.js';
import { profileOptions } from './lib/profiles.js';
import { record } from './lib/metrics.js';
import { reportSummary } from './lib/report.js';

export const options = profileOptions('write');
export function setup() {
  if (__ENV.K6_ENABLE_WRITES !== 'true') fail('Write test requires K6_ENABLE_WRITES=true');
  if (!/^[0-9a-fA-F-]{36}$/.test(__ENV.K6_STUDENT_ID || ''))
    fail('Write test requires the dedicated project member K6_STUDENT_ID');
  return setupSessions();
}
export default function (sessions) {
  useSession(sessions, 'supervisor');
  const base = `${BASE_URL}/api/v1/projects/${PROJECT_ID}/submissions/requirements`;
  const unique = `${Date.now()}-vu${__VU}-it${__ITER}`;
  const request = {
    title: `SE3112 k6 ${unique}`,
    description: 'Ephemeral performance test requirement',
    allowedFileTypes: ['.pdf'], maxFileSizeBytes: 1048576,
    responsibilityMode: 'ASSIGNED_STUDENT', assignedStudentId: __ENV.K6_STUDENT_ID,
  };
  const created = http.post(base, JSON.stringify(request), {
    headers: headers({ 'Content-Type': 'application/json' }),
    tags: { operation: 'requirement_create' }, timeout: '20s',
  });
  const createdOkay = record(created, 'requirement_create', 201);
  if (createdOkay) {
    // The service's ApiResponse<T> returns the created entity under data.
    const id = created.json('data.id');
    if (id) {
      const deleted = http.del(`${base}/${id}`, null, {
        headers: headers(), tags: { operation: 'requirement_delete' }, timeout: '20s',
      });
      record(deleted, 'requirement_delete', 204);
    } else {
      fail('Created requirement has no data.id; record may require manual cleanup');
    }
  }
  sleep(0.1);
}
export const handleSummary = reportSummary;
