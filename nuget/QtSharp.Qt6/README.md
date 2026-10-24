# QtSharp.Qt6

The Qt 6 runtime the QtSharp bindings load (the `PyQt6-Qt6` slot): Qt6Core, Qt6Gui, Qt6Widgets,
Qt6Network, Qt6Svg, Qt6SvgWidgets, Qt6PrintSupport, Qt6Xml, Qt6OpenGL, Qt6OpenGLWidgets, the
MSVC runtime, and the Windows platform plugin (`platforms\qwindows.dll`).

It carries no managed code. `QtSharp.Api` depends on it, so referencing the binding package is
enough; the payload is copied next to your executable on build and publish.
