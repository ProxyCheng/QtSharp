# QtSharp.Charts.Qt6

> The package id carries the platform: the snippets below use the `win-x64` variant. On
> Linux, use the matching `.linux-x64` package (`QtSharp.Api.linux-x64`, ...).

The Qt Charts runtime (`Qt6Charts.dll`) for `QtSharp.Charts`, split out the way
`PyQt6-Charts-Qt6` is split from `PyQt6-Charts`. It carries no managed code; the DLL is copied
next to your executable on build and publish.

On Linux this package has no payload of its own: the base `QtSharp.Qt6` package deploys the whole
Qt tree, addon libraries and plugins included.
