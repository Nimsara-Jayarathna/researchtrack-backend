export const BASE_URL = (__ENV.K6_BASE_URL || '').replace(/\/+$/, '');
export const PROJECT_ID = __ENV.K6_PROJECT_ID || '';
export const PROFILE = __ENV.K6_PROFILE || 'smoke';
export const PERFORMANCE_KEY = __ENV.K6_PERFORMANCE_TOKEN || '';
export const BYPASS_ENABLED = __ENV.K6_RATE_LIMIT_BYPASS_ENABLED === 'true';
export const INCLUDE_INTEGRATIONS = __ENV.K6_INCLUDE_INTEGRATIONS === 'true';
export const REPORT_DIR = __ENV.K6_REPORT_DIR || '.';
export const WARMUP_PASSES = Number(__ENV.K6_WARMUP_PASSES || '1');
export const EXECUTION_MODE = __ENV.K6_EXECUTION_MODE || 'manual';

export function requireConfiguration() {
  if (!/^https:\/\/[^\s/]+/.test(BASE_URL)) throw new Error('K6_BASE_URL must be an HTTPS URL');
  if (!/^[0-9a-fA-F-]{36}$/.test(PROJECT_ID)) throw new Error('K6_PROJECT_ID must be a UUID');
  if (BYPASS_ENABLED && !PERFORMANCE_KEY) throw new Error('K6_PERFORMANCE_TOKEN is required when gateway exemption is enabled');
  if (!Number.isInteger(WARMUP_PASSES) || WARMUP_PASSES < 0 || WARMUP_PASSES > 5) {
    throw new Error('K6_WARMUP_PASSES must be an integer between 0 and 5');
  }
  if (!['manual', 'automatic-post-deploy'].includes(EXECUTION_MODE)) {
    throw new Error('K6_EXECUTION_MODE must be manual or automatic-post-deploy');
  }
  for (const key of ['K6_SUPERVISOR_EMAIL', 'K6_SUPERVISOR_PASSWORD', 'K6_STUDENT_EMAIL', 'K6_STUDENT_PASSWORD']) {
    if (!__ENV[key]) throw new Error(`${key} is not configured`);
  }
}

export function headers(extra = {}) {
  return {
    ...(BYPASS_ENABLED && PERFORMANCE_KEY ? { 'X-ResearchTrack-Performance-Key': PERFORMANCE_KEY } : {}),
    ...extra,
  };
}
