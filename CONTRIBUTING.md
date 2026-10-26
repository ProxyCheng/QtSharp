# Contributing to QtSharp

Thank you for helping build a practical Qt binding for .NET. The project targets Windows x64 and
Linux x64 (glibc), uses Qt 6.12.x and .NET 10, and treats the checked-in API model as a reproducible
contract.

## Before opening an issue

Search existing issues and read the relevant section of `README.md`. Include the
Qt version, .NET SDK version, platform, compiler/toolset, architecture, command used, and the
complete error or crash signature. For native crashes, include the smallest reproducible demo and
the generated/native log when possible. Never attach secrets, proprietary Qt files, or customer
data.

## Development setup

1. Install the prerequisites listed in `README.md` for your platform.
2. Place the Qt development kit and clang builtin headers in the documented locations, or set the
   corresponding environment variables. On Linux, `tools/build-qt-kit.sh` stages both the kit and
   the runtime tree.
3. Run `powershell -File tools\regen-api.ps1` (Windows) or `pwsh ./tools/regen-api.ps1` (Linux)
   from any directory. Model extraction is Windows-only; a Linux run uses the checked-in model and
   skips that step.
4. Open `QtSharp.slnx` or build the individual projects with `dotnet build -c Release`.

Platform-specific code lives in platform-specific files, and the build compiles exactly one:
`gen/HostPlatform.Windows.cs` / `HostPlatform.Unix.cs`, `tests/TestPlatform.Windows.cs` /
`.Unix.cs`, and `build_api.bat` / `build_api.sh`. Do not put `OperatingSystem` checks in shared
sources. The managed runtime has no platform branch at all - the deployed `qt.conf` is what makes
Qt find its plugins on Linux.

The regeneration gate checks model byte identity and deterministic code generation. Do not edit
`out/`; it is generated output. Changes to the API surface normally belong in `gen/`, the model
roots, or the handwritten runtime, followed by a fresh gate run.

## Pull requests

- Keep each pull request focused and explain the user-visible or binding-level effect.
- Add or update a conformance demo when a change adds or fixes supported Qt behavior.
- Update `README.md` or `NOTICE.md` when setup, licensing, or architecture changes.
- Run the narrowest meaningful check locally, and report checks that could not run because the
  required Qt kit or toolchain was unavailable.
- Do not commit `Qt/`, `lib/`, `out/`, `bin/`, `obj/`, generated native binaries, or secrets.
- Generated model changes must be intentional. Include the extraction/codegen result in the PR
  description and explain any coverage change.

## Releases

A release is a semantic version plus a `v<version>` tag, and every release that changes the binding
gets its own version: patch for fixes that leave the API alone, minor for API additions and Qt
version upgrades. Do not reuse a version that is already published - bump it instead.

The version is passed to the release workflow, which builds and smoke-tests every package from the
checked-in models before publishing, and creates or updates the GitHub Release:

```powershell
gh workflow run release.yml -f version=0.3.0
```

Pushing a `v<version>` tag does the same. The `Version` properties in the package projects and the
defaults in `tools/pack-*.ps1` follow the released version; the workflow overrides them from the
version it was given.

By contributing, you agree that your contribution is provided under the MIT License in `LICENSE`.
