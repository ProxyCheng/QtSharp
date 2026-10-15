# Contributing to QtSharp

Thank you for helping build a practical Qt binding for .NET. The project is Windows x64 today,
uses Qt 6.11.x and .NET 10, and treats the checked-in API model as a reproducible contract.

## Before opening an issue

Search existing issues and read the relevant section of `README.md`. Include the
Qt version, .NET SDK version, Visual Studio toolset, architecture, command used, and the complete
error or crash signature. For native crashes, include the smallest reproducible demo and the
generated/native log when possible. Never attach secrets, proprietary Qt files, or customer data.

## Development setup

1. Install the prerequisites listed in `README.md`.
2. Place the Qt development kit and clang builtin headers in the documented locations, or set the
   corresponding environment variables.
3. Run `powershell -File tools\regen-api.ps1` from any directory.
4. Open `QtSharp.slnx` or build the individual projects with `dotnet build -c Release`.

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

By contributing, you agree that your contribution is provided under the MIT License in `LICENSE`.
