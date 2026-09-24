#!/usr/bin/env python3
"""Regression tests for deploy/build/service-impact.py.

Each test builds a throwaway git repository shaped like ResearchTrack (seven
service projects referencing BuildingBlocks, the DbCheck tool, the Dockerfile)
and runs the real CLI against it. A stub `docker` stands in for GHCR.

Run: python3 deploy/build/test_service_impact.py
"""

from __future__ import annotations

import json
import os
import re
import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT = REPO_ROOT / "deploy" / "build" / "service-impact.py"
ALL = ["gateway", "auth", "project", "github", "jira", "meeting", "submission"]

PROJECTS = {
    "gateway": "src/Gateway/ResearchTrack.Gateway/ResearchTrack.Gateway.csproj",
    "auth": "src/Services/ResearchTrack.AuthService/ResearchTrack.AuthService.csproj",
    "project": "src/Services/ResearchTrack.ProjectService/ResearchTrack.ProjectService.csproj",
    "github": "src/Services/ResearchTrack.GitHubService/ResearchTrack.GitHubService.csproj",
    "jira": "src/Services/ResearchTrack.JiraService/ResearchTrack.JiraService.csproj",
    "meeting": "src/Services/ResearchTrack.MeetingService/ResearchTrack.MeetingService.csproj",
    "submission": "src/Services/ResearchTrack.SubmissionService/ResearchTrack.SubmissionService.csproj",
}
BUILDING_BLOCKS = "src/BuildingBlocks/ResearchTrack.BuildingBlocks.Api/ResearchTrack.BuildingBlocks.Api.csproj"
GITHUB_DIR = "src/Services/ResearchTrack.GitHubService"
JIRA_DIR = "src/Services/ResearchTrack.JiraService"

DOCKER_STUB = """#!/usr/bin/env python3
import os, sys
ref = sys.argv[4]
published = open(os.environ["STUB_PUBLISHED"]).read().split()
if ref in published:
    print('{"digest":"sha256:0"}')
    sys.exit(0)
print("not found", file=sys.stderr)
sys.exit(1)
"""


def csproj(*references: str, extra: str = "") -> str:
    refs = "".join(f'    <ProjectReference Include="{r}" />\n' for r in references)
    return f'<Project Sdk="Microsoft.NET.Sdk.Web">\n  <ItemGroup>\n{refs}  </ItemGroup>\n{extra}</Project>\n'


class Repo:
    def __init__(self, root: Path):
        self.root = root
        root.mkdir(parents=True)
        self.writes = 0
        self.git("init", "-q", "-b", "main")
        self.git("config", "user.email", "test@example.invalid")
        self.git("config", "user.name", "test")
        self.git("config", "commit.gpgsign", "false")

    def git(self, *args: str) -> str:
        return subprocess.run(["git", *args], cwd=self.root, check=True, capture_output=True, text=True).stdout.strip()

    def write(self, path: str, content: str | None = None) -> None:
        self.writes += 1
        if content is None:
            content = f"// content {self.writes}\n"  # always a real change
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content)

    def remove(self, path: str) -> None:
        self.git("rm", "-q", "-r", path)

    def commit(self, message: str = "change") -> str:
        self.git("add", "-A")
        self.git("commit", "-q", "--allow-empty", "-m", message)
        return self.git("rev-parse", "HEAD")


def seed(repo: Repo) -> str:
    for service, project in PROJECTS.items():
        repo.write(project, csproj("../../BuildingBlocks/ResearchTrack.BuildingBlocks.Api/ResearchTrack.BuildingBlocks.Api.csproj"))
        repo.write(str(Path(project).parent / "Program.cs"), f"// {service}\n")
    repo.write(BUILDING_BLOCKS, csproj())
    repo.write("src/BuildingBlocks/ResearchTrack.BuildingBlocks.Api/Extensions/ApplicationBuilderExtensions.cs")
    repo.write("tools/ResearchTrack.DbCheck/ResearchTrack.DbCheck.csproj", csproj())
    repo.write("tools/ResearchTrack.DbCheck/Program.cs")
    repo.write("tools/ResearchTrack.DevSeeder/ResearchTrack.DevSeeder.csproj", csproj())
    repo.write(f"{GITHUB_DIR}/Controllers/ProjectGitHubReadController.cs")
    repo.write(f"{JIRA_DIR}/Controllers/ProjectJiraConnectionController.cs")
    repo.write("deploy/Dockerfile.service", "FROM scratch\n")
    repo.write("deploy/image/migrate.sh", "#!/bin/sh\n")
    repo.write("deploy/azure/scripts/deploy-container-apps.sh", "#!/bin/sh\n")
    repo.write("deploy/build/service-impact.py", "# model\n")
    repo.write(".github/workflows/backend-build-images.yml", "name: build\n")
    repo.write(".github/workflows/backend-ci.yml", "name: ci\n")
    repo.write("Directory.Build.props", "<Project />\n")
    repo.write("Directory.Packages.props", "<Project />\n")
    repo.write("global.json", "{}\n")
    repo.write(".dockerignore", "bin\n")
    repo.write("ResearchTrack.sln", "\n")
    repo.write("README.md", "# readme\n")
    repo.write("docs/devops/README.md", "# docs\n")
    repo.write("config/env/jira/.env.example", "Jira__ClientId=\n")
    repo.write("tests/ResearchTrack.JiraService.Tests/JiraTests.cs")
    repo.write("scripts/test.sh", "#!/bin/sh\n")
    return repo.commit("seed")


class ServiceImpactTest(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        self.repo = Repo(root / "repo")
        self.base = seed(self.repo)
        stub_dir = root / "bin"
        stub_dir.mkdir()
        (stub_dir / "docker").write_text(DOCKER_STUB)
        (stub_dir / "docker").chmod(0o755)
        self.checker = str(stub_dir / "docker")
        self.published = root / "published"
        self.published.write_text("")
        self.outputs = root / "outputs"

    def tearDown(self) -> None:
        self.tmp.cleanup()

    # -- helpers -----------------------------------------------------------
    def run_cli(self, *args: str, expect: int = 0) -> tuple[str, dict[str, str]]:
        self.outputs.write_text("")
        env = dict(os.environ, STUB_PUBLISHED=str(self.published))
        result = subprocess.run(
            [sys.executable, str(SCRIPT), *args, "--github-output", str(self.outputs)],
            cwd=self.repo.root, capture_output=True, text=True, env=env)
        self.assertEqual(result.returncode, expect, result.stdout + result.stderr)
        values = dict(line.split("=", 1) for line in self.outputs.read_text().splitlines() if "=" in line)
        return result.stdout, values

    def impact(self, *changes: str, base: str | None = None, force: bool = False) -> list[str]:
        for path in changes:
            self.repo.write(path)
        head = self.repo.commit()
        _, values = self.run_cli("impact", "--base", base if base is not None else self.base,
                                 "--head", head, "--force", str(force).lower())
        return json.loads(values["matrix"])

    def plan(self, force: bool = False) -> tuple[list[str], dict[str, str], str]:
        stdout, values = self.run_cli("plan", "--head", "HEAD", "--image-prefix", "ghcr.io/o",
                                      "--checker", self.checker, "--force", str(force).lower(),
                                      "--summary", os.devnull)
        return json.loads(values["matrix"]), json.loads(values["image_tags"]), stdout

    def publish_current(self) -> dict[str, str]:
        """Mark the images for HEAD's fingerprints as published in GHCR."""
        _, tags, _ = self.plan()
        images = {"gateway": "researchtrack-gateway"}
        refs = [f"ghcr.io/o/{images.get(s, 'researchtrack-' + s)}:{tag}" for s, tag in tags.items()]
        with self.published.open("a") as handle:
            handle.write("\n".join(refs) + "\n")
        return tags

    # -- GitHub --------------------------------------------------------------
    def test_existing_github_file_rebuilds_github(self):
        self.assertEqual(self.impact(f"{GITHUB_DIR}/Controllers/ProjectGitHubReadController.cs"), ["github"])

    def test_new_github_controller_rebuilds_github(self):
        self.assertEqual(self.impact(f"{GITHUB_DIR}/Controllers/GitHubSyncStateController.cs"), ["github"])

    def test_new_github_worker_directory_rebuilds_github(self):
        self.assertEqual(self.impact(f"{GITHUB_DIR}/Workers/Sync/GitHubSyncWorker.cs",
                                     f"{GITHUB_DIR}/Services/State/GitHubSyncStateService.cs"), ["github"])

    # -- Jira ----------------------------------------------------------------
    def test_existing_jira_file_rebuilds_jira(self):
        self.assertEqual(self.impact(f"{JIRA_DIR}/Controllers/ProjectJiraConnectionController.cs"), ["jira"])

    def test_new_jira_controller_rebuilds_jira(self):
        self.assertEqual(self.impact(f"{JIRA_DIR}/Controllers/ProjectJiraIssuesController.cs"), ["jira"])

    def test_new_jira_sync_state_implementation_rebuilds_jira(self):
        self.assertEqual(self.impact(f"{JIRA_DIR}/Sync/JiraSyncStateService.cs",
                                     f"{JIRA_DIR}/Persistence/Migrations/20260923_SyncRevision.cs"), ["jira"])

    def test_new_jira_oauth_auth_url_implementation_rebuilds_jira(self):
        self.assertEqual(self.impact(f"{JIRA_DIR}/OAuth/JiraAuthUrlBuilder.cs",
                                     f"{JIRA_DIR}/appsettings.json"), ["jira"])

    def test_deleted_jira_file_rebuilds_jira(self):
        self.repo.remove(f"{JIRA_DIR}/Controllers/ProjectJiraConnectionController.cs")
        self.assertEqual(self.impact(), ["jira"])

    # -- shared code -----------------------------------------------------------
    def test_building_blocks_change_rebuilds_every_dependent_service(self):
        self.assertEqual(self.impact("src/BuildingBlocks/ResearchTrack.BuildingBlocks.Api/Security/Jwt.cs"), ALL)

    def test_new_shared_project_rebuilds_only_services_referencing_it(self):
        shared = "src/Shared/ResearchTrack.Integrations/ResearchTrack.Integrations.csproj"
        self.repo.write(shared, csproj())
        for service in ("github", "jira"):
            self.repo.write(PROJECTS[service], csproj(
                "../../BuildingBlocks/ResearchTrack.BuildingBlocks.Api/ResearchTrack.BuildingBlocks.Api.csproj",
                "../../Shared/ResearchTrack.Integrations/ResearchTrack.Integrations.csproj"))
        self.base = self.repo.commit("add shared project")
        self.assertEqual(self.impact("src/Shared/ResearchTrack.Integrations/OAuth/TokenStore.cs"), ["github", "jira"])

    def test_transitive_project_reference_is_followed(self):
        self.repo.write("src/Shared/Contracts/Contracts.csproj", csproj())
        self.repo.write(BUILDING_BLOCKS, csproj("../../Shared/Contracts/Contracts.csproj"))
        self.base = self.repo.commit("bb references contracts")
        self.assertEqual(self.impact("src/Shared/Contracts/ProjectDto.cs"), ALL)

    def test_file_linked_from_outside_project_directory(self):
        self.repo.write(PROJECTS["jira"], csproj(
            "../../BuildingBlocks/ResearchTrack.BuildingBlocks.Api/ResearchTrack.BuildingBlocks.Api.csproj",
            extra='  <ItemGroup><Compile Include="../../Generated/Jira/**/*.cs" /></ItemGroup>\n'))
        self.base = self.repo.commit("link generated code")
        self.assertEqual(self.impact("src/Generated/Jira/Client.g.cs"), ["jira"])

    def test_nested_directory_build_props_rebuilds_services_below_it(self):
        self.assertEqual(self.impact("src/Services/Directory.Build.props"),
                         ["auth", "project", "github", "jira", "meeting", "submission"])

    # -- global build inputs -------------------------------------------------------
    def test_global_build_configuration_rebuilds_all(self):
        for path in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
                     "global.json", "NuGet.config", ".editorconfig", ".dockerignore", "ResearchTrack.sln"):
            with self.subTest(path=path):
                self.assertEqual(self.impact(path), ALL)

    def test_docker_build_definition_rebuilds_all(self):
        for path in ("deploy/Dockerfile.service", "deploy/image/migrate.sh", "deploy/image/new-entrypoint.sh"):
            with self.subTest(path=path):
                self.assertEqual(self.impact(path), ALL)

    def test_image_tool_and_build_logic_rebuild_all(self):
        for path in ("tools/ResearchTrack.DbCheck/Program.cs", "deploy/build/service-impact.py",
                     ".github/workflows/backend-build-images.yml"):
            with self.subTest(path=path):
                self.assertEqual(self.impact(path), ALL)

    # -- unrelated files ---------------------------------------------------------------
    def test_documentation_and_non_image_paths_build_nothing(self):
        self.assertEqual(self.impact(
            "README.md", "docs/devops/azure-production/RUNBOOK.md", "config/env/jira/.env.example",
            "tests/ResearchTrack.JiraService.Tests/NewTests.cs", "scripts/test.sh",
            ".github/workflows/backend-ci.yml", "deploy/azure/scripts/deploy-container-apps.sh",
            "tools/ResearchTrack.DevSeeder/Program.cs"), [])

    # -- unknown paths ---------------------------------------------------------------
    def test_unknown_backend_path_is_never_silently_ignored(self):
        for path in ("src/Contracts/Events/ProjectCreated.cs", "proto/jira.proto", "Directory.Solution.props",
                     "build/versions.props"):
            with self.subTest(path=path):
                self.assertEqual(self.impact(path), ALL)
        stdout, _ = self.run_cli("impact", "--base", self.base, "--head", "HEAD")
        self.assertIn("unclassified path", stdout)

    # -- base handling ---------------------------------------------------------------
    def test_unavailable_base_rebuilds_all(self):
        self.assertEqual(self.impact("README.md", base="0123456789abcdef0123456789abcdef01234567"), ALL)

    def test_missing_base_falls_back_to_first_parent(self):
        self.assertEqual(self.impact(f"{JIRA_DIR}/X.cs", base=""), ["jira"])

    def test_force_full_build_selects_all_seven(self):
        self.assertEqual(self.impact("README.md", force=True), ALL)

    def test_impact_logs_reasons(self):
        self.repo.write(f"{JIRA_DIR}/Controllers/New.cs")
        head = self.repo.commit()
        stdout, _ = self.run_cli("impact", "--base", self.base, "--head", head)
        self.assertIn("Changed files (1):", stdout)
        self.assertRegex(stdout, r"jira +true +- src/Services/ResearchTrack\.JiraService/Controllers/New\.cs "
                                 r"\(service project ResearchTrack\.JiraService\)")
        self.assertRegex(stdout, r"github +false")

    # -- fingerprint plan (Production) ---------------------------------------------------
    def test_plan_builds_everything_when_nothing_is_published(self):
        selected, tags, _ = self.plan()
        self.assertEqual(selected, ALL)
        self.assertEqual(sorted(tags), sorted(ALL))
        self.assertTrue(all(re.fullmatch(r"src-[0-9a-f]{40}", tag) for tag in tags.values()))

    def test_plan_reuses_published_images_for_unchanged_source(self):
        self.publish_current()
        self.repo.write("docs/notes.md")
        self.repo.commit()
        selected, _, stdout = self.plan()
        self.assertEqual(selected, [])
        self.assertIn("reuse", stdout)

    def test_plan_force_full_build_rebuilds_all_with_current_tags(self):
        tags_before = self.publish_current()
        selected, tags, stdout = self.plan(force=True)
        self.assertEqual(selected, ALL)
        self.assertEqual(tags, tags_before)  # same source -> same current image specification
        self.assertEqual(stdout.count("force_full_build=true"), 7)

    def test_fingerprint_changes_only_for_affected_service(self):
        before = self.publish_current()
        self.repo.write(f"{JIRA_DIR}/Controllers/ProjectJiraIssuesController.cs")
        self.repo.commit()
        selected, after, _ = self.plan()
        self.assertEqual(selected, ["jira"])
        self.assertEqual([s for s in ALL if before[s] != after[s]], ["jira"])

    def test_incident_missed_push_is_still_deployed(self):
        """Production ran source A; feature push B (Jira/GitHub) never deployed
        (failed build / other workflow); push C only touches the Gateway.

        The per-push diff B..C selects only the Gateway (the old behaviour).
        The fingerprint plan at C selects Jira and GitHub too."""
        self.publish_current()  # A is what Production runs
        self.repo.write(f"{JIRA_DIR}/Controllers/ProjectJiraIssuesController.cs", "// sync-state\n")
        self.repo.write(f"{GITHUB_DIR}/Controllers/ProjectGitHubReadController.cs", "// sync-state\n")
        push_b = self.repo.commit("feature push (never deployed)")
        self.repo.write("src/Gateway/ResearchTrack.Gateway/TrustedProxyForwarding.cs")
        push_c = self.repo.commit("gateway push")

        _, diff_values = self.run_cli("impact", "--base", push_b, "--head", push_c)
        self.assertEqual(json.loads(diff_values["matrix"]), ["gateway"])

        selected, _, _ = self.plan()
        self.assertEqual(selected, ["gateway", "github", "jira"])

    def test_plan_outputs_summary(self):
        summary = Path(self.tmp.name) / "summary.md"
        self.run_cli("plan", "--head", "HEAD", "--image-prefix", "ghcr.io/o", "--checker", self.checker,
                     "--summary", str(summary))
        text = summary.read_text()
        for service in ALL:
            self.assertIn(f"| {service} | build | `src-", text)

    # -- metadata ------------------------------------------------------------------------
    def test_meta(self):
        result = subprocess.run([sys.executable, str(SCRIPT), "meta", "--service", "jira"],
                                capture_output=True, text=True, check=True)
        self.assertEqual(result.stdout, textwrap.dedent("""\
            project=src/Services/ResearchTrack.JiraService/ResearchTrack.JiraService.csproj
            dll=ResearchTrack.JiraService.dll
            context=JiraDbContext
            image=researchtrack-jira
            """))
        bad = subprocess.run([sys.executable, str(SCRIPT), "meta", "--service", "nope"], capture_output=True, text=True)
        self.assertEqual(bad.returncode, 1)


class RealRepositoryTest(unittest.TestCase):
    """The model against this repository's current tree."""

    def run_py(self, code: str) -> str:
        return subprocess.run([sys.executable, "-c", code], cwd=REPO_ROOT, check=True,
                              capture_output=True, text=True).stdout

    def test_every_service_project_exists_and_includes_shared_inputs(self):
        out = self.run_py(textwrap.dedent(f"""
            import importlib.util, json, sys
            spec = importlib.util.spec_from_file_location("si", {str(SCRIPT)!r})
            si = importlib.util.module_from_spec(spec); sys.modules["si"] = si; spec.loader.exec_module(si)
            tree = si.Tree("HEAD"); model = si.Model([tree])
            result = {{}}
            for service, meta in si.SERVICES.items():
                scope = model.scopes[service][0]
                result[service] = {{
                    "exists": meta.project in tree.blobs,
                    "unbounded": scope.unbounded,
                    "dirs": sorted(scope.project_dirs),
                }}
            print(json.dumps(result))
        """))
        result = json.loads(out)
        self.assertEqual(list(result), ALL)
        for service, info in result.items():
            with self.subTest(service=service):
                self.assertTrue(info["exists"])
                self.assertEqual(info["unbounded"], [])
                self.assertIn("src/BuildingBlocks/ResearchTrack.BuildingBlocks.Api", info["dirs"])
                self.assertIn("tools/ResearchTrack.DbCheck", info["dirs"])

    def test_services_match_compose_definition(self):
        compose = (REPO_ROOT / "deploy" / "compose.yml").read_text()
        images = set(re.findall(r"researchtrack-([a-z]+):\$\{DEPLOY_ENV\}", compose))
        self.assertEqual(images, set(ALL))


if __name__ == "__main__":
    unittest.main(verbosity=2)
