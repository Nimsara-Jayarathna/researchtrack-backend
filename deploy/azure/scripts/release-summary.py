#!/usr/bin/env python3
"""Summarize this run's statuses/timestamps via read-only GitHub Actions metadata."""
from datetime import datetime
import json
import os
import re
import sys
import urllib.request

REPOSITORY = 'Nimsara-Jayarathna/researchtrack-backend'


def elapsed(start, end):
    if not start or not end:
        return None
    return max(0, round((datetime.fromisoformat(end.replace('Z', '+00:00'))
                         - datetime.fromisoformat(start.replace('Z', '+00:00'))).total_seconds()))


def phase(rows):
    if not rows:
        return 'NOT EXECUTED', '-'
    results = [row.get('conclusion') for row in rows]
    if all(result == 'skipped' for result in results):
        return 'SKIPPED', '-'
    result = ('PASS' if all(item == 'success' for item in results)
              else 'FAIL' if any(item in ('failure', 'cancelled', 'timed_out') for item in results)
              else 'INCOMPLETE')
    timed = [row for row in rows if row.get('started_at') and row.get('completed_at')
             and row.get('conclusion') != 'skipped']
    seconds = elapsed(min(row['started_at'] for row in timed), max(row['completed_at'] for row in timed)) if timed else None
    return result, str(seconds) if seconds is not None else '-'


def render(run, jobs, sha, deploy_result, smoke_result):
    # Names below are fixed workflow names. Never copy arbitrary logs, payloads,
    # actors, step outputs or credentials into the release artifact.
    ci = [job for job in jobs if job['name'].endswith('Restore, Build and Test')
          or job['name'].endswith('kafka-integration')]
    ci_result, ci_seconds = phase(ci)
    if len(ci) != 2:
        ci_result = 'INCOMPLETE'
    build = [job for job in jobs if re.search(r'(?:^| / )Build (?:auth|project|github|jira|meeting|submission|gateway)$', job['name'])]
    built = sum(job.get('conclusion') == 'success' for job in build)
    steps = [step for job in jobs for step in job.get('steps', [])]
    def step_phase(*names):
        return phase([step for step in steps if step.get('name') in names])
    infrastructure = step_phase('Static Azure infrastructure preflight', 'Runtime deployment preflight',
                                'Verify VM stack and monitoring', 'Verify private networking and Nginx -> Gateway')
    health = step_phase('Migrate databases and deploy changed Container Apps')
    public = step_phase('Verify public API endpoint')
    restoration = step_phase('Restore exact pre-deployment runtime state',
                             'Final Gateway configuration restoration safeguard',
                             'Final restoration safeguard')
    smoke = {'success': 'PASS', 'failure': 'FAIL', 'cancelled': 'CANCELLED', 'skipped': 'SKIPPED'}.get(smoke_result, 'INCOMPLETE')
    # Aggregate caller results are authoritative, but inconsistent/missing job
    # metadata must never conceal a readiness, restoration or unrelated failure.
    failed_evidence = any(row.get('conclusion') in ('failure', 'cancelled', 'timed_out', 'action_required', 'startup_failure', 'stale')
                          for row in jobs + steps)
    required_names = ['Static Azure infrastructure preflight', 'Runtime deployment preflight',
                      'Verify VM stack and monitoring', 'Verify private networking and Nginx -> Gateway',
                      'Migrate databases and deploy changed Container Apps', 'Restore exact pre-deployment runtime state']
    if smoke == 'PASS':
        required_names += ['Start Azure → optional Gateway allowance → VPS k6 → restore Gateway and Azure',
                          'Final Gateway configuration restoration safeguard', 'Final restoration safeguard']
    complete = all(len([step for step in steps if step.get('name') == name and step.get('conclusion') == 'success']) == 1
                   for name in required_names)
    final = ('FAIL' if deploy_result != 'success' or ci_result != 'PASS' or failed_evidence or not complete or smoke in ('FAIL', 'CANCELLED', 'INCOMPLETE')
             else 'PASS' if smoke == 'PASS' else 'DEPLOYMENT PASSED; SMOKE SKIPPED — FINAL ACCEPTANCE PENDING')
    # created_at refers to the original run; it cannot measure a rerun's queue.
    queue = elapsed(run.get('created_at'), run.get('run_started_at')) if run.get('run_attempt') == 1 else None
    smoke_phase = step_phase('Start Azure → optional Gateway allowance → VPS k6 → restore Gateway and Azure')
    lines = ['### Production release result', '', f'Release commit: `{sha}`', '',
             f'**{final}**', '',
             f'Required CI: {ci_result}. Successfully built images: {built}.',
             'Image reuse/provenance, changed/unchanged applications and migrations are recorded in the source-selection and Container Apps tables.',
             'A no-op application table means no new application version was deployed.', '',
             '| Phase | Result | Wall seconds |', '|---|---|---:|',
             f'| Workflow queue | {"MEASURED" if queue is not None else "NOT AVAILABLE (rerun/missing timestamp)"} | {queue if queue is not None else "-"} |',
             f'| Required CI (unit/coverage/static + isolated Kafka/upgrades) | {ci_result} | {ci_seconds} |',
             f'| Parallel image builds | {phase(build)[0]} | {phase(build)[1]} |',
             f'| Infrastructure verification | {infrastructure[0]} | {infrastructure[1]} |',
             f'| Migration/rollout/revision health step | {health[0]} | {health[1]} |',
             f'| Optional public HTTP readiness | {public[0]} | {public[1]} |',
             f'| k6 smoke runtime session (includes acquire/restore) | {smoke} | {smoke_phase[1]} |',
             f'| Runtime/Gateway restoration safeguards | {restoration[0]} | {restoration[1]} |', '',
             'Individual migration, rollout and revision-health timings appear in the deployment table. Parallel durations are wall intervals, not summed worker time.',
             'k6-only timing and thresholds appear in its existing performance reports. A failed k6 smoke fails the release; use the reviewed revision rollback plan.',
             'On failure: retain run logs, image digests, migration/revision tables and Azure/Gateway snapshot artifacts. Check restoration first; then diagnose readiness, networking and k6 thresholds. Multi-service/schema rollback requires operator review; never reset Kafka offsets or remove durable pending events.', '']
    return '\n'.join(lines)


def main():
    run_id, attempt = os.environ['GITHUB_RUN_ID'], os.environ['GITHUB_RUN_ATTEMPT']
    sha = os.environ['GITHUB_SHA']
    if os.environ.get('GITHUB_REPOSITORY') != REPOSITORY or not re.fullmatch('[0-9a-f]{40}', sha):
        raise ValueError('Invalid release source')
    if not re.fullmatch('[0-9]+', run_id) or not re.fullmatch('[0-9]+', attempt):
        raise ValueError('Invalid run metadata')
    token = os.environ['GH_TOKEN']
    def get(path):
        request = urllib.request.Request('https://api.github.com/repos/' + REPOSITORY + path,
            headers={'Authorization': 'Bearer ' + token, 'Accept': 'application/vnd.github+json',
                     'X-GitHub-Api-Version': '2022-11-28'})
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)
    run = get('/actions/runs/' + run_id + '/attempts/' + attempt)
    if run.get('head_sha') != sha:
        raise ValueError('Run/source SHA mismatch')
    jobs = []
    for page in range(1, 11):
        data = get('/actions/runs/' + run_id + '/attempts/' + attempt + '/jobs?per_page=100&page=' + str(page))
        jobs.extend(data['jobs'])
        if len(jobs) >= data['total_count']:
            break
    else:
        raise ValueError('Incomplete job metadata')
    summary = render(run, jobs, sha, os.environ['DEPLOY_RESULT'], os.environ['SMOKE_RESULT'])
    with open(os.environ['GITHUB_STEP_SUMMARY'], 'a') as output:
        output.write(summary)


def report_unavailable(error):
    """Retain caller conclusions without inventing missing API evidence."""
    statuses = {'success': 'PASS', 'failure': 'FAIL', 'cancelled': 'CANCELLED',
                'skipped': 'SKIPPED', 'timed_out': 'TIMED OUT'}
    deploy = statuses.get(os.environ.get('DEPLOY_RESULT'), 'INCOMPLETE')
    smoke = statuses.get(os.environ.get('SMOKE_RESULT'), 'INCOMPLETE')
    sha = os.environ.get('GITHUB_SHA', '')
    if os.environ.get('GITHUB_REPOSITORY') != REPOSITORY or not re.fullmatch('[0-9a-f]{40}', sha):
        sha = 'UNAVAILABLE'
    failed = deploy in ('FAIL', 'CANCELLED', 'TIMED OUT') or smoke in ('FAIL', 'CANCELLED', 'TIMED OUT')
    result = ('FAIL — RELEASE REPORT INCOMPLETE' if failed
              else 'REPORTING INCOMPLETE — FINAL ACCEPTANCE NOT VERIFIED')
    text = ('### Production release result\n\n'
            f'Release commit: `{sha}`\n\n**{result}**\n\n'
            f'Deployment job result: {deploy}. k6 smoke job result: {smoke}.\n\n'
            f'Release metadata/report generation unavailable ({type(error).__name__}). '
            'Required CI, phase results and durations are NOT VERIFIED. '
            'Skipped smoke is not full acceptance.\n\n'
            'The reporting job remains failed. Retain the deployment/migration tables, '
            'workflow logs and Azure/Gateway recovery artifacts. Check restoration first; '
            'do not infer or automatically roll back application/schema state from missing metadata.\n')
    with open(os.environ['GITHUB_STEP_SUMMARY'], 'a') as output:
        output.write(text)


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print('Release reporting failed: ' + type(error).__name__, file=sys.stderr)
        try:
            report_unavailable(error)
        except Exception as summary_error:
            print('Release failure summary unavailable: ' + type(summary_error).__name__, file=sys.stderr)
        sys.exit(1)
