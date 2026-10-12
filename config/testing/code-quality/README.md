# Code-quality policy

- Tool pin: `scripts/code-quality/.config/dotnet-tools.json` (CSharpier 1.3.0).
- C# formatter config: `.csharpierrc` and `.csharpierignore` at repository root.
- Roslyn source IDE style: `src/.editorconfig` (only production source).
- Roslyn review-only profile: `roslyn.globalconfig`, attached to `src/Directory.Build.targets` only for `ResearchTrackQualityAnalysis=true`.
- Scripts: `bash ./scripts/code-quality.sh help`.
- No analyzer or formatting changes are made to any of the existing test/mutation/load projects.
