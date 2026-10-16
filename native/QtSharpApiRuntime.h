#pragma once

// Shared contract between the generated native bindings and the hand-written runtime.
typedef void (*QsApiDispatch)(void* userData, int methodId, void** argv);

// The plain-old-data twins of Qt's value types. A QPointF is two doubles and a QPoint two ints; C++
// passes them by value and PyQt and PySide model them as value types, so the managed side is a
// struct with exactly this layout (runtime/QtStructs.cs) and it crosses the boundary as itself -
// no handle, no allocation, no registration, nothing for the caller to release. The glue keeps one
// of these per value type and the generated code rebuilds Qt's own type from it at the call.
//
// The field order is the *memory* order of the Qt type, because an array of values crosses as these
// bytes (the generated glue reinterprets them as Qt's own). For every type here that is also the
// order its accessors have - except QRect, which keeps its two corners and whose accessors spell
// x,y,width,height; the generator's table carries the two expressions that convert it.
typedef struct { double x, y; } QsApiPointF;
typedef struct { int x, y; } QsApiPoint;
typedef struct { int width, height; } QsApiSize;
typedef struct { double width, height; } QsApiSizeF;
typedef struct { int left, top, right, bottom; } QsApiMargins;
typedef struct { double left, top, right, bottom; } QsApiMarginsF;
typedef struct { int x1, y1, x2, y2; } QsApiLine;
typedef struct { double x1, y1, x2, y2; } QsApiLineF;
typedef struct { int x1, y1, x2, y2; } QsApiRect;
typedef struct { double x, y, width, height; } QsApiRectF;
