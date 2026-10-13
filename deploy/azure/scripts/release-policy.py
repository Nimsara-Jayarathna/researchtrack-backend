#!/usr/bin/env python3
"""Read-only production eligibility check; no PR artifacts or caller-supplied SHA."""
import argparse
import json
import os
import re
import sys
import urllib.parse
import urllib.request

REPOSITORY = 'Nimsara-Jayarathna/researchtrack-backend'
BRANCH = 'main'


class PolicyError(ValueError):
    """Fixed policy rejection messages are safe to report; external errors are not."""


def verify(context, remote):
    if context.get('CI_RESULT') != 'success':
        raise PolicyError('Required CI did not succeed')
    if context.get('SOURCE_REPOSITORY') != REPOSITORY:
        raise PolicyError('Untrusted repository')
    if context.get('SOURCE_REF') != 'refs/heads/' + BRANCH:
        raise PolicyError('Unapproved branch or tag')
    if context.get('SOURCE_EVENT') not in ('push', 'workflow_dispatch'):
        raise PolicyError('Untrusted release event')
    sha = context.get('SOURCE_SHA', '')
    if not re.fullmatch(r'[0-9a-f]{40}', sha):
        raise PolicyError('Invalid immutable source SHA')
    if remote.get('name') != BRANCH or remote.get('commit', {}).get('sha') != sha:
        raise PolicyError('Source is stale or does not match the approved branch head')
    return sha


def verify_operation(context, remote):
    """Authorize manual production operations only from the current main commit."""
    ref = context.get('SOURCE_REF')
    if context.get('SOURCE_EVENT') != 'workflow_dispatch':
        raise PolicyError('Manual production operations require workflow_dispatch')
    if ref != 'refs/heads/' + BRANCH:
        raise PolicyError('Unapproved operation branch or tag')
    # No build/deploy is performed by these manual operational workflows.
    return verify(dict(context, CI_RESULT='success'), remote)


def main(operation=False):
    # Reject untrusted context before issuing even a read-only authenticated call.
    context = dict(os.environ)
    verifier = verify_operation if operation else verify
    branch = BRANCH
    verifier(context, {'name': branch, 'commit': {'sha': context.get('SOURCE_SHA')}})
    token = os.environ.get('GH_TOKEN', '')
    if not token:
        raise PolicyError('Read-only GitHub token is required')
    url = 'https://api.github.com/repos/' + REPOSITORY + '/branches/' + urllib.parse.quote(branch, safe='')
    request = urllib.request.Request(url, headers={
        'Authorization': 'Bearer ' + token, 'Accept': 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28',
    })
    with urllib.request.urlopen(request, timeout=30) as response:
        sha = verifier(context, json.load(response))
    result = ('manual operation authorized for the current trusted source ' if operation else
              'required CI succeeded for the current trusted production source ') + sha
    print('PASS: ' + result)
    summary = os.environ.get('GITHUB_STEP_SUMMARY')
    if summary:
        with open(summary, 'a') as output:
            output.write('\nManual operation source: `' + sha + '`; authorization: PASS (not release CI evidence).\n' if operation else
                         '\nRelease commit: `' + sha + '`; required CI (unit/coverage, static checks, isolated Kafka/recovery/migrations): PASS.\n')


if __name__ == '__main__':
    try:
        parser = argparse.ArgumentParser(description=__doc__)
        parser.add_argument('--operation', action='store_true', help='Authorize manual runtime work, not a release')
        main(operation=parser.parse_args().operation)
    except Exception as error:
        # Do not expose request headers, tokens, response bodies or event payloads.
        print('Release eligibility rejected: ' + (str(error) if isinstance(error, PolicyError) else type(error).__name__), file=sys.stderr)
        sys.exit(1)
