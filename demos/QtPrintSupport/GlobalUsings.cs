global using QtSharp.Demos.Hosting;
global using QtSharp.Runtime;
global using QtSharp.QtCore;
global using QtSharp.QtGui;
global using QtSharp.QtWidgets;
global using QtSharp.QtPrintSupport;

// Only QtCore declares a static `Qt` class; every other module names its free-function class
// after the module, so `Qt.Key`, `Qt.Alignment`, ... resolve on their own.
