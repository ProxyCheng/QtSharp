// The sample references the demo library (which compiles the binding), so it imports the binding
// namespaces itself: one per Qt module, plus the runtime and the demo dispatcher.
global using QtSharp.Runtime;
global using QtSharp.QtCore;
global using QtSharp.QtGui;
global using QtSharp.QtWidgets;
global using QtSharp.Demos;

// Each module has its own static Qt class for the free functions it declares (qHash of a QFont
// is QtGui's). The Qt namespace enums a UI program uses are QtCore's, so one alias keeps `Qt.Key`,
// `Qt.Alignment`, ... unambiguous when several module namespaces are imported.
global using Qt = QtSharp.QtCore.Qt;
