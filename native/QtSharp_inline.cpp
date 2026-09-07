#include <qapplication.h>
#include <qwidget.h>
#include <new>

extern "C" __declspec(dllexport) void qs_QFlag_int(void* i, int v) { new (i) QFlag(v); }
extern "C" __declspec(dllexport) void qs_QFlag_copy(void* i, void* o) { new (i) QFlag(*(QFlag*)o); }
extern "C" __declspec(dllexport) void qs_QAtomicInt_int(void* i, int v) { new (i) QAtomicInt(v); }
extern "C" __declspec(dllexport) void qs_QAtomicInt_copy(void* i, void* o) { new (i) QAtomicInt(*(QAtomicInt*)o); }
extern "C" __declspec(dllexport) void qs_QArrayData(void* i) { new (i) QArrayData(); }
extern "C" __declspec(dllexport) void qs_QBindingStorage_copy(void* i, void* o) { new (i) QBindingStorage(*(QBindingStorage*)o); }
