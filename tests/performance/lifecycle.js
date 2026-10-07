// Full authenticated submission lifecycle: Supervisor creates a requirement;
// Student uploads a real sample ZIP via S3, submits v1; Supervisor requests
// changes; Student resubmits v2; Supervisor approves. State is intentionally
// retained as version-history evidence inside the DEDICATED project.
import http from 'k6/http';
import { check, fail } from 'k6';
import { setupSessions, useSession } from './lib/auth.js';
import { BASE_URL, PROJECT_ID, headers } from './lib/settings.js';
import { record } from './lib/metrics.js';
import { profileOptions } from './lib/profiles.js';
import { reportSummary } from './lib/report.js';

const fixture = open('./fixtures/se3112-sample.zip', 'b');
const size = fixture.byteLength;
const base = `${BASE_URL}/api/v1/projects/${PROJECT_ID}/submissions`;
const json = (body) => JSON.stringify(body);

export const options = profileOptions('lifecycle');
export function setup() {
  if (__ENV.K6_ENABLE_WRITES !== 'true') fail('Lifecycle needs K6_ENABLE_WRITES=true');
  if (!/^[0-9a-fA-F-]{36}$/.test(__ENV.K6_STUDENT_ID || ''))
    fail('K6_STUDENT_ID must be an active member of the dedicated project');
  return setupSessions();
}

function api(sessions, role, method, url, body, name, expected = 200) {
  useSession(sessions, role);
  const params = { headers: headers({ 'Content-Type': 'application/json' }),
    tags: { operation: name, role }, timeout: '30s' };
  let response;
  switch (method) {
    case 'GET': response = http.get(url, params); break;
    case 'POST': response = http.post(url, body == null ? null : json(body), params); break;
    default: fail(`Unsupported method ${method}`);
  }
  if (!record(response, name, expected)) fail(`${name} returned ${response.status}`);
  return response.json('data');
}

function submitVersion(sessions, requirementId, label) {
  const upload = api(sessions, 'student', 'POST',
    `${base}/requirements/${requirementId}/upload-sessions`,
    { fileName: `SE3112-${label}.zip`, contentType: 'application/zip',
      fileSizeBytes: size, submissionNote: `Performance lifecycle ${label}` },
    `${label}_session`, 201);
  if (!upload?.uploadUrl || !upload?.uploadSessionId) fail('Upload grant is incomplete');
  // S3-compatible pre-signed upload: never send ResearchTrack cookies or the
  // gateway performance token to object storage.
  const storageHeaders = { ...(upload.requiredHeaders || {}) };
  if (!Object.keys(storageHeaders).some(k => k.toLowerCase() === 'content-type')) {
    storageHeaders['Content-Type'] = 'application/zip';
  }
  const stored = http.put(upload.uploadUrl, fixture, {
    headers: storageHeaders, timeout: '40s', tags: { operation: `${label}_object_storage` },
  });
  const storageSuccess = [200, 201, 204].includes(stored.status);
  record(stored, `${label}_object_storage`, storageSuccess ? stored.status : 200, false);
  check(stored, { [`${label} S3 upload succeeded`]: () => storageSuccess });
  if (!storageSuccess) fail(`${label} object storage PUT failed with ${stored.status}`);
  const result = api(sessions, 'student', 'POST',
    `${base}/upload-sessions/${upload.uploadSessionId}/complete`, null,
    `${label}_complete`, 200);
  if (!result?.id || !result?.currentVersionId) fail('Completion did not return submission/version IDs');
  return result;
}

export default function (sessions) {
  const unique = `${Date.now()}-${__VU}-${__ITER}`;
  const requirement = api(sessions, 'supervisor', 'POST', `${base}/requirements`, {
    title: `SE3112 k6 lifecycle ${unique}`,
    description: 'Dedicated-project version history performance verification',
    allowedFileTypes: ['.zip'], maxFileSizeBytes: 1048576,
    responsibilityMode: 'ASSIGNED_STUDENT', assignedStudentId: __ENV.K6_STUDENT_ID,
  }, 'requirement_create', 201);
  if (!requirement?.id) fail('Requirement create response missing data.id');
  const v1 = submitVersion(sessions, requirement.id, 'v1');
  api(sessions, 'supervisor', 'POST', `${base}/${v1.id}/reviews`, {
    versionId: v1.currentVersionId, decision: 'CHANGES_REQUESTED',
    feedback: 'SE3112 automated version-history test: please resubmit.',
  }, 'review_request_changes', 200);
  const v2 = submitVersion(sessions, requirement.id, 'v2');
  if (v2.id !== v1.id || v2.versionCount !== 2) fail('Version history did not advance from v1 to v2');
  api(sessions, 'supervisor', 'POST', `${base}/${v2.id}/reviews`, {
    versionId: v2.currentVersionId, decision: 'APPROVED',
    feedback: 'SE3112 automated version-history test: accepted.',
  }, 'review_approve', 200);
  const final = api(sessions, 'student', 'GET', `${base}/${v2.id}`, null,
    'verify_version_history', 200);
  if (final.versionCount !== 2 || final.status !== 'APPROVED' || final.versions.length !== 2) {
    fail('Final submission must be APPROVED with exactly two retained versions');
  }
  // We archive only the requirement to distinguish historical test evidence.
  // Uploaded documents and version history remain persistent intentionally.
  api(sessions, 'supervisor', 'POST', `${base}/requirements/${requirement.id}/archive`,
    null, 'archive_test_requirement', 200);
}

export const handleSummary = reportSummary;
