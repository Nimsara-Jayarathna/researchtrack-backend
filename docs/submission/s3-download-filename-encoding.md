# S3 download/preview filename encoding — October 2026

## Problem

S3 responds with `400 InvalidArgument` and
`Header value cannot be represented using ISO-8859-1` when the
`response-content-disposition` presigned URL parameter contains unescaped
Unicode such as the em dash in:

`Group 26 - ResearchTrack — Sprint 01 - Presentation.pdf`

## Root cause and fix

`S3ObjectStorageService.CreateDownloadGrantAsync` previously copied the
original filename directly into the Content-Disposition override, removing
only double quotes. The AWS S3 response-header override must not include raw
Unicode characters outside Latin-1.

The new `Infrastructure/DownloadContentDisposition.cs` constructs:

```http
Content-Disposition: inline; filename="Group 26 - ResearchTrack - Sprint 01 - Presentation.pdf"; filename*=UTF-8''Group%2026%20-%20ResearchTrack%20%E2%80%94%20Sprint%2001%20-%20Presentation.pdf
```

The header value is **entirely ASCII**; the `filename*` portion is RFC 5987
percent-encoded UTF-8. The AWS SDK subsequently URL-encodes that header
value when signing the complete S3 request. Do **not** manually edit or
re-encode an already signed URL.

- `inline=true` yields `inline` (browser preview).
- `inline=false` yields `attachment` (download).
- The original filename in the submission version database is untouched.
- No object/key rename, S3 migration, frontend change, or new configuration is required.
- Quotes, CR/LF/control characters and path prefixes cannot inject extra
  header parameters or paths through the ASCII fallback filename.
- Browsers that support `filename*` can retain the original Unicode filename;
  clients that do not can use the ASCII fallback.

## Validation

From the repository root, with .NET SDK matching `global.json` installed:

```bash
dotnet test tests/ResearchTrack.SubmissionService.Tests/ResearchTrack.SubmissionService.Tests.csproj --filter FullyQualifiedName~DownloadContentDispositionTests
dotnet test tests/ResearchTrack.SubmissionService.Tests/ResearchTrack.SubmissionService.Tests.csproj --filter FullyQualifiedName~S3DownloadGrantTests
```

To validate the live deployment:

1. Deploy the updated Submission Service (`rt-submission-prod` in the Azure production environment) using the normal reviewed backend deployment workflow.
2. Request a **new** preview URL for the existing file with an em dash in its name, and open it. The S3 response should be `200 OK` with an `inline` disposition, rather than `400 InvalidArgument`.
3. Request a new download URL for the same file and confirm a successful download and a sensible filename. Repeat with a Sinhala filename if available.
4. Confirm ASCII filenames still work, and `inline`/`attachment` behavior has not changed.

**Operational caveat:** An earlier signed URL containing the invalid header remains invalid even after the code is deployed. Generate a new URL. Real S3 validation requires a deployed environment and a test object; the offline unit tests do not access AWS.
