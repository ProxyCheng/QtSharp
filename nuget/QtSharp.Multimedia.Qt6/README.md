# QtSharp.Multimedia.Qt6

> The package id carries the platform: the snippets below use the `win-x64` variant. On
> Linux, use the matching `.linux-x64` package (`QtSharp.Api.linux-x64`, ...).

The Qt Multimedia runtime (`Qt6Multimedia.dll`, `Qt6MultimediaWidgets.dll` and the backend plugin
`multimedia\ffmpegmediaplugin.dll`) loaded by `QtSharp.Multimedia`. Brought in automatically;
reference it only if you build the binding module yourself.

On Linux this package has no payload of its own: the base `QtSharp.Qt6` package deploys the whole
Qt tree, addon libraries and plugins included.
