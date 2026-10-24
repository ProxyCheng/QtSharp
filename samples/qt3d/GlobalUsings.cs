// The sample references the binding assemblies directly (QtSharp.Qt3D's modules and the base
// modules they build on). QtSharp.Runtime carries QSignal and the declaration attributes.
global using QtSharp.Runtime;
global using QtSharp.QtCore;
global using QtSharp.QtGui;
global using QtSharp.QtWidgets;

// Each module has its own static Qt class for the free functions it declares (qHash of a QFont
// is QtGui's). The Qt namespace enums a UI program uses are QtCore's, so one alias keeps `Qt.Key`,
// `Qt.Alignment`, ... unambiguous when several module namespaces are imported.
global using Qt = QtSharp.QtCore.Qt;
