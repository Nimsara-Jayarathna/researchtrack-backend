#!/usr/bin/env python3
"""Read-only production eligibility check; no PR artifacts or caller-supplied SHA."""
import json
import os
import re
import sys
import urllib.parse
import urllib.request

REPOSITORY = 'Nimsara-Jayarathna/researchtrack-backend'
BRANCH = 'integration/main-kafka-production-20261011'


def verify(context, remote):
    if context.get('CI_RESULT') != 'success':
        raise ValueError('Required CI did not succeed')
    if context.get('SOURCE_REPOSITORY') != REPOSITORY:
        raise ValueError('Untrusted repository')
    if context.get('SOURCE_REF') != 'refs/heads/' + BRANCH:
        raise ValueError('Unapproved branch or tag')
    if context.get('SOURCE_EVENT') not in ('push', 'workflow_dispatch'):
        raise ValueError('Untrusted release event')
    sha = context.get('SOURCE_SHA', '')
    if not re.fullmatch(r'[0-9a-f]{40}', sha):
        raise ValueError('Invalid immutable source SHA')
    if remote.get('name') != BRANCH or remote.get('commit', {}).get('sha') != sha:
        raise ValueError('Source is stale or does not match the approved branch head')
    return sha


def main():
    # Reject untrusted context before issuing even a read-only authenticated call.
    context = dict(os.environ)
    verify(context, {'name': BRANCH, 'commit': {'sha': context.get('SOURCE_SHA')}})
    token = os.environ.get('GH_TOKEN', '')
    if not token:
        raise ValueError('Read-only GitHub token is required')
    url = 'https://api.github.com/repos/' + REPOSITORY + '/branches/' + urllib.parse.quote(BRANCH, safe='')
    request = urllib.request.Request(url, headers={
        'Authorization': 'Bearer ' + token, 'Accept': 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28',
    })
    with urllib.request.urlopen(request, timeout=30) as response:
        sha = verify(context, json.load(response))
    print('PASS: required CI succeeded for the current trusted production source ' + sha)
    summary = os.environ.get('GITHUB_STEP_SUMMARY')
    if summary:
        with open(summary, 'a') as output:
            output.write('\nRelease commit: `' + sha + '`; required CI (unit/coverage, static checks, isolated Kafka/recovery/migrations): PASS.\n')


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # Do not expose request headers, tokens, response bodies or event payloads.
        print('Release eligibility rejected: ' + (str(error) if isinstance(error, ValueError) else type(error).__name__), file=sys.stderr)
        sys.exit(1)
