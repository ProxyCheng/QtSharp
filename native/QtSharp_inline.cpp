#include <qapplication.h>
#include <qwidget.h>
#include <qstring.h>
#include <qstringlist.h>
#include <qabstractbutton.h>
#include <qabstractitemmodel.h>
#include <qabstractitemdelegate.h>
#include <qkeysequence.h>
#include <qkeysequenceedit.h>
#include <qaction.h>
#include <qsettings.h>
#include <qvariant.h>
#include <qpixmap.h>
#include <qimage.h>
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

// --- Shortcut Editor helpers -------------------------------------------------
// QString <-> QKeySequence and QAction shortcut conversions (the generated
// bindings drop the QString-taking QKeySequence constructor and toString()).
extern "C" __declspec(dllexport) void qs_QKeySequenceEdit_setKeySequence_string(void* ed, const char16_t* s) {
    static_cast<QKeySequenceEdit*>(ed)->setKeySequence(QKeySequence(QString::fromUtf16(s)));
}
extern "C" __declspec(dllexport) void* qs_QKeySequenceEdit_keySequence_string(void* ed) {
    return new QString(static_cast<QKeySequenceEdit*>(ed)->keySequence().toString(QKeySequence::NativeText));
}
extern "C" __declspec(dllexport) void qs_QAction_setShortcut_string(void* a, const char16_t* s) {
    static_cast<QAction*>(a)->setShortcut(QKeySequence(QString::fromUtf16(s)));
}
extern "C" __declspec(dllexport) void* qs_QAction_shortcut_string(void* a) {
    return new QString(static_cast<QAction*>(a)->shortcut().toString(QKeySequence::NativeText));
}

// Emits a QAbstractItemModel signal whose payload (QModelIndex/QList<int>) the binding
// generator cannot express in the generated signal surface.
extern "C" __declspec(dllexport) void qs_QAbstractItemModel_emit_dataChanged(void* model, const void* topLeft, const void* bottomRight) {
    emit static_cast<QAbstractItemModel*>(model)->dataChanged(*static_cast<const QModelIndex*>(topLeft), *static_cast<const QModelIndex*>(bottomRight));
}

// QSettings keys are QAnyStringView in Qt 6, which the generated bindings cannot build
// from a C# string; expose the QString keyed overloads instead.
extern "C" __declspec(dllexport) void qs_QSettings_beginGroup_string(void* s, const char16_t* key) {
    static_cast<QSettings*>(s)->beginGroup(QString::fromUtf16(key));
}
extern "C" __declspec(dllexport) void qs_QSettings_setValue_string(void* s, const char16_t* key, const void* value) {
    static_cast<QSettings*>(s)->setValue(QString::fromUtf16(key), *static_cast<const QVariant*>(value));
}
extern "C" __declspec(dllexport) void qs_QSettings_value_string(void* s, const char16_t* key, void* outVariant) {
    *static_cast<QVariant*>(outVariant) = static_cast<QSettings*>(s)->value(QString::fromUtf16(key));
}
extern "C" __declspec(dllexport) int qs_QSettings_contains_string(void* s, const char16_t* key) {
    return static_cast<QSettings*>(s)->contains(QString::fromUtf16(key)) ? 1 : 0;
}

// QPixmap is returned by value from factories; the generated sret wrapper only bitwise
// copies and then destroys the temporary, which nulls the implicitly shared data. Use
// explicit copy assignment into a caller owned QPixmap instead.
extern "C" __declspec(dllexport) void qs_QPixmap_fromImage(void* out, const void* image) {
    *static_cast<QPixmap*>(out) = QPixmap::fromImage(*static_cast<const QImage*>(image));
}
extern "C" __declspec(dllexport) void qs_QPixmap_scaled(void* out, const void* src, int w, int h, int aspectMode, int transformMode) {
    *static_cast<QPixmap*>(out) = static_cast<const QPixmap*>(src)->scaled(w, h,
        static_cast<Qt::AspectRatioMode>(aspectMode), static_cast<Qt::TransformationMode>(transformMode));
}
