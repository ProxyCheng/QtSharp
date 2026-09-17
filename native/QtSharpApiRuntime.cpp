// Hand-written runtime that ships with the generated bindings (not model-derived).
//
// Holds the two mechanisms the PoCs proved:
//   * one generic moc-metadata signal forwarder (no per-signal generated glue)
//   * the QString bridge used by the generated accessors

#include <qobject.h>
#include <qevent.h>
#include <qmetaobject.h>
#include <qmetatype.h>
#include <qbytearray.h>
#include <qstring.h>
#include <qcoreapplication.h>
#include <qapplication.h>
#include <private/qobject_p.h>
#include <vector>
#include "QtSharpApiRuntime.h"

// ------------------------------------------------------------------ QString bridge
extern "C" __declspec(dllexport) void* qs_api_qstring_from_utf16(const char16_t* s)
{
    return new QString(QString::fromUtf16(s));
}

extern "C" __declspec(dllexport) const unsigned short* qs_api_qstring_utf16(const void* s)
{
    return static_cast<const QString*>(s)->utf16();
}

extern "C" __declspec(dllexport) long long qs_api_qstring_size(const void* s)
{
    return static_cast<long long>(static_cast<const QString*>(s)->size());
}

extern "C" __declspec(dllexport) void qs_api_qstring_free(void* s)
{
    delete static_cast<QString*>(s);
}

// QStringList: the managed side copies the elements out one by one. The list itself belongs to
// the signal's caller, so only the copies have to be freed.
extern "C" __declspec(dllexport) int qs_api_qstringlist_count(const void* list)
{
    return static_cast<int>(static_cast<const QStringList*>(list)->size());
}

extern "C" __declspec(dllexport) void* qs_api_qstringlist_at(const void* list, int index)
{
    return new QString(static_cast<const QStringList*>(list)->at(index));
}

// A QStringList built for a single call (a managed string[] passed as a container argument).
extern "C" __declspec(dllexport) void* qs_api_qstringlist_new()
{
    return new QStringList();
}

extern "C" __declspec(dllexport) void qs_api_qstringlist_append(void* list, const char16_t* text)
{
    static_cast<QStringList*>(list)->append(text ? QString::fromUtf16(text) : QString());
}

extern "C" __declspec(dllexport) void qs_api_qstringlist_free(void* list)
{
    delete static_cast<QStringList*>(list);
}

// QByteArray: built from a managed byte[] for the duration of a call, and copied out when a
// QByteArrayView (or a QByteArray) comes back.
extern "C" __declspec(dllexport) void* qs_api_qbytearray_from_bytes(const unsigned char* data, int length)
{
    return new QByteArray(reinterpret_cast<const char*>(data), length);
}

extern "C" __declspec(dllexport) int qs_api_qbytearray_size(const void* array)
{
    return static_cast<int>(static_cast<const QByteArray*>(array)->size());
}

extern "C" __declspec(dllexport) const unsigned char* qs_api_qbytearray_data(const void* array)
{
    return reinterpret_cast<const unsigned char*>(static_cast<const QByteArray*>(array)->constData());
}

extern "C" __declspec(dllexport) void qs_api_qbytearray_free(void* array)
{
    delete static_cast<QByteArray*>(array);
}

// ------------------------------------------------------------------ signal forwarder
typedef void (*QsApiSignalCallback)(void* userData, int argc, const void* const* argv, const int* typeIds);

class QsApiSlotObject : public QtPrivate::QSlotObjectBase
{
public:
    QsApiSlotObject(QsApiSignalCallback callback, void* userData, std::vector<int> typeIds)
        : QSlotObjectBase(&QsApiSlotObject::impl)
        , m_callback(callback)
        , m_userData(userData)
        , m_typeIds(std::move(typeIds))
    {
    }

private:
    static void impl(int which, QSlotObjectBase* self_, QObject* receiver, void** args, bool* ret)
    {
        Q_UNUSED(receiver);
        auto* self = static_cast<QsApiSlotObject*>(self_);

        if (which == Call)
        {
            if (self->m_callback)
            {
                const void* const* argv = args
                    ? reinterpret_cast<const void* const*>(args + 1)   // args[0] is the return slot
                    : nullptr;
                self->m_callback(self->m_userData, int(self->m_typeIds.size()), argv,
                                 self->m_typeIds.data());
            }
        }
        else if (which == Destroy)
        {
            delete self;
        }
        else if (which == Compare)
        {
            if (ret)
                *ret = false;
        }
    }

    QsApiSignalCallback m_callback;
    void* m_userData;
    std::vector<int> m_typeIds;
};

extern "C" __declspec(dllexport) int qs_api_connect(void* sender, const char* signature,
    QsApiSignalCallback callback, void* userData, int connectionType)
{
    if (!sender || !signature || !callback)
        return 0;

    auto* object = static_cast<QObject*>(sender);
    const QMetaObject* metaObject = object->metaObject();
    const QByteArray normalized = QMetaObject::normalizedSignature(signature);
    const int index = metaObject->indexOfSignal(normalized.constData());
    if (index < 0)
        return 0;

    const QMetaMethod signal = metaObject->method(index);
    std::vector<int> typeIds;
    const int argc = signal.parameterCount();
    typeIds.reserve(argc);
    for (int i = 0; i < argc; ++i)
        typeIds.push_back(signal.parameterMetaType(i).id());

    auto* slot = new QsApiSlotObject(callback, userData, std::move(typeIds));
    const QMetaObject::Connection connection =
        QObjectPrivate::connect(object, index, slot, Qt::ConnectionType(connectionType));
    if (!connection)
    {
        slot->destroyIfLastRef();
        return 0;
    }
    return 1;
}

// PoC probe: call a virtual from native code so the generated C++ wrapper's override
// (and therefore the managed dispatch) is exercised.
extern "C" __declspec(dllexport) int qs_api_probe_event_filter(void* self, void* watched, void* event)
{
    return static_cast<QObject*>(self)->eventFilter(
        static_cast<QObject*>(watched), static_cast<QEvent*>(event)) ? 1 : 0;
}

// Diagnostic/lookup helper for the managed event layer: the numeric QEvent::Type.
extern "C" __declspec(dllexport) int qs_api_event_type(const void* event)
{
    return event ? int(static_cast<const QEvent*>(event)->type()) : -1;
}

// Delivers a user event, which QObject::event routes to the virtual customEvent(). Used to
// check that a managed override of a virtual declared in an *ancestor* class is reached.
extern "C" __declspec(dllexport) int qs_api_probe_user_event(void* object)
{
    QEvent event(QEvent::User);
    return QCoreApplication::sendEvent(static_cast<QObject*>(object), &event) ? 1 : 0;
}

// Delivers a synthetic mouse press, so a managed mousePressEvent override can be exercised
// (and its event payload read) without a real input device.
extern "C" __declspec(dllexport) int qs_api_probe_mouse_press(void* widget, int x, int y)
{
    const QPointF pos(x, y);
    QMouseEvent event(QEvent::MouseButtonPress, pos, pos,
                      Qt::LeftButton, Qt::LeftButton, Qt::NoModifier);
    return QCoreApplication::sendEvent(static_cast<QObject*>(widget), &event) ? 1 : 0;
}

// ------------------------------------------------------------------ application bootstrap
// QApplication's constructor takes int&/char**, which the model deliberately does not
// describe (it cannot be marshalled meaningfully), so starting the event loop is a runtime
// concern rather than a generated one.
extern "C" __declspec(dllexport) int qs_api_app_create()
{
    static int argc = 1;
    static char arg0[] = "qtsharp";
    static char* argv[] = { arg0, nullptr };
    static QApplication app(argc, argv);
    Q_UNUSED(app);
    return 0;
}

extern "C" __declspec(dllexport) int qs_api_app_exec()
{
    return QApplication::exec();
}

extern "C" __declspec(dllexport) void qs_api_app_quit()
{
    QCoreApplication::quit();
}
