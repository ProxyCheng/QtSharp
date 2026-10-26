// The binding is compiled into QtSharp.Demos; the binding tests reference it for the types and
// for the shared GrabCovers helper. The runtime carries QSignal and the declaration attributes.
global using QtSharp.Runtime;
global using QtSharp.QtCore;
global using QtSharp.QtGui;
global using QtSharp.QtWidgets;
global using QtSharp.QtNetwork;
global using QtSharp.QtSql;
global using QtSharp.QtSvg;
global using QtSharp.QtSvgWidgets;
global using QtSharp.QtPrintSupport;
global using QtSharp.QtXml;
global using QtSharp.QtOpenGL;
global using QtSharp.QtOpenGLWidgets;
global using QtSharp.Demos;

// Every module has a static Qt class for the free functions it declares; the namespace enums these
// tests use are QtCore's, so the alias keeps `Qt.Alignment` unambiguous.
global using Qt = QtSharp.QtCore.Qt;
