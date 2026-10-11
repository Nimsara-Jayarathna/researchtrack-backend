#!/usr/bin/env python3
"""Offline release-policy checks; never contact GitHub or Azure."""
import hashlib
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[3]
WORKFLOWS = ROOT / ".github/workflows"
MAIN_GUARD = "    if: ${{ github.ref == 'refs/heads/main' }}\n"
LEGACY_GUARD = "    if: ${{ inputs.environment_name != 'production' || github.ref == 'refs/heads/main' }}\n"


def read(name):
    return (WORKFLOWS / name).read_text()


def job(source, name):
    match = re.search(r"^  " + re.escape(name) + r":\n(.*?)(?=^  [\w-]+:|\Z)",
                      source.split("\njobs:\n", 1)[1], re.M | re.S)
    if match is None:
        raise AssertionError("Missing job: " + name)
    return match.group(1)


class ProductionBranchPolicy(unittest.TestCase):
    def test_application_push_is_main_only(self):
        push = read("backend-deploy-production.yml").split("  push:\n", 1)[1].split("  workflow_dispatch:", 1)[0]
        self.assertEqual('    branches: ["main"]\n', push)

    def test_infrastructure_push_is_main_only_with_path_filters(self):
        push = read("azure-infrastructure.yml").split("  push:\n", 1)[1].split("  workflow_dispatch:", 1)[0]
        self.assertTrue(push.startswith('    branches: ["main"]\n    paths:\n'))
        for path in ["deploy/azure/vm/**", "deploy/monitoring/**", "deploy/mysql/**"]:
            self.assertIn('      - "' + path + '"', push)

    def test_manual_entrypoints_cannot_deploy_other_refs(self):
        for name in ["azure-infrastructure.yml", "backend-deploy-production.yml"]:
            with self.subTest(workflow=name):
                self.assertIn("  workflow_dispatch:", read(name))
                self.assertTrue(job(read(name), "deploy").startswith(MAIN_GUARD))

    def test_reusable_azure_build_and_deploy_enforce_main(self):
        source = read("backend-deploy-azure-production.yml")
        self.assertIn("  workflow_call:", source)
        for name in ["build", "deploy"]:
            self.assertTrue(job(source, name).startswith(MAIN_GUARD))

    def test_legacy_production_is_guarded_and_test_path_is_preserved(self):
        source = read("backend-deploy-reusable.yml")
        for name in ["build", "deploy"]:
            self.assertTrue(job(source, name).startswith(LEGACY_GUARD))
        test = read("backend-deploy-test.yml")
        self.assertIn('branches: ["develop"]', test)
        self.assertIn("environment_name: test", test)

    def test_security_concurrency_and_deployment_bodies_are_unchanged(self):
        # Reviewed pre-integration workflows. Removing ONLY the new branch policy
        # must recover the originals, including secrets, OIDC, migrations, rollback,
        # power restoration, concurrency, monitoring inputs and destructive gates.
        originals = {
            "azure-infrastructure.yml": "8f3ffe86ff809db525bb9b9bdd1ed3882721f0c3a0ac3de15154ed3738207d25",
            "backend-deploy-production.yml": "a3bc4028bc2708f30de10f170e87643652702afb36be23c4be207802e036e997",
            "backend-deploy-azure-production.yml": "180d7c7e43d2d65e0bcb571bd022bd81ac878c3b658bab81ca35a0aae0b5d9a4",
            "backend-deploy-reusable.yml": "96a27e74abec4e48d31bcf89075ee1035c9e2404ba432fe5879b68e8f5c0035f",
        }
        for name, digest in originals.items():
            with self.subTest(workflow=name):
                source = read(name).replace(MAIN_GUARD, "").replace(LEGACY_GUARD, "")
                if name in ["azure-infrastructure.yml", "backend-deploy-production.yml"]:
                    source = source.replace('branches: ["main"]', 'branches: ["devops/sprint4-kafka-infrastructure-verification"]')
                self.assertEqual(digest, hashlib.sha256(source.encode()).hexdigest())

    def test_main_coverage_and_mutation_gates_remain(self):
        ci = read("backend-ci.yml")
        for value in ["scripts/coverage.sh --no-build", "COVERAGE_MIN_BUSINESS_LINE: '65'",
                      "COVERAGE_MIN_BUSINESS_BRANCH: '50'", "test_production_branch_policy.py"]:
            self.assertIn(value, ci)
        mutation = read("backend-mutation.yml")
        self.assertIn("workflow_dispatch:", mutation)
        self.assertIn("scripts/mutation-baseline.sh", mutation)


if __name__ == "__main__":
    unittest.main()
