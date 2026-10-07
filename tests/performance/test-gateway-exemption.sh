#!/usr/bin/env bash
# Offline mocked Azure regression: source state, dynamic IP, revision updates,
# exact original template restoration, secret cleanup and failed updates.
set -Eeuo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin"
cat > "$work/bin/az" <<'PY'
#!/usr/bin/env python3
import json, os, pathlib, sys
p=pathlib.Path(os.environ['FAKE_GATEWAY_STATE'])
d=json.loads(p.read_text())
a=sys.argv[1:]

def save():p.write_text(json.dumps(d))

def fail(msg):sys.exit(msg)

if a[:2]==['containerapp','show']:
    print(json.dumps(d['app']))
elif a[:3]==['containerapp','secret','list']:
    print(json.dumps([{'name':n} for n in d['secrets']]))
elif a[:3]==['containerapp','secret','set']:
    namevalue=a[a.index('--secrets')+1]
    name,secret=namevalue.split('=',1)
    if name in d['secrets']:fail('Secret overwrite is not permitted')
    assert len(secret)>=32
    d['secrets'].append(name);save()
elif a[:3]==['containerapp','secret','remove']:
    name=a[a.index('--secret-names')+1]
    d['secrets'].remove(name);save()
elif a[:3]==['containerapp','revision','copy']:
    if os.environ.get('FAKE_AZ_FAIL_REVISION_COPY')=='true':
        fail('Mock Azure refused revision creation')
    origin=a[a.index('--from-revision')+1]
    src=d['revisions'][origin]
    fresh=json.loads(json.dumps(src))
    # revision-scope environment mutation from --set-env-vars
    if '--set-env-vars' in a:
        idx=a.index('--set-env-vars')+1
        while idx<len(a) and not a[idx].startswith('--'):
            key,val=a[idx].split('=',1)
            env=fresh['containers'][0]['env']
            env[:]=[x for x in env if x['name']!=key]
            env.append({'name':key, 'secretRef':val[len('secretref:'):] } if val.startswith('secretref:') else {'name':key,'value':val})
            idx+=1
    d['counter']+=1
    rev='rt-gateway-prod--mock'+str(d['counter'])
    d['revisions'][rev]=fresh
    app=d['app']
    app['properties']['latestRevisionName']=rev
    app['properties']['latestReadyRevisionName']=rev
    app['properties']['template']=fresh
    save()
else:
    fail('Unsupported fake az args: '+repr(a))
PY
chmod +x "$work/bin/az"
export PATH="$work/bin:$PATH"
export RESOURCE_GROUP=rg-researchtrack-prod
export K6_GATEWAY_APP_NAME=rt-gateway-prod
export GITHUB_RUN_ID=9876543 GITHUB_RUN_ATTEMPT=1
export K6_PERFORMANCE_TOKEN='synthetic-super-secret-token-abcdefghijklmnopqrstuvwxyz'
export K6_GATEWAY_WAIT_ATTEMPTS=3 K6_GATEWAY_WAIT_SECONDS=0
export FAKE_GATEWAY_STATE="$work/azure-state.json"
python3 - <<'PY'
import os,json,pathlib
rev='rt-gateway-prod--baseline'
template={'containers':[{'name':'gateway','image':'example.invalid/gateway:tested','resources':{'cpu':0.5,'memory':'1Gi'},'env':[
    {'name':'RateLimiting__PerformanceTest__Enabled','value':'false'},
    {'name':'RateLimiting__PerformanceTest__AllowedIp','value':'0.0.0.0'},
    {'name':'RateLimiting__PerformanceTest__Token','value':'DISABLED'},
    {'name':'OTHER_SERVICE_SECRET','secretRef':'original-backend-secret'},
]}],'scale':{'minReplicas':1,'maxReplicas':1}}
app={'properties':{'configuration':{'activeRevisionsMode':'Single','ingress':{'traffic':[{'latestRevision':True,'weight':100}]}},
 'latestRevisionName':rev,'latestReadyRevisionName':rev,'template':template}}
pathlib.Path(os.environ['FAKE_GATEWAY_STATE']).write_text(json.dumps({'app':app,'revisions':{rev:template},'counter':0,'secrets':['original-backend-secret']}))
PY
snapshot="$work/gateway.json"
script="$repo/scripts/performance/gateway-exemption.sh"
"$script" capture "$snapshot"
[[ -s "$snapshot" ]]
if grep -F "$K6_PERFORMANCE_TOKEN" "$snapshot"; then echo 'Gateway snapshot leaked the secret.' >&2;exit 1;fi
"$script" enable "$snapshot" 198.51.100.53 2>/dev/null && {
  echo 'Reserved test IPv4 was incorrectly accepted for gateway allowance.' >&2;exit 1;
}
"$script" enable "$snapshot" 8.8.8.8
python3 - <<'PY'
import json,os
from pathlib import Path
d=json.loads(Path(os.environ['FAKE_GATEWAY_STATE']).read_text())
env={x['name']:x for x in d['app']['properties']['template']['containers'][0]['env']}
assert env['RateLimiting__PerformanceTest__Enabled']['value']=='true'
assert env['RateLimiting__PerformanceTest__AllowedIp']['value']=='8.8.8.8'
assert env['RateLimiting__PerformanceTest__Token']['secretRef'].startswith('k6-perf-')
assert 'original-backend-secret' in d['secrets']
assert len(d['secrets'])==2
PY
"$script" restore "$snapshot"
"$script" verify "$snapshot"
python3 - <<'PY'
import json,os
from pathlib import Path
d=json.loads(Path(os.environ['FAKE_GATEWAY_STATE']).read_text())
assert d['app']['properties']['template']==d['revisions']['rt-gateway-prod--baseline']
assert d['secrets']==['original-backend-secret']
assert d['app']['properties']['latestRevisionName']!='rt-gateway-prod--baseline'
PY
# The restore operation is idempotent, including secret removal.
"$script" restore "$snapshot"

# An Azure error *after* secret creation must still be recoverable.
export GITHUB_RUN_ID=9876544
snapshot2="$work/gateway-failed.json"
"$script" capture "$snapshot2"
export FAKE_AZ_FAIL_REVISION_COPY=true
if "$script" enable "$snapshot2" 8.8.8.8 >/dev/null 2>&1;then
  echo 'Simulated Azure update failure was ignored.' >&2;exit 1
fi
unset FAKE_AZ_FAIL_REVISION_COPY
"$script" restore "$snapshot2"
"$script" verify "$snapshot2"
# Simulate failure of the actual remote k6 runner. The wrapper MUST restore
# before control returns to the Azure runtime-session power cleanup.
export GITHUB_RUN_ID=9876546
snapshot3="$work/gateway-k6-failed.json"
"$script" capture "$snapshot3"
mkdir -p "$work/session"
cp "$repo/scripts/performance/run-gateway-session.sh" "$repo/scripts/performance/gateway-exemption.sh" "$work/session/"
cat > "$work/session/run-vps.sh" <<'SH'
#!/usr/bin/env bash
exit 17  # synthetic failed k6 performance gate
SH
chmod +x "$work/session/"*.sh
export K6_RATE_LIMIT_BYPASS_ENABLED=true
export K6_GATEWAY_STATE_FILE="$snapshot3"
export K6_GATEWAY_ACTIVE_FILE="$work/gateway.active"
export K6_VPS_EGRESS_IP=8.8.8.8
if bash "$work/session/run-gateway-session.sh" >/dev/null 2>&1; then
  echo 'Failed k6 was incorrectly accepted.' >&2;exit 1
fi
[[ ! -f "$K6_GATEWAY_ACTIVE_FILE" ]] || { echo 'Gateway remained owned after failed k6.' >&2;exit 1; }
"$script" verify "$snapshot3"

# Single mode is required; no unreviewed traffic topology mutation.
python3 - <<'PY'
import json,os,pathlib
p=pathlib.Path(os.environ['FAKE_GATEWAY_STATE']);d=json.loads(p.read_text())
d['app']['properties']['configuration']['activeRevisionsMode']='Multiple';p.write_text(json.dumps(d))
PY
export GITHUB_RUN_ID=9876545
if "$script" capture "$work/rejected.json" >/dev/null 2>&1;then
  echo 'Unsupported multi-revision gateway was accepted.' >&2;exit 1
fi
printf 'Mocked Gateway capture/enable/restore/verify/failure recovery tests passed.\n'
