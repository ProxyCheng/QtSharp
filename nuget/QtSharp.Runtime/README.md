# QtSharp.Runtime

The runtime every QtSharp binding package builds on — the equivalent of `PyQt6-sip`.

It contains:

| | |
|---|---|
| `lib/net10.0/QtSharp.Runtime.dll` | handles, `QSignal`, `[QtProperty]` / `[QtSignal]`, the wrapper cache and the marshalling helpers |
| `tools/native/QtSharp.Api.dll` | the shared native glue the base modules call |

It has no Qt types of its own, so it can be referenced by the base assemblies and by every addon
alike. You normally do not reference it directly: `QtSharp.Api` (or an addon package) brings it in.

The Qt runtime DLLs that the glue loads are in `QtSharp.Qt6` (or an addon's companion `*.Qt6`
package); those packages copy everything next to your executable on build and publish.
