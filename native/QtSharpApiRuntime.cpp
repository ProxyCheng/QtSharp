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
#include <private/qmetaobject_p.h>
#include <private/qmetaobjectbuilder_p.h>
#include <cstring>
#include <string>
#include <unordered_map>
#include <unordered_set>
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

// The virtual-dispatch counterparts of the two above: there the managed override *produced* the
// value, and it has to land in the slot the native caller provided rather than in a heap object.
extern "C" __declspec(dllexport) void qs_api_write_qstring(void* destination, const char16_t* text)
{
    *static_cast<QString*>(destination) = text ? QString::fromUtf16(text) : QString();
}

extern "C" __declspec(dllexport) void qs_api_write_qstringlist(void* destination, void* list)
{
    if (list)
        *static_cast<QStringList*>(destination) = *static_cast<QStringList*>(list);
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

// Activates a signal from managed code. The argv convention is moc's own: args[0] is the return
// slot (always null for a signal) and args[i] holds the address of the i-th argument's storage.
// The managed side builds that storage (a QString object, a pointer variable, a boxed value) and
// this only has to forward it, which is what makes `Emit` possible for any signal the model
// publishes - and, with a dynamic meta-object, for a signal a managed class declared itself.
extern "C" __declspec(dllexport) int qs_api_emit(void* sender, const char* signature, void** args, int argc)
{
    if (!sender || !signature)
        return 0;

    auto* object = static_cast<QObject*>(sender);
    const QMetaObject* metaObject = object->metaObject();
    const QByteArray normalized = QMetaObject::normalizedSignature(signature);
    const int index = metaObject->indexOfSignal(normalized.constData());
    if (index < 0)
        return 0;

    std::vector<void*> argv(size_t(argc) + 1, nullptr);
    for (int i = 0; i < argc; ++i)
        argv[size_t(i) + 1] = args ? args[i] : nullptr;

    // indexOfSignal answers with an absolute *method* index, while activate wants the signal's
    // position among this meta-object's own signals. The two numberings differ (slots share the
    // method table), which is why the naive "index - methodOffset()" silently activates the wrong
    // signal - or none at all.
    const QMetaMethod method = metaObject->method(index);
    const int signalIndex = QMetaObjectPrivate::signalIndex(method);
    const int localSignalIndex = signalIndex - QMetaObjectPrivate::signalOffset(metaObject);
    QMetaObject::activate(object, metaObject, localSignalIndex, argv.data());
    return 1;
}

// ------------------------------------------------------------------ dynamic meta-object
// A C# class can declare its own signals and properties, the way pyqtSignal/Property do in
// PyQt. Qt has no room for them in the meta-object moc generated at compile time, so one is
// built at run time with QMetaObjectBuilder (a private-but-shipped header) and installed on the
// object. Once installed, QObject::metaObject() returns it, indexOfSignal/indexOfProperty find
// the extra members, and QMetaObject::activate emits them like any other signal.
//
// The value a property read/write carries is the plain C++ type (moc's qt_metacall writes into
// a[0] directly), so the managed side is handed that pointer and does its own conversion.
typedef void (*QsApiMetaPropertyCallback)(void* object, int index, int write, void* value);
static QsApiMetaPropertyCallback g_metaPropertyCallback = nullptr;

class QsApiDynamicMetaObject : public QAbstractDynamicMetaObject
{
public:
    QsApiDynamicMetaObject(QMetaObject* metaObject, int propertyBase)
        : m_metaObject(metaObject)
        , m_propertyBase(propertyBase)
    {
        // QObject::metaObject() hands back this very object, so the QMetaObject half has to carry
        // the built table. The table belongs to QMetaObjectBuilder (its string/method data lives
        // in separate allocations), is shared by every instance of the managed type, and is never
        // freed here.
        std::memcpy(static_cast<void*>(static_cast<QMetaObject*>(this)), metaObject, sizeof(QMetaObject));
    }

    // Only the members past the base's are ours; everything below is a copy of the base's own
    // table and is handled by moc's generated qt_metacall.
    int metaCall(QObject* object, QMetaObject::Call call, int id, void** args) override
    {
        if ((call == QMetaObject::ReadProperty || call == QMetaObject::WriteProperty)
            && id >= m_propertyBase && g_metaPropertyCallback)
        {
            g_metaPropertyCallback(object, id - m_propertyBase, call == QMetaObject::WriteProperty,
                                   args ? args[0] : nullptr);
            return 0;
        }
        return object->qt_metacall(call, id, args);
    }

private:
    QMetaObject* m_metaObject;
    int m_propertyBase;
};

// The meta-object tables are built once per member list and shared by every instance of the
// managed type; a managed type is identified by the name it hands over.
static std::unordered_map<std::string, QMetaObject*> g_dynamicMetaObjects;

extern "C" __declspec(dllexport) void qs_api_set_meta_property_callback(QsApiMetaPropertyCallback callback)
{
    g_metaPropertyCallback = callback;
}

// Installs a meta-object carrying the extra signals and properties. Must be called before any
// connection to them (i.e. right after the object is constructed).
// (`signals`/`slots` are macros in Qt, so the parameter names avoid them.)
extern "C" __declspec(dllexport) int qs_api_attach_metaobject(
    void* object, const char* className,
    const char* const* signalSignatures, int signalCount,
    const char* const* propertyNames, const char* const* propertyTypes, int propertyCount)
{
    if (!object || !className)
        return 0;
    if (signalCount <= 0 && propertyCount <= 0)
        return 0;

    auto* qobject = static_cast<QObject*>(object);
    const QMetaObject* base = qobject->metaObject();
    if (base->indexOfClassInfo("qtsharp-dynamic") >= 0)
        return 1;                       // already attached

    std::string key = className;
    key += '\x1f';
    for (int i = 0; i < signalCount; ++i)
    {
        key += signalSignatures[i];
        key += '\n';
    }
    key += "\x1e";
    for (int i = 0; i < propertyCount; ++i)
    {
        key += propertyNames[i];
        key += ':';
        key += propertyTypes[i];
        key += '\n';
    }

    auto found = g_dynamicMetaObjects.find(key);
    QMetaObject* built;
    if (found == g_dynamicMetaObjects.end())
    {
        // Only the members the managed class adds. The base's are reached through superClass(),
        // which is also what makes the offsets line up: QMetaObject::methodOffset() is
        // superClass()->methodCount(), so an extra signal lands where moc would have put it.
        // (Cloning the base's own table instead duplicates those members and leaves the built
        // table / the object's real class disagreeing about where the signals start.)
        QMetaObjectBuilder builder;
        builder.setClassName(className);
        builder.setSuperClass(base);
        for (int i = 0; i < signalCount; ++i)
            builder.addSignal(signalSignatures[i]);
        for (int i = 0; i < propertyCount; ++i)
            builder.addProperty(propertyNames[i], propertyTypes[i]);
        // Marks the object as already carrying our table (and is what makes the check above a
        // one-time operation).
        builder.addClassInfo("qtsharp-dynamic", "1");
        built = builder.toMetaObject();
        g_dynamicMetaObjects[key] = built;
    }
    else
    {
        built = found->second;
    }

    // The built table holds only the managed class's own members, so everything at or past the
    // base's property count belongs to it.
    auto* dynamicMetaObject = new QsApiDynamicMetaObject(built, base->propertyCount());
    QObjectPrivate::get(qobject)->metaObject = dynamicMetaObject;
    return 1;
}

// ------------------------------------------------------------------ object destruction
// Qt is the only thing that knows when an object dies (a parent is destroyed, a scene is
// cleared, the application shuts down), and a managed wrapper that keeps using it afterwards
// would be a use-after-free. One hook is installed once for the whole process: it is called with
// the address of the object that Qt destroyed, and the managed side marks every wrapper of that
// address invalid (and every item whose owner it was).
//
// There is deliberately no per-object delegate or GCHandle here: the slot's userData *is* the
// object address, and Qt frees the slot when it frees the object's connections.
typedef void (*QsApiDestroyedCallback)(void* object);
static QsApiDestroyedCallback g_destroyedCallback = nullptr;
static std::unordered_set<void*> g_watchedObjects;

class QsApiDestroyedSlot : public QtPrivate::QSlotObjectBase
{
public:
    explicit QsApiDestroyedSlot(void* object)
        : QSlotObjectBase(&QsApiDestroyedSlot::impl)
        , m_object(object)
    {
    }

private:
    static void impl(int which, QSlotObjectBase* self_, QObject* receiver, void** args, bool* ret)
    {
        Q_UNUSED(receiver);
        Q_UNUSED(args);
        auto* self = static_cast<QsApiDestroyedSlot*>(self_);
        if (which == Call)
        {
            // The object is being destroyed: unregister before handing the news over.
            g_watchedObjects.erase(self->m_object);
            if (g_destroyedCallback)
                g_destroyedCallback(self->m_object);
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

    void* m_object;
};

extern "C" __declspec(dllexport) void qs_api_set_destroyed_callback(QsApiDestroyedCallback callback)
{
    g_destroyedCallback = callback;
}

// Watching is idempotent and cheap: one slot per object, and nothing for an object that was
// already watched.
extern "C" __declspec(dllexport) void qs_api_watch_destroyed(void* object)
{
    if (!object || !g_destroyedCallback)
        return;
    if (!g_watchedObjects.insert(object).second)
        return;

    auto* sender = static_cast<QObject*>(object);
    const int index = sender->metaObject()->indexOfSignal("destroyed(QObject*)");
    if (index < 0)
        return;
    auto* slot = new QsApiDestroyedSlot(object);
    const QMetaObject::Connection connection =
        QObjectPrivate::connect(sender, index, slot, Qt::DirectConnection);
    if (!connection)
        slot->destroyIfLastRef();
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

// Delivers a synthetic wheel turn (the delta in eighths of a degree, as Qt's own events carry
// it, with the vertical component set - a wheel turn is a vertical scroll) so a managed
// wheelEvent override has a real QWheelEvent to read.
extern "C" __declspec(dllexport) int qs_api_probe_wheel(void* widget, int x, int y, int degrees, int modifiers)
{
    const QPointF pos(x, y);
    const QPoint angle(0, degrees * 8);
    QWheelEvent event(pos, pos, QPoint(0, 0), angle, Qt::NoButton,
                      Qt::NoModifier | Qt::KeyboardModifier(modifiers), Qt::NoScrollPhase, false);
    return QCoreApplication::sendEvent(static_cast<QObject*>(widget), &event) ? 1 : 0;
}

// Delivers a synthetic key press, so a managed keyPressEvent override can read the key code and
// the text the event carries.
extern "C" __declspec(dllexport) int qs_api_probe_key_press(void* widget, int key, int modifiers)
{
    const QString text = key < 0x10000 ? QString(QChar(key)) : QString();
    QKeyEvent event(QEvent::KeyPress, key, Qt::KeyboardModifier(modifiers), text);
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
