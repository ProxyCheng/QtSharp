#pragma once

// Shared contract between the generated native bindings and the hand-written runtime: the dispatch
// signature every generated subclass calls, and nothing else.
//
// The plain-old-data mirrors of Qt's value types (QsApiPointF, QsApiSize, QsApiRect, ...) are
// *generated* into each binding's own translation unit instead of living here: the table in
// gen/ApiModel.cs is their single source of truth, so every binding - the core and each extension
// module - carries exactly the ones its model has, and growing that table cannot leave a module
// compiling against a stale copy of this header.
typedef void (*QsApiDispatch)(void* userData, int methodId, void** argv);
