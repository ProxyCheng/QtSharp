# QtSharp.Qt6

> The package id carries the platform: the snippets below use the `win-x64` variant. On
> Linux, use the matching `.linux-x64` package (`QtSharp.Api.linux-x64`, ...).

The Qt 6 runtime the QtSharp bindings load (the `PyQt6-Qt6` slot).

| RID | payload |
|---|---|
| `win-x64` | Qt6Core, Qt6Gui, Qt6Widgets, Qt6Network, Qt6Svg, Qt6SvgWidgets, Qt6PrintSupport, Qt6OpenGL, Qt6OpenGLWidgets, the MSVC runtime, and the Windows platform plugin (`platforms\qwindows.dll`) |
| `linux-x64` | the whole staged Qt tree: `lib/libQt6*.so.6` (addons included), the ICU/OpenSSL/FFmpeg libraries Qt bundles, `plugins/` (platform integration, image formats, styles, TLS, Qt3D, multimedia) and `qt.conf` |

It carries no managed code. `QtSharp.Api` depends on it, so referencing the binding package is
enough; the payload is copied next to your executable on build and publish. Windows gets the flat
DLL layout Qt expects there; Linux gets the prefix layout (`lib/`, `plugins/`, `qt.conf`) that the
libraries' and plugins' `$ORIGIN` rpaths expect, so no `LD_LIBRARY_PATH` is needed.

On Linux the application still needs the system X11/OpenGL libraries Qt itself needs
(`libxcb-*`, `libxkbcommon-x11-0`, `libgl1` or `libegl1`, `libdbus-1-3`, fonts), exactly like any
other Qt application.
