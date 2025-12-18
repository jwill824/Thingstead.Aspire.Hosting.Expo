# Thingstead.Aspire.Hosting.Expo

Helpers to integrate Expo with Aspire hosting.

## Overview

This project provides a small library with helpers to add an Expo frontend as a container resource to an Aspire distributed application. It packages small runtime assets (a `Dockerfile`, an entrypoint script and optional instrumentation) into the NuGet package so consumers only need to point at their local project folder as the Docker build context.

The project produces a NuGet package and the repository contains GitHub Actions workflows that apply semantic versioning and publish packages to GitHub Packages.

## Usage

Typical usage is to create an `ExpoOptions` instance, point `BuildContext` at your Expo project's folder, and call `AddExpo(...)` on your distributed application builder. The library will register an HTTP endpoint, set environment variables used by the packaged Dockerfile and provide a command to generate/open a QR code for the published URL.

Example (conceptual):

```csharp
var expoOptions = new ExpoOptions
{
    ResourceName = "expo",
    BuildContext = "../MyExpoApp",
    UriCallback = () => "https://example.com",
    Port = 8082,
    TargetPort = 8082
};

builder.AddExpo(expoOptions);
```

The library is intentionally lightweight; `AddExpo` accepts a consumer-supplied build context and supplies the packaged `Dockerfile` as the override Dockerfile path so COPY instructions targeting the entrypoint and instrumentation succeed even when the consumer doesn't include those files.

## Consuming the package

This repository provides `NuGet.config.template` with the GitHub Packages feed URL. Do NOT commit a `NuGet.config` that contains credentials.

Recommended minimal flow (manual PAT insertion via a password manager):

1. Create a GitHub Personal Access Token (PAT) with the minimum scope you need:
   - For consuming packages: `read:packages`
   - For publishing packages: `write:packages` (and add `repo` or other scopes only if required)

2. Store the PAT in your password manager and copy it when needed.

3. Create a local `NuGet.config` from `NuGet.config.template` and paste the token into the credentials block (do NOT commit this file).

4. Restore or add the package locally:

```bash
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet restore
# or
dotnet add package Thingstead.Aspire.Hosting.Expo --version 0.1.0
```

## How the Dockerfile and entrypoint work

- `Dockerfile` (packaged and embedded): the project includes a small Node-based image that installs dependencies and runs `npx expo start` exposing the configured port. The library ships this `Dockerfile` as an embedded resource and sets it as the override dockerfile path when building the consumer project.

- `docker-entrypoint.sh` (packaged and embedded): the entrypoint script starts the Expo packager, waits until the packager HTTP endpoint is reachable (polling for readiness), and then waits for the packager process. The extension will ensure this script is present in the build context so Docker `COPY` instructions referencing it succeed.

When `AddExpo` runs it extracts the embedded resources to a temporary directory and then either points Docker at the consumer's build context directly or creates a temporary merged context containing the consumer files plus the embedded `docker-entrypoint.sh` (if they are missing from the consumer's folder). This ensures the packaged `Dockerfile` can `COPY` these files during `docker build`.

Files are extracted to `$(Temp)/aspire-expo-resources/<assemblyName>` and the library attempts to set the executable bit on Unix-like systems for the entrypoint script.

## Project layout and important files

- `Dockerfile` — Node-based image used for development and packaging; embedded into the NuGet package.
- `docker-entrypoint.sh` — Entrypoint script that launches the Expo packager and waits for readiness.
- `ExpoOptions.cs` — Options POCO used by `AddExpo` (BuildContext, Port, TargetPort, UriCallback).
- `Extensions/ExpoResourceBuilderExtensions.cs` — Extension methods that add/configure the Expo container resource and register the QR generation command.
- `Utils/FileHelpers.cs` — Helpers used for extracting embedded resources and copying directories.
- `Utils/QrUtil.cs` — QR generation helper used by the `WithQrCommand` command.

## Running tests

Run tests locally with:

```bash
dotnet test Thingstead.Aspire.Hosting.Expo.Tests
```

The test project includes unit tests that exercise `ExpoOptions`, `FileHelpers`, `QrUtil` and private helpers on `ExpoResourceBuilderExtensions` via reflection so the library doesn't need to pull in runtime infrastructure.

## Contributing

See `CONTRIBUTING.md` for contribution guidelines, testing instructions, and release details.
