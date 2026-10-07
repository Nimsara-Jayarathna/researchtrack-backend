// Canonical operation catalogue used by metrics, runtime-configured endpoint
// thresholds and detailed reports. Keep operation keys stable: historical k6
// artifacts can then be compared across runs even if routes change internally.
export const OPERATION_CATALOG = Object.freeze({
  projects: { label: 'List projects', category: 'Project', method: 'GET' },
  project_details: { label: 'Project details', category: 'Project', method: 'GET' },
  requirements: { label: 'Submission requirements', category: 'Submission', method: 'GET' },
  submissions: { label: 'Submission history', category: 'Submission', method: 'GET' },
  jira_issues: { label: 'Jira issues', category: 'Jira', method: 'GET' },
  jira_sprint_progress: { label: 'Jira sprint progress', category: 'Jira', method: 'GET' },
  github_activity: { label: 'GitHub activity', category: 'GitHub', method: 'GET' },
  supervisor_dashboard: { label: 'Supervisor dashboard', category: 'Dashboard', method: 'GET' },
  requirement_create: { label: 'Create submission requirement', category: 'Submission write', method: 'POST' },
  requirement_delete: { label: 'Delete submission requirement', category: 'Submission write', method: 'DELETE' },
  v1_session: { label: 'Create v1 upload session', category: 'Lifecycle', method: 'POST' },
  v1_object_storage: { label: 'Upload v1 object', category: 'Object storage', method: 'PUT' },
  v1_complete: { label: 'Complete v1 upload', category: 'Lifecycle', method: 'POST' },
  review_request_changes: { label: 'Request submission changes', category: 'Lifecycle', method: 'POST' },
  v2_session: { label: 'Create v2 upload session', category: 'Lifecycle', method: 'POST' },
  v2_object_storage: { label: 'Upload v2 object', category: 'Object storage', method: 'PUT' },
  v2_complete: { label: 'Complete v2 upload', category: 'Lifecycle', method: 'POST' },
  review_approve: { label: 'Approve submission', category: 'Lifecycle', method: 'POST' },
  verify_version_history: { label: 'Verify version history', category: 'Lifecycle', method: 'GET' },
  archive_test_requirement: { label: 'Archive test requirement', category: 'Lifecycle', method: 'POST' },
  jira_webhook: { label: 'Jira webhook ingestion', category: 'Webhook', method: 'POST' },
  unknown: { label: 'Uncatalogued operation', category: 'Other', method: 'HTTP' },
});

export const OPERATION_KEYS = Object.freeze(Object.keys(OPERATION_CATALOG));

export function operationMetricKey(operation) {
  return OPERATION_CATALOG[operation] ? operation : 'unknown';
}
