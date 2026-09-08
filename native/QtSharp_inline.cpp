#include <qapplication.h>
#include <qwidget.h>
#include <qstring.h>
#include <qstringlist.h>
#include <new>

extern "C" __declspec(dllexport) void qs_QFlag_int(void* i, int v) { new (i) QFlag(v); }
extern "C" __declspec(dllexport) void qs_QFlag_copy(void* i, void* o) { new (i) QFlag(*(QFlag*)o); }
extern "C" __declspec(dllexport) void qs_QAtomicInt_int(void* i, int v) { new (i) QAtomicInt(v); }
extern "C" __declspec(dllexport) void qs_QAtomicInt_copy(void* i, void* o) { new (i) QAtomicInt(*(QAtomicInt*)o); }
extern "C" __declspec(dllexport) void qs_QArrayData(void* i) { new (i) QArrayData(); }
extern "C" __declspec(dllexport) void qs_QBindingStorage_copy(void* i, void* o) { new (i) QBindingStorage(*(QBindingStorage*)o); }

extern "C" __declspec(dllexport) void* qs_QString_from_utf16(const char16_t* s) { return new QString(QString::fromUtf16(s)); }
extern "C" __declspec(dllexport) const unsigned short* qs_QString_utf16(const void* s) { return static_cast<const QString*>(s)->utf16(); }
extern "C" __declspec(dllexport) long long qs_QString_size(const void* s) { return static_cast<long long>(static_cast<const QString*>(s)->size()); }
extern "C" __declspec(dllexport) void qs_QString_free(void* s) { delete static_cast<QString*>(s); }
extern "C" __declspec(dllexport) void* qs_QWidget_windowTitle(void* w) { return new QString(static_cast<QWidget*>(w)->windowTitle()); }

extern "C" __declspec(dllexport) void* qs_QStringList_from_strings(const char16_t* const* strings, int count) { auto* list = new QStringList(); list->reserve(count); for (int i = 0; i < count; i++) list->append(QString::fromUtf16(strings[i])); return list; }
extern "C" __declspec(dllexport) long long qs_QStringList_size(const void* list) { return static_cast<long long>(static_cast<const QStringList*>(list)->size()); }
extern "C" __declspec(dllexport) const unsigned short* qs_QStringList_at(const void* list, long long index) { return static_cast<const QStringList*>(list)->at(static_cast<int>(index)).utf16(); }
extern "C" __declspec(dllexport) void qs_QStringList_free(void* list) { delete static_cast<QStringList*>(list); }
