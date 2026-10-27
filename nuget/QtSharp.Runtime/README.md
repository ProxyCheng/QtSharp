# QtSharp.Runtime

> The package id carries the platform: the snippets below use the `win-x64` variant. On
> Linux, use the matching `.linux-x64` package (`QtSharp.Api.linux-x64`, ...).

The runtime every QtSharp binding package builds on — the equivalent of `PyQt6-sip`.

It contains:

| | |
|---|---|
| `lib/net10.0/QtSharp.Runtime.dll` | handles, `QSignal`, `[QtProperty]` / `[QtSignal]`, the wrapper cache and the marshalling helpers |
| `tools/native/win-x64/QtSharp.Api.dll` | the shared native glue the base modules call (in the `win-x64` package) |
| `tools/native/linux-x64/libQtSharp.Api.so` | the same glue compiled with g++ (in the `linux-x64` package) |

It has no Qt types of its own, so it can be referenced by the base assemblies and by every addon
alike. You normally do not reference it directly: `QtSharp.Api` (or an addon package) brings it in.

The Qt runtime that the glue loads is in `QtSharp.Qt6` (or an addon's companion `*.Qt6` package on
Windows); those packages copy everything next to your executable on build and publish, in the
layout each platform expects (flat DLLs + `platforms/` on Windows, `lib/` + `plugins/` + `qt.conf`
on Linux).
