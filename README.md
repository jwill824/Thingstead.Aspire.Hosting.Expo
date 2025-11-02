# Thingstead.Aspire.Hosting.Expo

Helpers to integrate expo with Aspire hosting.

## Overview

This project produces a NuGet package and publishes it to GitHub Packages via GitHub Actions. The workflow automatically applies semantic versioning and creates GitHub Releases with release notes.

## Usage

The following snippet shows a typical usage pattern. Add a parameter to hold the ngrok auth token,
add an `Expo` resource, configure the default forwarding command, and wait for the public URL.

```csharp

```

## Consuming the package and managing a local PAT (1Password)

This repository provides `NuGet.config.template` with the GitHub Packages feed URL and `packageSourceMapping` for `Thingstead.Aspire.Hosting.Ngrok`. Do NOT commit a `NuGet.config` that contains credentials.

Recommended minimal flow (manual PAT insertion via 1Password):

1. Create a GitHub Personal Access Token (PAT) with the minimum scope you need:
   - For consuming packages: `read:packages`
   - For publishing packages: `write:packages` (and add `repo` or other scopes only if required)

2. Store the PAT in 1Password (or another secret manager). Name the item `GitHub NuGet PAT` or similar.

3. Retrieve the PAT using the 1Password CLI and copy it to your clipboard or paste it directly into a `NuGet.config` copied from the template.

Example `op` commands (1Password CLI):

```bash
# Simple field fetch (if item name is exactly 'GitHub NuGet PAT')
op item get "GitHub NuGet PAT" --field password

# Or, fetch JSON and extract the password field (robust for scripts)
op item get "GitHub NuGet PAT" --format json | jq -r '.fields[] | select(.name=="password") | .value'
```

4. Create a local `NuGet.config` from the template and paste the token into the credentials block (do NOT commit this file).

Example (edit a copy of `NuGet.config.template` and replace placeholders):

```xml
<packageSourceCredentials>
  <github>
    <add key="Username" value="OWNER" />
    <add key="ClearTextPassword" value="<PASTE_TOKEN_HERE>" />
  </github>
</packageSourceCredentials>
```

5. Run the add/restore command locally:

```bash
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet restore
# or
dotnet add package Thingstead.Aspire.Hosting.Expo --version 0.1.0
```

## Semantic versioning (automated)

The workflow follows semantic versioning (semver) and uses Conventional Commit heuristics to determine the next version when you push to `main`:

- Breaking changes (commit message contains `BREAKING CHANGE` or a `!` in the header) → major bump
- `feat(...)` or `feat:` commit headers → minor bump
- Everything else → patch bump

If there are no prior `vMAJOR.MINOR.PATCH` tags, the workflow starts at `0.1.0`.

When a version is determined the workflow will:

- create and push an annotated tag `vMAJOR.MINOR.PATCH`
- pack the project with that version
- publish the package to GitHub Packages
- create a GitHub Release and attach the produced `.nupkg` as a release asset

If you prefer a manual release instead, you can create and push a tag (for example `v1.2.0`) and the workflow will use that tag's version.

> [!WARNING]
> Never commit `NuGet.config` containing `ClearTextPassword` to source control. Keep credentials in your password manager or Keychain and use the template in this repo.

### How to influence version bumps

Use Conventional Commit style messages to influence the bump type:

- `feat:` — new feature → minor bump
- `fix:` — bug fix → patch bump
- Add `BREAKING CHANGE:` in the commit body or use `!` in the header (for example `feat!: ...`) → major bump

Examples:

```bash
# feature -> bump minor
git commit -m "feat(api): add new connection option"

# fix -> bump patch
git commit -m "fix(docs): correct README example"

# breaking change -> bump major
git commit -m "feat!: change public API" -m "BREAKING CHANGE: args changed"
```

### Dry-run

You can test the release pipeline without creating tags or publishing by using the workflow_dispatch input `dry_run=true` in the Actions UI. That runs `semantic-release --dry-run` and prints the computed version and proposed changelog.

### Resources

- [semantic-release (npm)](https://www.npmjs.com/package/semantic-release)
- [semantic-release docs](https://semantic-release.gitbook.io/)
- [Conventional Commits](https://www.conventionalcommits.org/)