# Public GitHub repository linking

Public repository creation must not depend on GitHub REST API quota.

## Linking path

1. Parse and validate the `github.com/{owner}/{repository}` URL locally.
2. Verify anonymous cloneability through Git smart HTTP (`git-upload-pack` discovery on `github.com`).
3. Store a deterministic negative provisional repository id.
4. Allow the project repository link to be created immediately.
5. Queue synchronization separately.

## Synchronization path

- GitHub App sources use installation access tokens.
- Public URL sources may use anonymous REST API access for rich metadata.
- If GitHub's anonymous REST quota is exhausted, the repository remains linked and existing synchronized data is preserved.
- Failed automatic syncs cool down for one hour before scheduled retry.
- Once a public sync can reach the REST API, the provisional negative repository id is transactionally promoted to the real positive GitHub repository id.
- Anonymous rate-limit reset information is cached in-process so repeated operations do not continue sending requests before the reset time.

No personal access token is required for the public linking business flow.
