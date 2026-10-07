import http from 'k6/http';
import { fail } from 'k6';
import { BASE_URL, headers, requireConfiguration } from './settings.js';

// Setup performs ONLY two logins regardless of virtual-user count; the gateway
// deliberately retains its 10/min login limit during load tests.
function logIn(email, password, role) {
  const response = http.post(
    `${BASE_URL}/api/v1/auth/login`,
    JSON.stringify({ email, password }),
    { headers: headers({ 'Content-Type': 'application/json' }), tags: { operation: 'setup_login' } }
  );
  const cookie = response.cookies.ss_access_token?.[0]?.value;
  if (response.status !== 200 || !cookie) {
    fail(`${role} setup login did not return HTTP 200 and a session cookie (status=${response.status})`);
  }
  return cookie;
}

export function setupSessions() {
  requireConfiguration();
  return {
    supervisor: logIn(__ENV.K6_SUPERVISOR_EMAIL, __ENV.K6_SUPERVISOR_PASSWORD, 'Supervisor'),
    student: logIn(__ENV.K6_STUDENT_EMAIL, __ENV.K6_STUDENT_PASSWORD, 'Student'),
  };
}

export function useSession(sessions, role) {
  const token = sessions[role];
  if (!token) fail(`No session for role ${role}`);
  // Explicit cookie path matches AuthCookieService's AccessPath="/api".
  http.cookieJar().set(`${BASE_URL}/api`, 'ss_access_token', token, {
    path: '/api', secure: true, http_only: true,
  });
}
