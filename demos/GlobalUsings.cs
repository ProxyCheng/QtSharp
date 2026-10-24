global using QtSharp.Demos;
// The binding is ten assemblies/namespaces now (QtSharp.Api), so the ported examples import the
// modules they use once here instead of in forty-five files. QtSharp.Runtime carries QSignal and
// the [QtProperty]/[QtSignal] attributes.
global using QtSharp.Runtime;
global using QtSharp.QtCore;
global using QtSharp.QtGui;
global using QtSharp.QtWidgets;
global using QtSharp.QtNetwork;
global using QtSharp.QtSvg;
global using QtSharp.QtSvgWidgets;
global using QtSharp.QtPrintSupport;
global using QtSharp.QtXml;
global using QtSharp.QtOpenGL;
global using QtSharp.QtOpenGLWidgets;

// Each module has its own static Qt class for the free functions it declares (qHash of a QFont
// is QtGui's). The Qt namespace enums a UI program uses are QtCore's, so one alias keeps `Qt.Key`,
// `Qt.Alignment`, ... unambiguous when several module namespaces are imported.
global using Qt = QtSharp.QtCore.Qt;
