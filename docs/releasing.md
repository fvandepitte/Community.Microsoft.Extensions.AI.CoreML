# Releasing

This repository ships three workflows:

- `CI` (`.github/workflows/ci.yml`) for restore/build/test on push and pull request.
- `Package` (`.github/workflows/package.yml`) for building the native bridge and packing NuGet artifacts.
- `Release` (`.github/workflows/release.yml`) for tag-triggered publication to NuGet.

## Tagging convention

Release tags must start with `v` and the remainder is used as package version:

- `v1.2.3` -> `1.2.3`
- `v1.2.3-preview1` -> `1.2.3-preview1`

## Required repository configuration

### Secrets

- `NUGET_API_KEY` (repository or environment secret): API key with permission to publish `Community.Microsoft.Extensions.AI.CoreML` on NuGet.org.

### Environment

- Environment name: `release` (used by `release.yml`).
- Recommended: configure required reviewers on the `release` environment for gated production publishes.

### Runner requirement

The package/release flow requires an Apple Silicon macOS runner label: `macos-latest-xlarge`.

## How to cut a release

1. Ensure `CI` is green on `main`.
2. Create and push a tag using the release version format (`vX.Y.Z` or `vX.Y.Z-suffix`).
3. Wait for the `Release` workflow to finish.
4. Verify:
   - NuGet package and symbols are published.
   - Artifacts are attached to the GitHub release.
