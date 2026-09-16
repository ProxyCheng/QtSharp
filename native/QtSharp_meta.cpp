// Generic, moc-metadata-driven signal dispatcher (PoC for a shiboken-style architecture).
//
// Instead of generating one compile-time C++ connect function per signal (which is what
// QtSharp_signals.cpp does today, and which cannot express overloaded signals or signals
// carrying a QPrivateSignal marker), this installs a single QtPrivate::QSlotObjectBase
// forwarder that receives the raw `void**` argument array and hands it to managed code.
//
// The signal is located at runtime from the sender's QMetaObject, so ANY signal that moc
// knows about works - including overloads and QPrivateSignal-decorated ones.

#include <qobject.h>
#include <qmetaobject.h>
#include <qmetatype.h>
#include <qbytearray.h>
#include <qstring.h>
#include <private/qobject_p.h>
#include <vector>

typedef void (*QsMetaCallback)(void* userData, int argc, const void* const* argv, const int* typeIds);

class QsMetaSlotObject : public QtPrivate::QSlotObjectBase
{
public:
    QsMetaSlotObject(QsMetaCallback callback, void* userData, std::vector<int> typeIds)
        : QSlotObjectBase(&QsMetaSlotObject::impl)
        , m_callback(callback)
        , m_userData(userData)
        , m_typeIds(std::move(typeIds))
    {
    }

private:
    static void impl(int which, QSlotObjectBase* self_, QObject* receiver, void** args, bool* ret)
    {
        Q_UNUSED(receiver);
        auto* self = static_cast<QsMetaSlotObject*>(self_);

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

    QsMetaCallback m_callback;
    void* m_userData;
    std::vector<int> m_typeIds;
};

extern "C" __declspec(dllexport) int qs_meta_signal_index(void* sender, const char* signature)
{
    if (!sender || !signature)
        return -1;

    auto* obj = static_cast<QObject*>(sender);
    const QByteArray normalized = QMetaObject::normalizedSignature(signature);
    return obj->metaObject()->indexOfSignal(normalized.constData());
}

extern "C" __declspec(dllexport) int qs_meta_signal_parameter_count(void* sender, const char* signature)
{
    const int index = qs_meta_signal_index(sender, signature);
    if (index < 0)
        return -1;

    auto* obj = static_cast<QObject*>(sender);
    return obj->metaObject()->method(index).parameterCount();
}

// Introspection helpers (a real binding would generate its registry from these).
extern "C" __declspec(dllexport) int qs_meta_method_count(void* sender)
{
    if (!sender)
        return 0;
    return static_cast<QObject*>(sender)->metaObject()->methodCount();
}

// 0 = method, 1 = signal, 2 = slot, 3 = constructor
extern "C" __declspec(dllexport) int qs_meta_method_kind(void* sender, int index)
{
    if (!sender)
        return -1;
    return int(static_cast<QObject*>(sender)->metaObject()->method(index).methodType());
}

extern "C" __declspec(dllexport) const char* qs_meta_method_signature(void* sender, int index)
{
    static thread_local QByteArray buffer;
    if (!sender)
        return "";
    buffer = static_cast<QObject*>(sender)->metaObject()->method(index).methodSignature();
    return buffer.constData();
}

// Returns 1 on success, 0 on failure.
extern "C" __declspec(dllexport) int qs_meta_connect(void* sender, const char* signature,
    QsMetaCallback callback, void* userData, int connectionType)
{
    if (!sender || !signature || !callback)
        return 0;

    auto* obj = static_cast<QObject*>(sender);
    const QMetaObject* metaObject = obj->metaObject();
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

    auto* slot = new QsMetaSlotObject(callback, userData, std::move(typeIds));
    const QMetaObject::Connection connection =
        QObjectPrivate::connect(obj, index, slot, Qt::ConnectionType(connectionType));
    if (!connection)
    {
        slot->destroyIfLastRef();
        return 0;
    }
    return 1;
}
