// The binding tests use the base modules directly and the hosting library for the shared pixel
// helper. The runtime carries QSignal and the declaration attributes.
global using QtSharp.Runtime;
global using QtSharp.QtCore;
global using QtSharp.QtGui;
global using QtSharp.QtWidgets;
global using QtSharp.QtNetwork;
global using QtSharp.QtPositioning;
global using QtSharp.QtSql;
global using QtSharp.QtSvg;
global using QtSharp.QtSvgWidgets;
global using QtSharp.QtPrintSupport;
global using QtSharp.QtMultimedia;
global using QtSharp.Examples.Hosting;
global using QtSharp.Tests;

// Only QtCore declares a static `Qt` class: every other module names its free-function class
// after the module, so `Qt.Alignment` therefore resolves on its own, and no alias is needed here -
// if a second module ever publishes a `Qt` again, these files stop compiling, which is the
// regression test for issue #8.
