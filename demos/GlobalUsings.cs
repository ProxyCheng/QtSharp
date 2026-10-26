global using QtSharp.Demos;
// The binding is ten assemblies/namespaces now (QtSharp.Api), so the ported examples import the
// modules they use once here instead of in forty-five files. QtSharp.Runtime carries QSignal and
// the [QtProperty]/[QtSignal] attributes.
global using QtSharp.Runtime;
global using QtSharp.QtCore;
global using QtSharp.QtGui;
global using QtSharp.QtWidgets;
global using QtSharp.QtNetwork;
global using QtSharp.QtMultimedia;
global using QtSharp.QtPositioning;
global using QtSharp.QtSvg;
global using QtSharp.QtSvgWidgets;
global using QtSharp.QtPrintSupport;
global using QtSharp.QtOpenGL;
global using QtSharp.QtOpenGLWidgets;

// Only QtCore declares a static `Qt` class: every other module names its free-function class
// after the module (QtGui, QtWidgets, ...). `Qt.Key`, `Qt.Alignment`, ... therefore resolve on
// their own, and a second module publishing a `Qt` would break these files - the compile-level
// regression test for issue #8.
