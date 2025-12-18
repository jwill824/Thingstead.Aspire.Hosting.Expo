CONTRIBUTING
============

This repository includes guidelines for consuming the package, local testing, and the automated release pipeline.

Consuming the package and managing a local PAT
---------------------------------------------

This repository provides `NuGet.config.template` with the GitHub Packages feed URL and `packageSourceMapping` for `Thingstead.Aspire.Hosting.Ngrok`.

Do NOT commit a `NuGet.config` that contains credentials.

Recommended minimal flow (manual PAT insertion via a password manager):

1. Create a GitHub Personal Access Token (PAT) with the minimum scope you need:
   - For consuming packages: `read:packages`
   - For publishing packages: `write:packages` (and add `repo` or other scopes only if required)

2. Store the PAT in your password manager and copy it when needed.

3. Create a local `NuGet.config` from the template and add the PAT into the credentials block (do NOT commit this file).

4. Run the add/restore command locally:

```bash
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet restore
# or
dotnet add package Thingstead.Aspire.Hosting.Ngrok --version 0.1.0
```

Semantic versioning and automated releases
-----------------------------------------

The GitHub Actions workflow in this repo applies semantic versioning automatically when publishing packages. It uses Conventional Commits to select the next version:

- Breaking changes (commit body contains `BREAKING CHANGE` or a `!` in the header) → major bump
- `feat(...)` or `feat:` commit headers → minor bump
- Everything else → patch bump

If there are no prior `vMAJOR.MINOR.PATCH` tags, the workflow starts at `0.1.0`.

The workflow will create the tag, pack the project, publish to GitHub Packages, and create a GitHub Release with the `.nupkg` attached.

If you want to influence releases locally, follow Conventional Commits when creating PRs/commits. For dry runs, use the Actions UI input `dry_run=true` to preview the computed version and changelog.

Developer notes
---------------

- Run the unit tests locally with:

```bash
dotnet test Thingstead.Aspire.Hosting.Ngrok.Tests
```

- Make sure to update unit tests or API documentation when changing public APIs (this repository contains unit tests that assert the public API behavior).

- When opening a PR, include a short summary and reference any breaking changes explicitly using `BREAKING CHANGE` in the commit body (or `!` in the header) so the release tooling will pick up the proper version bump.

Resources
---------

- [semantic-release (npm)](https://www.npmjs.com/package/semantic-release)
- [Conventional Commits](https://www.conventionalcommits.org/)
  