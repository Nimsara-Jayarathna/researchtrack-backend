#!/usr/bin/env python3
"""Decide which backend service images a commit needs, and explain why.

One dependency model for every deployment path. For each service the image
inputs are, read from the commit itself (never hard-coded per feature):

- the service project directory and every project it references
  (ProjectReference, followed transitively), plus any file a project pulls in
  from outside its directory (Compile/Content/Import items);
- the DbCheck tool the Dockerfile compiles into every image;
- MSBuild/SDK/NuGet configuration picked up implicitly from any of those
  project directories or their ancestors (Directory.Build.*, NuGet.config, ...);
- files every image build reads: the Dockerfile and its COPY sources, the
  build-context filter, this model and the image build workflow.

Known non-image paths (docs, tests, env contracts, deployment scripts, ...)
never trigger a build. Anything else is *unclassified* and counts as an input
of every service, so a new shared directory or build file is rebuilt
conservatively instead of being silently ignored.

Commands:
  impact  services affected by the changes between two commits (Test path)
  plan    per-service source fingerprint and whether an image for it is
          already published; missing images are built (Production path)
  meta    build metadata for one service
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import posixpath
import re
import subprocess
import sys
from dataclasses import dataclass, field

# Bump to invalidate every published fingerprint (forces a full rebuild).
MODEL_VERSION = "1"


@dataclass(frozen=True)
class Service:
    project: str
    dll: str
    db_context: str
    image: str


# Build and log order. The single service -> project/image mapping for CI.
SERVICES: dict[str, Service] = {
    "gateway": Service(
        "src/Gateway/ResearchTrack.Gateway/ResearchTrack.Gateway.csproj",
        "ResearchTrack.Gateway.dll", "", "researchtrack-gateway"),
    "auth": Service(
        "src/Services/ResearchTrack.AuthService/ResearchTrack.AuthService.csproj",
        "ResearchTrack.AuthService.dll", "AuthDbContext", "researchtrack-auth"),
    "project": Service(
        "src/Services/ResearchTrack.ProjectService/ResearchTrack.ProjectService.csproj",
        "ResearchTrack.ProjectService.dll", "ProjectDbContext", "researchtrack-project"),
    "github": Service(
        "src/Services/ResearchTrack.GitHubService/ResearchTrack.GitHubService.csproj",
        "ResearchTrack.GitHubService.dll", "GitHubDbContext", "researchtrack-github"),
    "jira": Service(
        "src/Services/ResearchTrack.JiraService/ResearchTrack.JiraService.csproj",
        "ResearchTrack.JiraService.dll", "JiraDbContext", "researchtrack-jira"),
    "meeting": Service(
        "src/Services/ResearchTrack.MeetingService/ResearchTrack.MeetingService.csproj",
        "ResearchTrack.MeetingService.dll", "MeetingDbContext", "researchtrack-meeting"),
    "submission": Service(
        "src/Services/ResearchTrack.SubmissionService/ResearchTrack.SubmissionService.csproj",
        "ResearchTrack.SubmissionService.dll", "SubmissionDbContext", "researchtrack-submission"),
}

DOCKERFILE = "deploy/Dockerfile.service"
# Compiled into every image by the Dockerfile, besides the service itself.
IMAGE_TOOL_PROJECTS = ("tools/ResearchTrack.DbCheck/ResearchTrack.DbCheck.csproj",)
# Read by every image build (the Dockerfile copies the whole context).
GLOBAL_FILES = frozenset({
    DOCKERFILE,
    ".dockerignore",
    "ResearchTrack.sln",
    "dotnet-tools.json",
    ".config/dotnet-tools.json",
    ".github/workflows/backend-build-images.yml",
    "deploy/build/service-impact.py",
})
GLOBAL_PREFIXES = ("deploy/image/",)
# Applied implicitly by the SDK/MSBuild/NuGet from a project directory or any ancestor.
IMPLICIT_BUILD_FILES = frozenset(name.lower() for name in (
    "Directory.Build.props",
    "Directory.Build.targets",
    "Directory.Build.rsp",
    "Directory.Packages.props",
    "NuGet.config",
    "global.json",
    ".editorconfig",
    ".globalconfig",
))
# Never part of a service image. Checked only after the inputs above, so e.g.
# deploy/Dockerfile.service or a project a service references under tests/
# still counts.
NON_IMAGE_PREFIXES = (
    ".github/",
    "config/",
    "deploy/",
    "docs/",
    "scripts/",
    "tests/",
    "tools/ResearchTrack.DevSeeder/",
)
NON_IMAGE_ROOT_FILE = re.compile(
    r"^(?:[^/]+\.md|\.gitignore|\.gitattributes|\.env(?:\.[A-Za-z0-9_-]+)*\.example|LICENSE(?:\.[a-z]+)?)$"
)

PROJECT_REFERENCE = re.compile(r"<ProjectReference\b[^>]*?\bInclude\s*=\s*\"([^\"]+)\"", re.S)
ITEM_PATH = re.compile(r"\b(?:Include|Update|Project)\s*=\s*\"([^\"]+)\"")
PROJECT_DIR_PROPERTY = re.compile(r"\$\((?:MSBuildThisFileDirectory|MSBuildProjectDirectory)\)[\\/]?")
WILDCARD = re.compile(r"[*?]")


class GitError(RuntimeError):
    pass


def git(*args: str) -> str:
    result = subprocess.run(["git", *args], capture_output=True, text=True)
    if result.returncode != 0:
        raise GitError(f"git {' '.join(args)}: {result.stderr.strip()}")
    return result.stdout


def resolve_commit(rev: str) -> str | None:
    if not rev or re.fullmatch(r"0+", rev):
        return None
    try:
        return git("rev-parse", "--verify", "--quiet", f"{rev}^{{commit}}").strip() or None
    except GitError:
        return None


class Tree:
    """Tracked files of one commit: path -> blob id."""

    def __init__(self, rev: str):
        self.rev = rev
        self.blobs: dict[str, str] = {}
        self._text: dict[str, str] = {}
        for entry in git("ls-tree", "-r", "-z", "--full-tree", rev).split("\0"):
            if not entry:
                continue
            meta, path = entry.split("\t", 1)
            self.blobs[path] = meta.split()[2]

    def read(self, path: str) -> str | None:
        if path not in self.blobs:
            return None
        if path not in self._text:
            self._text[path] = git("cat-file", "blob", self.blobs[path])
        return self._text[path]


def _is_ancestor_dir(directory: str, of: str) -> bool:
    return directory == "" or of == directory or of.startswith(directory + "/")


@dataclass
class Scope:
    """Everything that can change one service image."""

    service: str
    # project directory -> why it is compiled into the image
    project_dirs: dict[str, str] = field(default_factory=dict)
    # external file/prefix -> project that pulls it in
    extra_files: dict[str, str] = field(default_factory=dict)
    extra_prefixes: dict[str, str] = field(default_factory=dict)
    unbounded: list[str] = field(default_factory=list)

    def reason(self, path: str) -> str | None:
        if path in GLOBAL_FILES or path.startswith(GLOBAL_PREFIXES):
            return "shared image build input"
        directory, name = posixpath.split(path)
        if name.lower() in IMPLICIT_BUILD_FILES:
            for project_dir in self.project_dirs:
                if _is_ancestor_dir(directory, project_dir):
                    return f"MSBuild/SDK configuration applied to {posixpath.basename(project_dir)}"
        for project_dir, why in self.project_dirs.items():
            if path.startswith(project_dir + "/"):
                return why
        if path in self.extra_files:
            return f"file referenced by {self.extra_files[path]}"
        for prefix, owner in self.extra_prefixes.items():
            if path.startswith(prefix):
                return f"file referenced by {owner}"
        if self.unbounded:
            return f"{self.unbounded[0]} has an item path that cannot be resolved statically"
        return None


def _normalize(base_dir: str, value: str) -> str | None:
    """Repository-relative path for an MSBuild item value, None if it leaves the repo."""
    value = PROJECT_DIR_PROPERTY.sub("", value.strip()).replace("\\", "/")
    joined = posixpath.normpath(posixpath.join(base_dir, value)) if base_dir else posixpath.normpath(value)
    if joined == ".." or joined.startswith("../") or joined.startswith("/"):
        return None
    return "" if joined == "." else joined


def build_scope(tree: Tree, service: str) -> Scope:
    scope = Scope(service)
    main = SERVICES[service].project
    pending = [(main, "service project {}")] + [(tool, "tool {} compiled into every image") for tool in IMAGE_TOOL_PROJECTS]
    seen: set[str] = set()

    while pending:
        project, why = pending.pop(0)
        if project in seen:
            continue
        seen.add(project)
        project_dir = posixpath.dirname(project)
        name = posixpath.basename(project_dir)
        scope.project_dirs.setdefault(project_dir, why.format(name))

        text = tree.read(project)
        if text is None:
            # A referenced project missing from this commit cannot be modelled.
            scope.unbounded.append(project)
            continue

        references = set()
        for value in PROJECT_REFERENCE.findall(text):
            for item in value.split(";"):
                target = _normalize(project_dir, item)
                if target is None or "$(" in item:
                    scope.unbounded.append(project)
                    continue
                references.add(target)
                pending.append((target, "referenced project {} (via " + name + ")"))

        for value in ITEM_PATH.findall(text):
            for item in filter(None, (part.strip() for part in value.split(";"))):
                if "$(" in PROJECT_DIR_PROPERTY.sub("", item):
                    # Only external-looking unknown properties are unresolvable.
                    if ".." in item or "/" in item or "\\" in item:
                        scope.unbounded.append(project)
                    continue
                target = _normalize(project_dir, item)
                if target is None:
                    scope.unbounded.append(project)
                    continue
                if target in references or _is_ancestor_dir(project_dir, target):
                    continue  # inside the project directory: already covered
                match = WILDCARD.search(target)
                if match:
                    prefix = target[: match.start()].rsplit("/", 1)[0]
                    scope.extra_prefixes[(prefix + "/") if prefix else ""] = name
                else:
                    scope.extra_files[target] = name
                    scope.extra_prefixes[target + "/"] = name
    return scope


def is_non_image(path: str) -> bool:
    return path.startswith(NON_IMAGE_PREFIXES) or bool(NON_IMAGE_ROOT_FILE.match(path))


class Model:
    def __init__(self, trees: list[Tree]):
        self.scopes = {service: [build_scope(tree, service) for tree in trees] for service in SERVICES}

    def reasons(self, path: str) -> dict[str, str]:
        """service -> reason the path is one of its image inputs ({} if none)."""
        found: dict[str, str] = {}
        for service, scopes in self.scopes.items():
            for scope in scopes:
                reason = scope.reason(path)
                if reason:
                    found[service] = reason
                    break
        return found

    def classify(self, path: str) -> dict[str, str]:
        found = self.reasons(path)
        if found or is_non_image(path):
            return found
        reason = "unclassified path (not owned by a known project, not a known non-image path) - rebuilding conservatively"
        return {service: reason for service in SERVICES}


def impact(base: str | None, head: str, force: bool) -> tuple[dict[str, list[str]], list[str]]:
    """service -> reasons (empty list = unaffected), plus the changed files."""
    result: dict[str, list[str]] = {service: [] for service in SERVICES}
    if force:
        for service in SERVICES:
            result[service].append("force_full_build=true")
        return result, []

    head_commit = resolve_commit(head)
    if head_commit is None:
        raise GitError(f"cannot resolve head commit {head!r}")
    base_commit = resolve_commit(base) if base else None
    if base_commit is None:
        why = f"base commit {base!r} is not available" if base else "no base commit"
        for service in SERVICES:
            result[service].append(f"{why}; cannot compute changes safely - rebuilding")
        return result, []

    try:
        changed = [p for p in git("diff", "--name-only", "--no-renames", base_commit, head_commit).splitlines() if p]
    except GitError as error:
        for service in SERVICES:
            result[service].append(f"diff failed ({error}) - rebuilding")
        return result, []

    # Deleted/moved projects are modelled from the base tree, new ones from head.
    model = Model([Tree(head_commit), Tree(base_commit)])
    for path in changed:
        for service, reason in model.classify(path).items():
            result[service].append(f"{path} ({reason})")
    return result, changed


def fingerprints(model: Model, tree: Tree) -> dict[str, str]:
    """service -> sha256 over its image inputs (blob id + path) in this commit."""
    digests = {}
    for service, meta in SERVICES.items():
        digests[service] = hashlib.sha256()
        digests[service].update(f"model={MODEL_VERSION}\nservice={service}\nproject={meta.project}\n"
                                f"dll={meta.dll}\ndb_context={meta.db_context}\n".encode())
    for path in sorted(tree.blobs):
        for service in model.classify(path):
            digests[service].update(f"{tree.blobs[path]} {path}\n".encode())
    return {service: digest.hexdigest() for service, digest in digests.items()}


def image_tag(fp: str) -> str:
    return f"src-{fp[:40]}"


def image_published(checker: str, reference: str) -> bool:
    try:
        result = subprocess.run(
            [checker, "buildx", "imagetools", "inspect", reference, "--format", "{{json .Manifest}}"],
            capture_output=True, text=True, timeout=120)
    except (OSError, subprocess.TimeoutExpired):
        return False
    return result.returncode == 0


# ---------------------------------------------------------------------------
# Reporting
# ---------------------------------------------------------------------------
def summarize(reasons: list[str], limit: int = 5) -> str:
    shown = "; ".join(reasons[:limit])
    return shown + (f"; +{len(reasons) - limit} more" if len(reasons) > limit else "")


def print_impact(result: dict[str, list[str]], changed: list[str], stream=sys.stdout) -> None:
    if changed:
        print(f"Changed files ({len(changed)}):", file=stream)
        for path in changed:
            print(f"  {path}", file=stream)
        print(file=stream)
    print("Service impact:", file=stream)
    for service, reasons in result.items():
        if reasons:
            print(f"  {service:<11} true  - {summarize(reasons)}", file=stream)
        else:
            print(f"  {service:<11} false", file=stream)


def write_outputs(path: str | None, values: dict[str, str]) -> None:
    if not path:
        return
    with open(path, "a", encoding="utf-8") as handle:
        for key, value in values.items():
            handle.write(f"{key}={value}\n")


def selection_outputs(selected: list[str], image_tags: dict[str, str]) -> dict[str, str]:
    return {
        "matrix": json.dumps(selected, separators=(",", ":")),
        "services": " ".join(selected),
        "has_changes": "true" if selected else "false",
        "image_tags": json.dumps(image_tags, separators=(",", ":"), sort_keys=True),
    }


def cmd_impact(args: argparse.Namespace) -> int:
    base = args.base
    if not args.force and not base.strip("0"):
        # workflow_dispatch or a new branch has no "before": use the first parent.
        base = resolve_commit(f"{args.head}^") or ""
    result, changed = impact(base, args.head, args.force)
    print_impact(result, changed)
    selected = [service for service, reasons in result.items() if reasons]
    write_outputs(args.github_output, selection_outputs(selected, {}))
    return 0


def cmd_plan(args: argparse.Namespace) -> int:
    head = resolve_commit(args.head)
    if head is None:
        raise GitError(f"cannot resolve head commit {args.head!r}")
    tree = Tree(head)
    model = Model([tree])

    image_tags: dict[str, str] = {}
    selected: list[str] = []
    rows: list[tuple[str, str, str, str]] = []
    print(f"Source fingerprints for {head}:")
    for service, fp in fingerprints(model, tree).items():
        meta = SERVICES[service]
        tag = image_tag(fp)
        image_tags[service] = tag
        reference = f"{args.image_prefix}/{meta.image}:{tag}"
        if args.force:
            action, reason = "build", "force_full_build=true"
        elif not image_published(args.checker, reference):
            action, reason = "build", "no published image for the current source fingerprint"
        else:
            action, reason = "reuse", "image for the current source fingerprint already published"
        if action == "build":
            selected.append(service)
        rows.append((service, action, tag, reason))
        print(f"  {service:<11} {action:<6} {tag}  {reason}")

    # Context only: what changed in this push. Selection never depends on it.
    if args.base and not args.force:
        print()
        print(f"Changes since {args.base} (context; selection uses fingerprints):")
        try:
            result, changed = impact(args.base, head, False)
            print_impact(result, changed)
        except GitError as error:
            print(f"  unavailable: {error}")

    write_outputs(args.github_output, selection_outputs(selected, image_tags))
    if args.summary:
        with open(args.summary, "a", encoding="utf-8") as handle:
            handle.write(f"### Backend image selection\n\nSource: `{head}`\n\n")
            handle.write("| Service | Action | Image tag | Reason |\n|---|---|---|---|\n")
            for service, action, tag, reason in rows:
                handle.write(f"| {service} | {action} | `{tag}` | {reason} |\n")
            handle.write("\n")
    return 0


def cmd_meta(args: argparse.Namespace) -> int:
    meta = SERVICES.get(args.service)
    if meta is None:
        print(f"Unknown service: {args.service}", file=sys.stderr)
        return 1
    print(f"project={meta.project}")
    print(f"dll={meta.dll}")
    print(f"context={meta.db_context}")
    print(f"image={meta.image}")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)

    common = argparse.ArgumentParser(add_help=False)
    common.add_argument("--head", default="HEAD")
    common.add_argument("--base", default="")
    common.add_argument("--force", default="false", type=lambda v: v.lower() == "true")
    common.add_argument("--github-output", default=os.environ.get("GITHUB_OUTPUT"))

    sub.add_parser("impact", parents=[common]).set_defaults(func=cmd_impact)
    plan = sub.add_parser("plan", parents=[common])
    plan.add_argument("--image-prefix", required=True)
    plan.add_argument("--checker", default="docker")
    plan.add_argument("--summary", default=os.environ.get("GITHUB_STEP_SUMMARY"))
    plan.set_defaults(func=cmd_plan)
    meta = sub.add_parser("meta")
    meta.add_argument("--service", required=True)
    meta.set_defaults(func=cmd_meta)

    args = parser.parse_args(argv)
    try:
        return args.func(args)
    except GitError as error:
        print(f"error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
