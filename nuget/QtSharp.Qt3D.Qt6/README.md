# QtSharp.Qt3D.Qt6

The Qt 3D runtime for `QtSharp.Qt3D`: Qt63DCore/Render/Input/Logic/Animation/Extras,
Qt6Concurrent, Qt6ShaderTools, and the renderer, geometry-loader and scene-parser plugins that
Qt3DRender loads. It carries no managed code; the payload is copied next to your executable on
build and publish.

On Linux this package has no payload of its own: the base `QtSharp.Qt6` package deploys the whole
Qt tree, addon libraries and plugins included.
