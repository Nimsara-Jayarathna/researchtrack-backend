# Mutation tool manifest resolution fix

The repository previously contained two root-level local-tool manifests:

- `.config/dotnet-tools.json` with ReportGenerator and Stryker
- `dotnet-tools.json` with `dotnet-ef`

Because both were marked `isRoot: true`, commands executed from nested mutation-test directories could resolve the wrong manifest and fail with `dotnet-stryker does not exist`.

The repository now uses one canonical manifest only:

`.config/dotnet-tools.json`

It contains:

- `dotnet-ef` 10.0.9
- `dotnet-reportgenerator-globaltool` 5.5.11
- `dotnet-stryker` 5.0.0

`mutation-baseline.sh` explicitly restores that manifest and invokes Stryker with `dotnet tool run dotnet-stryker -- ...`. This avoids directory-dependent tool resolution.
