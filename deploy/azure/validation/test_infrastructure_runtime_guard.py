#!/usr/bin/env python3
"""Offline safety tests for Azure infrastructure runtime classification / restore."""
import importlib.util
import json
import re
import shutil
import subprocess
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[3]
TARGET = ROOT / "deploy/azure/scripts/infrastructure-runtime-guard.py"
spec = importlib.util.spec_from_file_location("rt_infra_guard", TARGET)
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)
VM_ID = "/subscriptions/fake/resourceGroups/rg-researchtrack-prod/providers/Microsoft.Compute/virtualMachines/vm-researchtrack-infra-prod"
GROUP = "rg-researchtrack-prod"
VM = "vm-researchtrack-infra-prod"


class AzureFixture:
    def __init__(self, states=None, vm_state="PowerState/deallocated", vm_present=True):
        self.states = states if states is not None else {name: "Stopped" for name in guard.APPS}
        self.power = vm_state
        self.present = vm_present
        self.operations = []
        self.vm_id = VM_ID

    def call(self, *args):
        self.operations.append(args)
        if args[:2] == ("group", "exists"):
            return "true"
        if args[:2] == ("vm", "list"):
            return json.dumps([{"name": VM, "id": self.vm_id}] if self.present else [])
        if args[:2] == ("containerapp", "list"):
            return json.dumps([{"name": n, "properties": {"runningStatus": v}} for n, v in self.states.items()])
        if args[:2] == ("vm", "show"):
            return self.vm_id
        if args[:2] == ("vm", "get-instance-view"):
            return self.power
        if args[:2] == ("vm", "deallocate"):
            self.power = "PowerState/deallocated"
            return ""
        if args[:2] == ("vm", "start"):
            self.power = "PowerState/running"
            return ""
        if args[:2] == ("vm", "stop"):
            self.power = "PowerState/stopped"
            return ""
        raise AssertionError("Unexpected az invocation: " + repr(args))


class RuntimeGuardTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.snapshot_file = Path(self.tmp.name) / "snapshot.json"
        self.env_file = Path(self.tmp.name) / "github.env"

    def classify(self, states):
        fixture = AzureFixture(states=states)
        with patch.object(guard, "az", fixture.call):
            guard.classify(GROUP, self.env_file)
        return self.env_file.read_text()

    def test_online_requires_all_seven_running(self):
        self.assertIn("EXPECT_APPS=true", self.classify({x: "Running" for x in guard.APPS}))
        self.assertIn("APP_RUNTIME_MODE=online", self.env_file.read_text())

    def test_stopped_apps_explicitly_skip_application_probes(self):
        self.assertIn("EXPECT_APPS=false", self.classify({x: "Stopped" for x in guard.APPS}))
        self.assertIn("APP_RUNTIME_MODE=paused", self.env_file.read_text())

    def test_bootstrap_and_stopped_partial_are_explicit(self):
        self.assertIn("APP_RUNTIME_MODE=bootstrap", self.classify({}))
        self.env_file.unlink()
        self.assertIn("APP_RUNTIME_MODE=partial-stopped", self.classify({guard.APPS[0]: "Stopped"}))

    def test_mixed_seven_fail_closed(self):
        with self.assertRaisesRegex(ValueError, "Mixed/partial active"):
            self.classify({name: ("Running" if i == 0 else "Stopped") for i, name in enumerate(guard.APPS)})
        self.assertFalse(self.env_file.exists())

    def test_partial_running_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "Mixed/partial active"):
            self.classify({guard.APPS[0]: "Running"})

    def test_unknown_transitional_and_unmanaged_fails_closed(self):
        for states in [{guard.APPS[0]: "Starting"}, {guard.APPS[0]: None}, {"rt-new-prod": "Stopped"}]:
            with self.subTest(states=states), self.assertRaises(ValueError):
                self.classify(states)

    def test_snapshot_off_and_restore_after_work(self):
        fixture = AzureFixture()
        with patch.object(guard, "az", fixture.call):
            guard.snapshot(GROUP, VM, self.snapshot_file)
            fixture.power = "PowerState/running"  # VM started by Azure Run Command
            guard.restore(GROUP, VM, self.snapshot_file)
        self.assertEqual("PowerState/deallocated", fixture.power)
        self.assertTrue(any(c[:2] == ("vm", "deallocate") for c in fixture.operations))
        self.assertEqual(0o600, self.snapshot_file.stat().st_mode & 0o777)

    def test_original_allocated_stopped_restored_without_deallocation(self):
        fixture = AzureFixture(vm_state="PowerState/stopped")
        with patch.object(guard, "az", fixture.call):
            guard.snapshot(GROUP, VM, self.snapshot_file)
            fixture.power = "PowerState/running"
            guard.restore(GROUP, VM, self.snapshot_file)
        self.assertEqual("PowerState/stopped", fixture.power)
        self.assertFalse(any(c[:2] == ("vm", "deallocate") for c in fixture.operations))

    def test_initially_running_is_never_turned_off(self):
        fixture = AzureFixture(vm_state="PowerState/running", states={x: "Running" for x in guard.APPS})
        with patch.object(guard, "az", fixture.call):
            guard.snapshot(GROUP, VM, self.snapshot_file)
            guard.restore(GROUP, VM, self.snapshot_file)
        self.assertFalse(any(c[:2] in (("vm", "deallocate"), ("vm", "stop")) for c in fixture.operations))

    def test_originally_running_vm_is_restarted_on_failure(self):
        fixture = AzureFixture(vm_state="PowerState/running", states={x: "Running" for x in guard.APPS})
        with patch.object(guard, "az", fixture.call):
            guard.snapshot(GROUP, VM, self.snapshot_file)
            fixture.power = "PowerState/deallocated"
            guard.restore(GROUP, VM, self.snapshot_file)
        self.assertEqual("PowerState/running", fixture.power)
        self.assertTrue(any(c[:2] == ("vm", "start") for c in fixture.operations))

    def test_snapshot_refuses_vm_off_with_running_apps(self):
        fixture = AzureFixture(states={guard.APPS[0]: "Running"})
        with patch.object(guard, "az", fixture.call), self.assertRaisesRegex(ValueError, "VM is off while"):
            guard.snapshot(GROUP, VM, self.snapshot_file)

    def test_restore_refuses_changed_app_state(self):
        fixture = AzureFixture()
        with patch.object(guard, "az", fixture.call):
            guard.snapshot(GROUP, VM, self.snapshot_file)
            fixture.power = "PowerState/running"
            fixture.states[guard.APPS[0]] = "Running"
            with self.assertRaisesRegex(ValueError, "states changed"):
                guard.restore(GROUP, VM, self.snapshot_file)
        self.assertFalse(any(c[:2] == ("vm", "deallocate") for c in fixture.operations))

    def test_restore_refuses_resource_identity_change(self):
        fixture = AzureFixture()
        with patch.object(guard, "az", fixture.call):
            guard.snapshot(GROUP, VM, self.snapshot_file)
            fixture.power = "PowerState/running"
            fixture.vm_id = VM_ID + "-replacement"
            with self.assertRaisesRegex(ValueError, "identity changed"):
                guard.restore(GROUP, VM, self.snapshot_file)

    def test_missing_vm_snapshot_does_not_stop_new_vm(self):
        fixture = AzureFixture(states={}, vm_present=False)
        with patch.object(guard, "az", fixture.call):
            guard.snapshot(GROUP, VM, self.snapshot_file)
            fixture.present = True
            fixture.power = "PowerState/running"
            guard.restore(GROUP, VM, self.snapshot_file)
        self.assertEqual("PowerState/running", fixture.power)

    def test_guard_wired_before_mutation_and_restore_always_runs(self):
        workflow = (ROOT / ".github/workflows/azure-infrastructure.yml").read_text()
        self.assertLess(workflow.index("Snapshot infrastructure runtime before reconciliation"),
                        workflow.index("      - name: What-If"))
        self.assertIn("steps.snapshot.outcome == 'success'", workflow)
        self.assertIn("APP_RUNTIME_MODE=$APP_RUNTIME_MODE", workflow)
        self.assertIn("infrastructure-runtime-guard.py classify", workflow)
        self.assertIn("infrastructure-runtime-guard.py restore", workflow)
        self.assertIn("EXPECT_APPS=$EXPECT_APPS", workflow)
        # Other runtime operations are not rewritten to mutate Container Apps.
        self.assertNotIn("az containerapp start", workflow)

    @unittest.skipUnless(shutil.which("jq"), "jq is needed for actual Prometheus alert-query evaluation")
    def test_real_prometheus_alert_query_filters_only_expected_app_alerts(self):
        source = (ROOT / "deploy/azure/scripts/validate-vm-stack.sh").read_text()
        expression = re.search(
            r'critical="\$\(curl -fsS http://127\.0\.0\.1:9090/api/v1/alerts \| jq -r \\\n'
            r'\s+--arg mode "\$app_runtime_mode" \'(.*?)\'\)"', source, re.S)
        self.assertIsNotNone(expression, "Cannot find actual jq expression in validator")
        alert_names = ("ResearchTrackServiceUnavailable", "ResearchTrackGatewayUnavailable",
                       "ResearchTrackKafkaBrokerUnavailable")
        data = json.dumps({"data": {"alerts": [
            {"state": "firing", "labels": {"severity": "critical", "alertname": name}}
            for name in alert_names]}})
        for mode, expected in (("paused", {"ResearchTrackKafkaBrokerUnavailable"}),
                               ("online", set(alert_names))):
            run = subprocess.run(["jq", "-r", "--arg", "mode", mode, expression.group(1)],
                                 input=data, capture_output=True, text=True)
            self.assertEqual(0, run.returncode, run.stderr)
            self.assertEqual(expected, set(run.stdout.strip().split(", ")))

    def test_paused_monitoring_does_not_silence_kafka_critical_alerts(self):
        source = (ROOT / "deploy/azure/scripts/validate-vm-stack.sh").read_text()
        self.assertIn('ResearchTrackServiceUnavailable', source)
        self.assertIn('ResearchTrackGatewayUnavailable', source)
        self.assertIn('critical alerts firing: $critical', source)
        self.assertIn('select($mode == "online" or', source)


if __name__ == "__main__":
    unittest.main()
