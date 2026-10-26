// The Qt3D demos use the Qt3D modules and the base modules they build on. QtSharp.Runtime carries
// QSignal and the declaration attributes.
global using QtSharp.Runtime;
global using QtSharp.Demos;
global using QtSharp.QtCore;
global using QtSharp.QtGui;
global using QtSharp.QtWidgets;

// Only QtCore declares a static `Qt` class; the other modules name theirs after the module, so
// `Qt.Key`, `Qt.Alignment`, ... resolve unambiguously without an alias.
