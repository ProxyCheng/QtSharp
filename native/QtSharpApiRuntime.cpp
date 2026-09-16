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
