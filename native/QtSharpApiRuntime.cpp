// Hand-written runtime that ships with the generated bindings (not model-derived).
//
// Holds the two mechanisms the PoCs proved:
//   * one generic moc-metadata signal forwarder (no per-signal generated glue)
//   * the QString bridge used by the generated accessors

#include <qobject.h>
#include <qevent.h>
#include <qmetaobject.h>
#include <qmetatype.h>
#include <qvariant.h>
#include <qbytearray.h>
#include <qbytearrayview.h>
#include <qchar.h>
#include <qstring.h>
#include <qcoreapplication.h>
#include <qguiapplication.h>
#include <qapplication.h>
#include <qtimer.h>
#include <qregularexpression.h>
#include <qrunnable.h>
#include <qthreadpool.h>
#include <private/qobject_p.h>
#include <private/qmetaobject_p.h>
#include <private/qmetaobjectbuilder_p.h>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>
#include "QtSharpApiRuntime.h"
#include "QtSharpApiExport.h"

// ------------------------------------------------------------------ QChar Unicode properties
//
// QChar is erased to System.Char (see ApiCodeGen's IsCharT), so the classification predicates and
// the category come from the BCL. These are the properties the BCL has no API for; they call Qt's
// own tables so the answers follow Qt's Unicode version, not the runtime's.
//
// A code unit crosses as uint32_t (a code point); the instance methods widen the char first.

extern "C" QS_API_EXPORT uint8_t qs_char_combining_class(uint32_t ucs4)
{
    return static_cast<uint8_t>(QChar::combiningClass(ucs4));
}

extern "C" QS_API_EXPORT void* qs_char_decomposition(uint32_t ucs4)
{
    return new QString(QChar::decomposition(ucs4));
}

extern "C" QS_API_EXPORT int qs_char_decomposition_tag(uint32_t ucs4)
{
    return static_cast<int>(QChar::decompositionTag(ucs4));
}

extern "C" QS_API_EXPORT int qs_char_direction(uint32_t ucs4)
{
    return static_cast<int>(QChar::direction(ucs4));
}

extern "C" QS_API_EXPORT int qs_char_joining_type(uint32_t ucs4)
{
    return static_cast<int>(QChar::joiningType(ucs4));
}

extern "C" QS_API_EXPORT int qs_char_script(uint32_t ucs4)
{
    return static_cast<int>(QChar::script(ucs4));
}

extern "C" QS_API_EXPORT int qs_char_unicode_version(uint32_t ucs4)
{
    return static_cast<int>(QChar::unicodeVersion(ucs4));
}

extern "C" QS_API_EXPORT int qs_char_current_unicode_version()
{
    return static_cast<int>(QChar::currentUnicodeVersion());
}

extern "C" QS_API_EXPORT int qs_char_digit_value(uint32_t ucs4)
{
    return QChar::digitValue(ucs4);
}

extern "C" QS_API_EXPORT bool qs_char_has_mirrored(uint32_t ucs4)
{
    return QChar::hasMirrored(ucs4);
}

extern "C" QS_API_EXPORT uint32_t qs_char_mirrored_char(uint32_t ucs4)
{
    return QChar::mirroredChar(ucs4);
}

extern "C" QS_API_EXPORT uint32_t qs_char_to_case_folded(uint32_t ucs4)
{
    return QChar::toCaseFolded(ucs4);
}

extern "C" QS_API_EXPORT uint32_t qs_char_to_title_case(uint32_t ucs4)
{
    return QChar::toTitleCase(ucs4);
}

// ------------------------------------------------------------------ QVariant managed payload
//
// A managed object has no Qt metatype, so a QVariant cannot hold it by value. This is the
// PySide PyObjectWrapper shape: the QVariant holds a copyable C++ value that points at a
// ref-counted box carrying an id. The managed side owns the object in a table keyed by that id;
// when the last QVariant copy is destroyed, the box calls back once and the table entry goes.
//
// The copy/destructor are the whole contract. Qt copies QVariant values at will (arguments,
// model data, item data), so a shallow pointer copy without the refcount would free the managed
// object while another QVariant still pointed at it.
typedef void (*QsApiManagedRelease)(long long);

static QsApiManagedRelease s_managedRelease = nullptr;

// Test/diagnostic counter: one per live box. The lifetime test asserts it returns to its
// baseline after every QVariant copy is destroyed, which is the native half of the leak check
// (the managed table's count is the other half).
static std::atomic<long long> s_managedBoxes{0};

struct QsApiManagedBox
{
    long long id;
    std::atomic<int> refs{1};

    explicit QsApiManagedBox(long long value) : id(value)
    {
        s_managedBoxes.fetch_add(1, std::memory_order_relaxed);
    }
};

struct QsApiManagedValue
{
    QsApiManagedBox* box = nullptr;

    QsApiManagedValue() = default;
    explicit QsApiManagedValue(QsApiManagedBox* value) : box(value) {}

    QsApiManagedValue(const QsApiManagedValue& other) : box(other.box)
    {
        if (box)
            box->refs.fetch_add(1, std::memory_order_relaxed);
    }

    QsApiManagedValue& operator=(const QsApiManagedValue& other)
    {
        if (other.box)
            other.box->refs.fetch_add(1, std::memory_order_relaxed);
        release();
        box = other.box;
        return *this;
    }

    QsApiManagedValue(QsApiManagedValue&& other) noexcept : box(other.box)
    {
        other.box = nullptr;
    }

    QsApiManagedValue& operator=(QsApiManagedValue&& other) noexcept
    {
        if (this != &other)
        {
            release();
            box = other.box;
            other.box = nullptr;
        }
        return *this;
    }

    ~QsApiManagedValue() { release(); }

private:
    void release()
    {
        if (!box)
            return;
        if (box->refs.fetch_sub(1, std::memory_order_acq_rel) == 1)
        {
            long long id = box->id;
            delete box;
            box = nullptr;
            s_managedBoxes.fetch_sub(1, std::memory_order_relaxed);
            if (s_managedRelease)
                s_managedRelease(id);
        }
        else
        {
            box = nullptr;
        }
    }
};

Q_DECLARE_METATYPE(QsApiManagedValue)

// qRegisterMetaType is idempotent; the function-local static runs it once, before the first
// QVariant that needs the type.
static int QsApiManagedMetaTypeId()
{
    static const int id = qRegisterMetaType<QsApiManagedValue>("QtSharp.ManagedObject");
    return id;
}

extern "C" QS_API_EXPORT void qs_api_set_managed_release_callback(QsApiManagedRelease callback)
{
    s_managedRelease = callback;
}

extern "C" QS_API_EXPORT long long qs_api_managed_box_count()
{
    return s_managedBoxes.load(std::memory_order_relaxed);
}

extern "C" QS_API_EXPORT void* qs_api_QVariant_fromManaged(long long id)
{
    QsApiManagedMetaTypeId();
    QsApiManagedValue value(new QsApiManagedBox(id));
    return new QVariant(QVariant::fromValue(value));
}

// The metatype-id spelling of QVariant::fromMetaType: the generated table resolved the id when the
// assembly loaded, so a conversion does not spell the type name or allocate a QMetaType wrapper.
// `copy` points at a live value of that type; QVariant copies it.
extern "C" QS_API_EXPORT void* qs_api_QVariant_fromMetaTypeId(int typeId, const void* copy)
{
    return new QVariant(QMetaType(typeId), copy);
}

// A QVariantMap built by the generated QMap helper. QVariant copies the map, so the caller keeps
// ownership of the one it passed in.
extern "C" QS_API_EXPORT void* qs_api_QVariant_fromVariantMap(const void* map)
{
    return map ? new QVariant(*static_cast<const QVariantMap*>(map)) : new QVariant();
}

extern "C" QS_API_EXPORT long long qs_api_QVariant_managedId(const void* variant)
{
    if (!variant)
        return -1;
    QsApiManagedMetaTypeId();
    const auto* value = static_cast<const QVariant*>(variant);
    if (value->metaType() != QMetaType::fromType<QsApiManagedValue>())
        return -1;
    QsApiManagedValue managed = value->value<QsApiManagedValue>();
    return managed.box ? managed.box->id : -1;
}

extern "C" QS_API_EXPORT void* qs_api_QVariant_clone(const void* variant)
{
    return variant ? new QVariant(*static_cast<const QVariant*>(variant)) : nullptr;
}

// QObject values cross as Qt's own QObject* metatype, the way PySide presents them. The pointer
// is non-owning, exactly as in C++: a variant does not keep a QObject alive.
extern "C" QS_API_EXPORT void* qs_api_QVariant_fromQObject(void* object)
{
    return new QVariant(QVariant::fromValue(static_cast<QObject*>(object)));
}

extern "C" QS_API_EXPORT void* qs_api_QVariant_toQObject(void* variant)
{
    if (!variant)
        return nullptr;
    auto* value = static_cast<QVariant*>(variant);
    if (value->metaType() == QMetaType::fromType<QObject*>())
        return value->value<QObject*>();
    return nullptr;
}


// ------------------------------------------------------------------ QString bridge
extern "C" QS_API_EXPORT void* qs_api_qstring_from_utf16(const char16_t* s)
{
    return new QString(QString::fromUtf16(s));
}

extern "C" QS_API_EXPORT const unsigned short* qs_api_qstring_utf16(const void* s)
{
    return static_cast<const QString*>(s)->utf16();
}

extern "C" QS_API_EXPORT long long qs_api_qstring_size(const void* s)
{
    return static_cast<long long>(static_cast<const QString*>(s)->size());
}

extern "C" QS_API_EXPORT void qs_api_qstring_free(void* s)
{
    delete static_cast<QString*>(s);
}

// QStringList: the managed side copies the elements out one by one. The list itself belongs to
// the signal's caller, so only the copies have to be freed.
extern "C" QS_API_EXPORT int qs_api_qstringlist_count(const void* list)
{
    return static_cast<int>(static_cast<const QStringList*>(list)->size());
}

extern "C" QS_API_EXPORT void* qs_api_qstringlist_at(const void* list, int index)
{
    return new QString(static_cast<const QStringList*>(list)->at(index));
}

// A QStringList built for a single call (a managed string[] passed as a container argument).
extern "C" QS_API_EXPORT void* qs_api_qstringlist_new()
{
    return new QStringList();
}

extern "C" QS_API_EXPORT void qs_api_qstringlist_append(void* list, const char16_t* text)
{
    static_cast<QStringList*>(list)->append(text ? QString::fromUtf16(text) : QString());
}

extern "C" QS_API_EXPORT void qs_api_qstringlist_free(void* list)
{
    delete static_cast<QStringList*>(list);
}

// The virtual-dispatch counterparts of the two above: there the managed override *produced* the
// value, and it has to land in the slot the native caller provided rather than in a heap object.
extern "C" QS_API_EXPORT void qs_api_write_qstring(void* destination, const char16_t* text)
{
    *static_cast<QString*>(destination) = text ? QString::fromUtf16(text) : QString();
}

extern "C" QS_API_EXPORT void qs_api_write_qstringlist(void* destination, void* list)
{
    if (list)
        *static_cast<QStringList*>(destination) = *static_cast<QStringList*>(list);
}

// QByteArray: built from a managed byte[] for the duration of a call, and copied out when a
// QByteArrayView (or a QByteArray) comes back.
extern "C" QS_API_EXPORT void* qs_api_qbytearray_from_bytes(const unsigned char* data, int length)
{
    return new QByteArray(reinterpret_cast<const char*>(data), length);
}

extern "C" QS_API_EXPORT int qs_api_qbytearray_size(const void* array)
{
    return static_cast<int>(static_cast<const QByteArray*>(array)->size());
}

extern "C" QS_API_EXPORT const unsigned char* qs_api_qbytearray_data(const void* array)
{
    return reinterpret_cast<const unsigned char*>(static_cast<const QByteArray*>(array)->constData());
}

extern "C" QS_API_EXPORT void qs_api_qbytearray_free(void* array)
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

// Connects a managed callback to a signal. The returned handle is the QMetaObject::Connection kept
// on the heap: the C# side needs it back to disconnect (the `-=` of a signal exposed as a C# event),
// and QObject::disconnect(connection) is the public way to drop exactly that one connection.
//
// `context` is the QObject whose lifetime and thread affinity the connection follows - Qt's
// QObject::connect(sender, signal, context, functor, type) shape. A queued connection is delivered
// on the context's thread (which must run an event loop), and destroying the context drops the
// connection before any later emission. A null context keeps the historical behaviour: the sender
// itself is the receiver, so the connection lives with the sender and Auto runs it on the emitting
// thread when both live in the same thread.
extern "C" QS_API_EXPORT void* qs_api_connect(void* sender, const char* signature,
    QsApiSignalCallback callback, void* userData, void* context, int connectionType)
{
    if (!sender || !signature || !callback)
        return nullptr;

    auto* object = static_cast<QObject*>(sender);
    const QMetaObject* metaObject = object->metaObject();
    const QByteArray normalized = QMetaObject::normalizedSignature(signature);
    const int index = metaObject->indexOfSignal(normalized.constData());
    if (index < 0)
        return nullptr;

    const QMetaMethod signal = metaObject->method(index);
    std::vector<int> typeIds;
    const int argc = signal.parameterCount();
    typeIds.reserve(argc);
    for (int i = 0; i < argc; ++i)
        typeIds.push_back(signal.parameterMetaType(i).id());

    auto* slot = new QsApiSlotObject(callback, userData, std::move(typeIds));
    auto* receiver = context ? static_cast<QObject*>(context) : object;
    const QMetaObject::Connection connection =
        QObjectPrivate::connect(object, index, receiver, slot, Qt::ConnectionType(connectionType));
    if (!connection)
    {
        slot->destroyIfLastRef();
        return nullptr;
    }
    return new QMetaObject::Connection(connection);
}

// Drops one connection and the handle with it. Safe on an already-broken connection (the sender may
// have died first), which is why the handle is deleted either way.
extern "C" QS_API_EXPORT int qs_api_disconnect(void* connection)
{
    if (!connection)
        return 0;
    auto* stored = static_cast<QMetaObject::Connection*>(connection);
    const bool disconnected = QObject::disconnect(*stored);
    delete stored;
    return disconnected ? 1 : 0;
}


// Activates a signal from managed code. The argv convention is moc's own: args[0] is the return
// slot (always null for a signal) and args[i] holds the address of the i-th argument's storage.
// The managed side builds that storage (a QString object, a pointer variable, a boxed value) and
// this only has to forward it, which is what makes `Emit` possible for any signal the model
// publishes - and, with a dynamic meta-object, for a signal a managed class declared itself.
extern "C" QS_API_EXPORT int qs_api_emit(void* sender, const char* signature, void** args, int argc)
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
// a[0] directly), so the managed side is handed that pointer and does its own conversion. `call`
// is QMetaObject::Call narrowed to the three the table can reach: 0 read, 1 write, 2 reset.
typedef void (*QsApiMetaPropertyCallback)(void* object, int index, int call, void* value);
static QsApiMetaPropertyCallback g_metaPropertyCallback = nullptr;

// A dynamic slot's managed implementation. `args` is moc's argv: args[0] is the return storage
// (null for a void slot) and args[i + 1] points at the i-th argument's native value.
typedef void (*QsApiMetaSlotCallback)(void* object, int slotIndex, void** args);
static QsApiMetaSlotCallback g_metaSlotCallback = nullptr;

// The Q_PROPERTY flags the managed side sends, one bit per QMetaProperty::is*() accessor.
enum QsApiMetaPropertyFlag : int
{
    QsApiPropertyReadable = 1 << 0,
    QsApiPropertyWritable = 1 << 1,
    QsApiPropertyResettable = 1 << 2,
    QsApiPropertyDesignable = 1 << 3,
    QsApiPropertyScriptable = 1 << 4,
    QsApiPropertyStored = 1 << 5,
    QsApiPropertyUser = 1 << 6,
    QsApiPropertyConstant = 1 << 7,
    QsApiPropertyFinal = 1 << 8,
    QsApiPropertyRequired = 1 << 9,
};

class QsApiDynamicMetaObject : public QAbstractDynamicMetaObject
{
public:
    QsApiDynamicMetaObject(QMetaObject* metaObject, int propertyBase,
                           int localSignalCount, int localSlotCount)
        : m_metaObject(metaObject)
        , m_propertyBase(propertyBase)
        , m_signalCount(localSignalCount)
        , m_slotCount(localSlotCount)
    {
        // QObject::metaObject() hands back this very object, so the QMetaObject half has to carry
        // the built table. The table belongs to QMetaObjectBuilder (its string/method data lives
        // in separate allocations), is shared by every instance of the managed type, and is never
        // freed here.
        std::memcpy(static_cast<void*>(static_cast<QMetaObject*>(this)), metaObject, sizeof(QMetaObject));
    }

    // Only the members past the base's are ours; everything below is a copy of the base's own
    // table and is handled by moc's generated qt_metacall.
    //
    // Method ids arrive in the built table's numbering, so the base's methods occupy [0,
    // superClass()->methodCount()). The builder appends methods in the order they were added -
    // signals first, then slots - which puts the local signals at [base, base + signalCount) and
    // the local slots right after them.
    int metaCall(QObject* object, QMetaObject::Call call, int id, void** args) override
    {
        if ((call == QMetaObject::ReadProperty || call == QMetaObject::WriteProperty
             || call == QMetaObject::ResetProperty)
            && id >= m_propertyBase && g_metaPropertyCallback)
        {
            const int code = call == QMetaObject::WriteProperty ? 1
                           : call == QMetaObject::ResetProperty ? 2
                           : 0;
            g_metaPropertyCallback(object, id - m_propertyBase, code, args ? args[0] : nullptr);
            return 0;
        }
        if (call == QMetaObject::InvokeMetaMethod && m_slotCount > 0 && g_metaSlotCallback)
        {
            const QMetaObject* parent = superClass();
            const int local = id - (parent ? parent->methodCount() : 0);
            if (local >= m_signalCount && local < m_signalCount + m_slotCount)
            {
                g_metaSlotCallback(object, local - m_signalCount, args);
                // QMetaMethod::invoke reads the result the way moc's qt_metacall spells it: a
                // negative value means "handled", anything else means "not handled" and the call
                // is reported as failed - with the slot already run, the worst kind of wrong.
                // -1 is what moc returns once it has consumed the last method id.
                return -1;
            }
        }
        return object->qt_metacall(call, id, args);
    }

private:
    QMetaObject* m_metaObject;
    int m_propertyBase;
    int m_signalCount;
    int m_slotCount;
};

// The meta-object tables are built once per (base, class name, member list) and shared by every
// instance of the managed type.
static std::unordered_map<std::string, QMetaObject*> g_dynamicMetaObjects;

extern "C" QS_API_EXPORT void qs_api_set_meta_property_callback(QsApiMetaPropertyCallback callback)
{
    g_metaPropertyCallback = callback;
}

extern "C" QS_API_EXPORT void qs_api_set_meta_slot_callback(QsApiMetaSlotCallback callback)
{
    g_metaSlotCallback = callback;
}

// Installs a meta-object carrying the extra signals, properties and slots. Must be called before
// any connection to them (i.e. right after the object is constructed).
// (`signals`/`slots` are macros in Qt, so the parameter names avoid them.)
extern "C" QS_API_EXPORT int qs_api_attach_metaobject(
    void* object, const char* className,
    const char* const* signalSignatures, int signalCount,
    const char* const* propertyNames, const char* const* propertyTypes, int propertyCount,
    const int* propertyFlags, const int* propertyNotify, const int* propertyRevisions,
    const char* const* slotSignatures, const char* const* slotReturnTypes, int slotCount)
{
    if (!object || !className)
        return 0;
    if (signalCount <= 0 && propertyCount <= 0 && slotCount <= 0)
        return 0;

    auto* qobject = static_cast<QObject*>(object);
    const QMetaObject* base = qobject->metaObject();
    if (base->indexOfClassInfo("qtsharp-dynamic") >= 0)
        return 1;                       // already attached

    // The class name and the member list are not the table's whole identity: two C# types can
    // share a simple name (they differ by namespace) and declare the same members while deriving
    // from different Qt classes. Every id in the table is an offset from the base's counts, so a
    // table built for the other base would misnumber this type's methods and properties. The
    // base's static meta-object is stable for the life of the process and names it exactly.
    std::string key = className;
    key += '\x1f';
    key += std::to_string(reinterpret_cast<std::uintptr_t>(base));
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
        key += ':';
        key += std::to_string(propertyFlags ? propertyFlags[i] : 0);
        key += ':';
        key += std::to_string(propertyNotify ? propertyNotify[i] : -1);
        key += ':';
        key += std::to_string(propertyRevisions ? propertyRevisions[i] : 0);
        key += '\n';
    }
    // The slots are part of the table's identity as much as the signals and properties: two
    // managed classes sharing a name but declaring different slots must not share a table.
    key += '\x1d';
    for (int i = 0; i < slotCount; ++i)
    {
        key += slotSignatures && slotSignatures[i] ? slotSignatures[i] : "";
        key += ':';
        key += slotReturnTypes && slotReturnTypes[i] ? slotReturnTypes[i] : "void";
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
        // `signals` is a Qt keyword macro, so the vector needs another name.
        std::vector<QMetaMethodBuilder> signalBuilders;
        signalBuilders.reserve(signalCount);
        for (int i = 0; i < signalCount; ++i)
            signalBuilders.push_back(builder.addSignal(signalSignatures[i]));
        for (int i = 0; i < propertyCount; ++i)
        {
            QMetaPropertyBuilder property = builder.addProperty(propertyNames[i], propertyTypes[i]);
            const int flags = propertyFlags ? propertyFlags[i] : 0;
            property.setReadable((flags & QsApiPropertyReadable) != 0);
            property.setWritable((flags & QsApiPropertyWritable) != 0);
            property.setResettable((flags & QsApiPropertyResettable) != 0);
            property.setDesignable((flags & QsApiPropertyDesignable) != 0);
            property.setScriptable((flags & QsApiPropertyScriptable) != 0);
            property.setStored((flags & QsApiPropertyStored) != 0);
            property.setUser((flags & QsApiPropertyUser) != 0);
            property.setConstant((flags & QsApiPropertyConstant) != 0);
            property.setFinal((flags & QsApiPropertyFinal) != 0);
            property.setRequired((flags & QsApiPropertyRequired) != 0);
            const int revision = propertyRevisions ? propertyRevisions[i] : 0;
            if (revision > 0)
                property.setRevision(revision);
            const int notify = propertyNotify ? propertyNotify[i] : -1;
            if (notify >= 0 && notify < int(signalBuilders.size()))
                property.setNotifySignal(signalBuilders[size_t(notify)]);
        }
        // Slots are appended after the signals, so the local signal range is [0, signalCount) and
        // the local slot range follows it (see QsApiDynamicMetaObject::metaCall).
        for (int i = 0; i < slotCount; ++i)
        {
            QMetaMethodBuilder slot = builder.addSlot(
                slotSignatures && slotSignatures[i] ? slotSignatures[i] : "");
            if (slotReturnTypes && slotReturnTypes[i])
                slot.setReturnType(slotReturnTypes[i]);
            slot.setAccess(QMetaMethod::Public);
        }
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
    // base's counts belongs to it.
    auto* dynamicMetaObject = new QsApiDynamicMetaObject(
        built, base->propertyCount(), signalCount, slotCount);
    QObjectPrivate::get(qobject)->metaObject = dynamicMetaObject;
    return 1;
}

// ------------------------------------------------------------------ dynamic invocation
// QMetaMethod::invoke and QMetaObject::invokeMethod are the runtime's reflection entry points:
// they take an argument list whose element is a type name and the address of a value in that
// type's native representation. Managed code cannot spell QGenericArgument (it is a C++ class),
// so this shim carries the same pair across a plain C ABI and builds the QGenericArgument /
// QGenericReturnArgument values itself.
//
// A call answers with one of the statuses below instead of throwing: the managed surface turns
// them into false plus a reason, so a dynamic call site never crashes on a name or a type that
// does not match. The value pointed to by `result` is a QVariant* the caller owns (release it
// with the ordinary QVariant destructor, i.e. through the managed VariantMarshalling bridge).
typedef struct QsApiInvokeArgument
{
    const char* typeName;   // the Qt spelling ("int", "QString", "QWidget*", "QVariant")
    const void* value;      // address of a value in that type's native representation
} QsApiInvokeArgument;

enum QsApiInvokeStatus
{
    QsApiInvokeOk = 0,
    QsApiInvokeInvalid = 1,           // null object/meta-object/signature
    QsApiInvokeNotFound = 2,          // no member with that name and argument count
    QsApiInvokeTooManyArguments = 3,  // QMetaMethod::invoke carries at most ten
    QsApiInvokeFailed = 4,            // Qt refused the call (type mismatch, queued return, ...)
    QsApiInvokeReturnUnavailable = 5, // a result was asked for, the return type cannot be copied
};

// One argument as it is finally handed to Qt. Normally that is the caller's own type name and
// value; a QVariant is converted to the parameter's declared type, and a QObject pointer is
// re-labelled with the parameter's declared class (Qt matches pointer metatypes exactly, so a
// QWidget* argument for a QObject* parameter would otherwise be rejected).
struct QsApiPreparedArgument
{
    QVariant converted;
    QByteArray declaredName;
    void* nullPointer = nullptr;
    const char* typeName = nullptr;
    const void* value = nullptr;
};

// Whether an argument can satisfy parameter `index`. "QVariant" is accepted for any parameter
// because the conversion is attempted while the call is prepared; a QObject pointer is accepted
// for a base-class pointer parameter when the object really is an instance of that class.
static bool qs_api_argument_matches(const QMetaMethod& method, int index,
                                    const QsApiInvokeArgument& argument)
{
    const QByteArray declared = method.parameterTypeName(index);
    const char* given = argument.typeName ? argument.typeName : "QVariant";
    if (declared == given)
        return true;
    if (std::strcmp(given, "QVariant") == 0)
        return true;
    if (!argument.value || !declared.endsWith('*') || !given || !*given || given[std::strlen(given) - 1] != '*')
        return false;

    const QMetaType declaredType = QMetaType::fromName(declared);
    if (!declaredType.isValid() || !declaredType.flags().testFlag(QMetaType::PointerToQObject))
        return false;
    const QMetaObject* declaredMetaObject = declaredType.metaObject();
    const QObject* object = *static_cast<QObject* const*>(argument.value);
    return declaredMetaObject && object && object->inherits(declaredMetaObject->className());
}

// Picks the method a by-name call means. A member with a parameter list is Qt's normalized
// signature and must resolve exactly; a bare name is matched against the methods of that name
// and argument count, preferring one whose parameter types fit. A count-only match is returned
// as a fallback so Qt still gets to refuse it with its own diagnostic rather than this shim
// reporting "not found".
static QMetaMethod qs_api_resolve_method(const QMetaObject* metaObject, const char* member,
                                         int argumentCount,
                                         const QsApiInvokeArgument* arguments, int* status)
{
    if (!metaObject || !member || !*member) {
        *status = QsApiInvokeInvalid;
        return QMetaMethod();
    }
    if (argumentCount < 0 || argumentCount > Q_METAMETHOD_INVOKE_MAX_ARGS) {
        *status = QsApiInvokeTooManyArguments;
        return QMetaMethod();
    }

    if (std::strchr(member, '(')) {
        const QByteArray signature = QMetaObject::normalizedSignature(member);
        const int index = metaObject->indexOfMethod(signature);
        if (index < 0 || metaObject->method(index).parameterCount() != argumentCount) {
            *status = QsApiInvokeNotFound;
            return QMetaMethod();
        }
        *status = QsApiInvokeOk;
        return metaObject->method(index);
    }

    const QByteArray name(member);
    QMetaMethod fallback;
    for (int i = 0; i < metaObject->methodCount(); ++i) {
        QMetaMethod method = metaObject->method(i);
        if (method.methodType() == QMetaMethod::Constructor
            || method.name() != name || method.parameterCount() != argumentCount)
            continue;
        bool matches = true;
        for (int a = 0; a < argumentCount; ++a) {
            if (!qs_api_argument_matches(method, a, arguments[a])) {
                matches = false;
                break;
            }
        }
        if (matches) {
            *status = QsApiInvokeOk;
            return method;
        }
        if (!fallback.isValid())
            fallback = method;
    }
    if (fallback.isValid()) {
        *status = QsApiInvokeOk;
        return fallback;
    }
    *status = QsApiInvokeNotFound;
    return QMetaMethod();
}

// Fills in the final type names and values. The prepared array outlives the invoke call, so the
// QByteArray names and converted QVariants the QGenericArguments point at stay alive.
static void qs_api_prepare_arguments(const QMetaMethod& method,
                                     const QsApiInvokeArgument* arguments, int argumentCount,
                                     QsApiPreparedArgument (&prepared)[Q_METAMETHOD_INVOKE_MAX_ARGS])
{
    for (int i = 0; i < argumentCount; ++i) {
        const char* given = arguments[i].typeName ? arguments[i].typeName : "QVariant";
        const void* value = arguments[i].value;
        prepared[i].typeName = given;
        prepared[i].value = value;

        if (!method.isValid())
            continue;
        const QByteArray declared = method.parameterTypeName(i);
        if (declared == given)
            continue;

        if (std::strcmp(given, "QVariant") == 0 && value) {
            const QVariant* variant = static_cast<const QVariant*>(value);
            if (declared.endsWith('*') && !variant->isValid()) {
                // A null managed reference is a null pointer for a pointer parameter.
                prepared[i].declaredName = declared;
                prepared[i].typeName = prepared[i].declaredName.constData();
                prepared[i].value = &prepared[i].nullPointer;
                continue;
            }
            const QMetaType target = QMetaType::fromName(declared);
            if (target.isValid()) {
                prepared[i].converted = *variant;
                if (prepared[i].converted.convert(target)) {
                    prepared[i].declaredName = declared;
                    prepared[i].typeName = prepared[i].declaredName.constData();
                    // The argument points at the value inside the variant, not at the QVariant.
                    prepared[i].value = prepared[i].converted.constData();
                }
            }
            continue;
        }

        if (declared == "QVariant" && value) {
            // The other direction: a value with a concrete Qt type for a QVariant parameter.
            const QMetaType givenType = QMetaType::fromName(QByteArrayView(given));
            if (givenType.isValid()) {
                prepared[i].converted = QVariant(givenType, value);
                prepared[i].declaredName = declared;
                prepared[i].typeName = prepared[i].declaredName.constData();
                prepared[i].value = &prepared[i].converted;
            }
            continue;
        }

        if (qs_api_argument_matches(method, i, arguments[i])) {
            prepared[i].declaredName = declared;
            prepared[i].typeName = prepared[i].declaredName.constData();
        }
    }
}

// The shared tail: allocate the return storage, build the ten QGenericArguments and make the
// call. Both entry points end here with a *resolved* method, so the call is always
// QMetaMethod::invoke. That matters: QMetaObject::invokeMethod's by-name overload accepts only a
// bare name (never a signature) and re-resolves it from the argument type names, which would lose
// the QVariant conversions prepared here and could land on a same-name, same-count overload whose
// types do not match. Resolving once and invoking the method Qt gave us is the same call with
// fewer ways to get it wrong.
static int qs_api_run_invoke(QMetaMethod method, void* object, int connectionType,
                             const QsApiInvokeArgument* arguments, int argumentCount,
                             void** result)
{
    if (!method.isValid() || !object)
        return QsApiInvokeInvalid;
    if (argumentCount < 0 || argumentCount > Q_METAMETHOD_INVOKE_MAX_ARGS)
        return QsApiInvokeTooManyArguments;
    if (result)
        *result = nullptr;

    const QMetaType returnType = method.returnMetaType();
    const bool wantsResult = result != nullptr;
    const bool hasResult = wantsResult && returnType.isValid() && returnType.id() != QMetaType::Void;
    void* returnStorage = nullptr;
    if (hasResult) {
        returnStorage = returnType.create();
        if (!returnStorage)
            return QsApiInvokeReturnUnavailable;
    }

    int status = QsApiInvokeFailed;
    QsApiPreparedArgument prepared[Q_METAMETHOD_INVOKE_MAX_ARGS];
    qs_api_prepare_arguments(method, arguments, argumentCount, prepared);

    QGenericArgument values[Q_METAMETHOD_INVOKE_MAX_ARGS];
    for (int i = 0; i < argumentCount; ++i) {
        if (prepared[i].typeName)
            values[i] = QGenericArgument(prepared[i].typeName, prepared[i].value);
    }
    const QGenericReturnArgument returnArgument(
        hasResult ? (method.typeName() ? method.typeName() : returnType.name()) : nullptr,
        returnStorage);

    const bool invoked = method.invoke(static_cast<QObject*>(object),
                                       static_cast<Qt::ConnectionType>(connectionType), returnArgument,
                                       values[0], values[1], values[2], values[3], values[4],
                                       values[5], values[6], values[7], values[8], values[9]);

    if (invoked) {
        if (hasResult)
            *result = new QVariant(returnType, returnStorage);
        status = QsApiInvokeOk;
    }
    if (returnStorage)
        returnType.destroy(returnStorage);
    return status;
}

// The QMetaMethod::invoke half. The managed QMetaMethod is a value type whose enclosing
// meta-object is a borrowed pointer, so the method is re-addressed here by the index it reports
// (QMetaMethod::methodIndex is the index QMetaObject::method takes).
extern "C" QS_API_EXPORT int qs_api_qmeta_method_invoke(
    void* metaObject, int methodIndex, void* object, int connectionType,
    const QsApiInvokeArgument* arguments, int argumentCount, void** result)
{
    if (!metaObject || !object)
        return QsApiInvokeInvalid;
    if (argumentCount < 0 || argumentCount > Q_METAMETHOD_INVOKE_MAX_ARGS)
        return QsApiInvokeTooManyArguments;

    const auto* meta = static_cast<const QMetaObject*>(metaObject);
    if (methodIndex < 0 || methodIndex >= meta->methodCount())
        return QsApiInvokeNotFound;
    const QMetaMethod method = meta->method(methodIndex);
    if (!method.isValid() || method.parameterCount() != argumentCount)
        return QsApiInvokeNotFound;
    return qs_api_run_invoke(method, object, connectionType, arguments, argumentCount, result);
}

// The by-name half. The member is resolved once here (a bare name with compatible argument types,
// or an exact normalized signature); the resolved method is then invoked, so Qt's own by-name
// overload never has to re-resolve what this shim already matched.
extern "C" QS_API_EXPORT int qs_api_qmeta_object_invoke(
    void* object, const char* member, int connectionType,
    const QsApiInvokeArgument* arguments, int argumentCount, void** result)
{
    if (!object || !member)
        return QsApiInvokeInvalid;
    if (argumentCount < 0 || argumentCount > Q_METAMETHOD_INVOKE_MAX_ARGS)
        return QsApiInvokeTooManyArguments;

    const auto* qobject = static_cast<const QObject*>(object);
    int status = QsApiInvokeOk;
    const QMetaMethod method = qs_api_resolve_method(qobject->metaObject(), member,
                                                     argumentCount, arguments, &status);
    if (status != QsApiInvokeOk)
        return status;
    return qs_api_run_invoke(method, object, connectionType, arguments, argumentCount, result);
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
typedef void (*QsApiDestroyedCallback)(void* object, long long generation);
static QsApiDestroyedCallback g_destroyedCallback = nullptr;
// Address -> the generation that was watched there. A native address is not a stable identity (the
// allocator hands a freed block to the next object), so the generation is what says whether an
// entry still describes the object being watched.
static std::unordered_map<void*, long long> g_watchedObjects;

class QsApiDestroyedSlot : public QtPrivate::QSlotObjectBase
{
public:
    QsApiDestroyedSlot(void* object, long long generation)
        : QSlotObjectBase(&QsApiDestroyedSlot::impl)
        , m_object(object)
        , m_generation(generation)
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
            // The object is being destroyed: unregister before handing the news over - but only if
            // this watch is still the current one for that address.
            const auto entry = g_watchedObjects.find(self->m_object);
            if (entry != g_watchedObjects.end() && entry->second == self->m_generation)
                g_watchedObjects.erase(entry);
            if (g_destroyedCallback)
                g_destroyedCallback(self->m_object, self->m_generation);
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
    long long m_generation;
};

extern "C" QS_API_EXPORT void qs_api_set_destroyed_callback(QsApiDestroyedCallback callback)
{
    g_destroyedCallback = callback;
}

// How many objects the runtime is watching. A Qt object costs one entry; a value type (QPointF,
// QRect, QSize, ...) costs none, because it is a struct and never reaches this table. Exposed so a
// test can show that pushing a hundred thousand points through a bulk call adds nothing here - the
// shape of the leak a sampling chart had.
extern "C" QS_API_EXPORT int qs_api_watched_count()
{
    return static_cast<int>(g_watchedObjects.size());
}

// Watching is idempotent and cheap: one slot per object generation, and nothing for an object that
// was already watched. The generation is passed in by the managed side and comes back with the
// notification, so a notification that arrives after the address has been reused is recognisable.
extern "C" QS_API_EXPORT void qs_api_watch_destroyed(void* object, long long generation)
{
    if (!object || !g_destroyedCallback)
        return;
    const auto existing = g_watchedObjects.find(object);
    if (existing != g_watchedObjects.end() && existing->second == generation)
        return;
    g_watchedObjects[object] = generation;

    auto* sender = static_cast<QObject*>(object);
    const int index = sender->metaObject()->indexOfSignal("destroyed(QObject*)");
    if (index < 0)
        return;
    auto* slot = new QsApiDestroyedSlot(object, generation);
    const QMetaObject::Connection connection =
        QObjectPrivate::connect(sender, index, slot, Qt::DirectConnection);
    if (!connection)
        slot->destroyIfLastRef();
}

// PoC probe: call a virtual from native code so the generated C++ wrapper's override
// (and therefore the managed dispatch) is exercised.
extern "C" QS_API_EXPORT int qs_api_probe_event_filter(void* self, void* watched, void* event)
{
    return static_cast<QObject*>(self)->eventFilter(
        static_cast<QObject*>(watched), static_cast<QEvent*>(event)) ? 1 : 0;
}

// Diagnostic/lookup helper for the managed event layer: the numeric QEvent::Type.
extern "C" QS_API_EXPORT int qs_api_event_type(const void* event)
{
    return event ? int(static_cast<const QEvent*>(event)->type()) : -1;
}

// Delivers a user event, which QObject::event routes to the virtual customEvent(). Used to
// check that a managed override of a virtual declared in an *ancestor* class is reached.
extern "C" QS_API_EXPORT int qs_api_probe_user_event(void* object)
{
    QEvent event(QEvent::User);
    return QCoreApplication::sendEvent(static_cast<QObject*>(object), &event) ? 1 : 0;
}

// Delivers a synthetic mouse press, so a managed mousePressEvent override can be exercised
// (and its event payload read) without a real input device.
extern "C" QS_API_EXPORT int qs_api_probe_mouse_press(void* widget, int x, int y)
{
    const QPointF pos(x, y);
    QMouseEvent event(QEvent::MouseButtonPress, pos, pos,
                      Qt::LeftButton, Qt::LeftButton, Qt::NoModifier);
    return QCoreApplication::sendEvent(static_cast<QObject*>(widget), &event) ? 1 : 0;
}

// Delivers a synthetic mouse move with the given button mask held, so a managed mouseMoveEvent
// override can be exercised (a press/move/release sequence is what a drag looks like, and a
// managed widget cannot construct a QMouseEvent itself).
extern "C" QS_API_EXPORT int qs_api_probe_mouse_move(void* widget, int x, int y, int buttons)
{
    const QPointF pos(x, y);
    QMouseEvent event(QEvent::MouseMove, pos, pos,
                      Qt::NoButton, Qt::MouseButton(buttons), Qt::NoModifier);
    return QCoreApplication::sendEvent(static_cast<QObject*>(widget), &event) ? 1 : 0;
}

// Delivers a synthetic left mouse release, the other end of the drag above.
extern "C" QS_API_EXPORT int qs_api_probe_mouse_release(void* widget, int x, int y)
{
    const QPointF pos(x, y);
    QMouseEvent event(QEvent::MouseButtonRelease, pos, pos,
                      Qt::LeftButton, Qt::NoButton, Qt::NoModifier);
    return QCoreApplication::sendEvent(static_cast<QObject*>(widget), &event) ? 1 : 0;
}

// Delivers a synthetic wheel turn (the delta in eighths of a degree, as Qt's own events carry
// it, with the vertical component set - a wheel turn is a vertical scroll) so a managed
// wheelEvent override has a real QWheelEvent to read.
extern "C" QS_API_EXPORT int qs_api_probe_wheel(void* widget, int x, int y, int degrees, int modifiers)
{
    const QPointF pos(x, y);
    const QPoint angle(0, degrees * 8);
    QWheelEvent event(pos, pos, QPoint(0, 0), angle, Qt::NoButton,
                      Qt::NoModifier | Qt::KeyboardModifier(modifiers), Qt::NoScrollPhase, false);
    return QCoreApplication::sendEvent(static_cast<QObject*>(widget), &event) ? 1 : 0;
}

// Delivers a synthetic key press, so a managed keyPressEvent override can read the key code and
// the text the event carries.
extern "C" QS_API_EXPORT int qs_api_probe_key_press(void* widget, int key, int modifiers)
{
    const QString text = key < 0x10000 ? QString(QChar(key)) : QString();
    QKeyEvent event(QEvent::KeyPress, key, Qt::KeyboardModifier(modifiers), text);
    return QCoreApplication::sendEvent(static_cast<QObject*>(widget), &event) ? 1 : 0;
}

// ------------------------------------------------------------------ application bootstrap
// QApplication's constructor is QApplication(int &argc, char **argv, int flags): an int Qt
// rewrites and an array of pointers it keeps for the whole process. Neither can come from a
// per-call marshalling layer (the model has no vocabulary for char** at all, and a temporary
// argv would dangle the moment the call returns), so the command line is a runtime concern -
// the same hand-written conversion sip/shiboken make for this one signature. The strings are
// copied into process-lifetime storage, and the application object is built exactly once: a
// second call returns the one that exists, which is what Qt itself enforces.
namespace {
// The command line, copied out of the managed strings. This is initialized before the application
// object is built, so it is torn down *after* it - the argv pointers stay valid for as long as
// anything can read them.
struct QsApiArguments
{
    int argc = 0;
    std::vector<QByteArray> strings;
    std::vector<char*> argv;
};

QsApiArguments& Arguments()
{
    static QsApiArguments storage;
    return storage;
}

// Plain pointers with trivial destructors, so nothing is registered with the CRT's exit sequence:
// Qt has to be torn down while the .NET runtime can still answer (see qs_api_app_destroy), and a
// static destructor would run after the CLR is gone.
QCoreApplication*& Instance()
{
    static QCoreApplication* instance = nullptr;
    return instance;
}

// Whether the application is ours to destroy. A host can already have built one - the legacy
// pipeline in the same process, or a C++ program embedding the runtime - and Qt allows only one.
bool& OwnsInstance()
{
    static bool owns = false;
    return owns;
}

QCoreApplication* BuildApplication(const char* const* args, int count, int flags, int kind)
{
    // Qt insists on at least the program name being present.
    static const char* fallback[] = { "qtsharp" };
    if (count <= 0)
    {
        args = fallback;
        count = 1;
    }

    auto& storage = Arguments();
    storage.argc = count;
    storage.strings.reserve(size_t(count));
    for (int i = 0; i < count; ++i)
        storage.strings.emplace_back(args[i] ? args[i] : "");
    storage.argv.reserve(size_t(count) + 1);
    for (QByteArray& argument : storage.strings)
        storage.argv.push_back(argument.data());
    storage.argv.push_back(nullptr);

    switch (kind)
    {
    case 0:
        return new QCoreApplication(storage.argc, storage.argv.data(), flags);
    case 1:
        return new QGuiApplication(storage.argc, storage.argv.data(), flags);
    default:
        // The flags parameter is a plain int in Qt's declaration (defaulting to
        // Qt::ApplicationFlags), so it passes straight through.
        return new QApplication(storage.argc, storage.argv.data(), flags);
    }
}

QCoreApplication* Application(const char* const* args, int count, int flags, int kind)
{
    if (Instance())
        return Instance();
    // Adopt an application the host already built instead of constructing a second one: Qt has
    // exactly one, and the second constructor silently takes over QCoreApplication::instance()
    // while the first object stays alive - which then tears the process down twice.
    if (QCoreApplication* existing = QCoreApplication::instance())
    {
        Instance() = existing;
        return existing;
    }
    Instance() = BuildApplication(args, count, flags, kind);
    OwnsInstance() = true;
    return Instance();
}
} // namespace

// kind: 0 = QCoreApplication, 1 = QGuiApplication, 2 = QApplication (the default).
extern "C" QS_API_EXPORT void* qs_api_app_create_args(
    const char* const* args, int count, int flags, int kind)
{
    return Application(args, count, flags, kind);
}

// Tears the application down, and with it every widget Qt still owns. The managed runtime calls
// this from its ProcessExit handler, i.e. while managed code can still be entered: Qt's own
// teardown destroys objects whose vtable hooks and signal connections point back into managed
// code, and doing that from a DLL static destructor (after the CLR is gone) trips
// "Attempt to execute managed code after the .NET runtime thread state has been destroyed" - or,
// if that step is skipped entirely, leaves Qt's later static destructors running with a half
// destroyed platform integration. This is the explicit equivalent of Qt's own `QApplication app;`
// local in main, which is likewise destroyed before static teardown.
extern "C" QS_API_EXPORT void qs_api_app_destroy()
{
    // Only an application this runtime built, and only while Qt still considers it *the*
    // application: a host that built its own has already replaced (or will destroy) it.
    if (Instance() && OwnsInstance() && QCoreApplication::instance() == Instance())
        delete Instance();
    Instance() = nullptr;
    OwnsInstance() = false;
}

// ------------------------------------------------------------------ managed functors
//
// Qt's functor-taking overloads (QTimer::singleShot, QMetaObject::invokeMethod) are templates, so
// there is no exported symbol to bind. These helpers instantiate them with a lambda that carries a
// managed token and dispatcher instead:
//
//   methodId 0 - invoke the delegate (the functor fired)
//   methodId 1 - release the token (Qt destroyed the functor without calling it: the context
//                object died, or the pending event was discarded)
//
// The shared_ptr keeps the token alive for exactly as long as Qt holds the functor. The dispatch
// pointer is nulled before the invoke so the destructor that follows cannot release it a second
// time; if destruction wins the race (it cannot in practice: Qt runs the functor and its
// destruction on the same thread), only the destructor releases.

namespace
{
struct QsApiManagedFunctor
{
    QsApiManagedFunctor(void* token, QsApiDispatch dispatch) : token(token), dispatch(dispatch) {}
    QsApiManagedFunctor(const QsApiManagedFunctor&) = delete;
    QsApiManagedFunctor& operator=(const QsApiManagedFunctor&) = delete;

    void* token;
    QsApiDispatch dispatch;

    ~QsApiManagedFunctor()
    {
        if (dispatch)
        {
            QsApiDispatch release = dispatch;
            dispatch = nullptr;
            release(token, 1, nullptr);
        }
    }
};

QsApiDispatch TakeInvoke(QsApiManagedFunctor& functor)
{
    QsApiDispatch dispatch = functor.dispatch;
    functor.dispatch = nullptr;
    return dispatch;
}
}

extern "C" QS_API_EXPORT void qs_api_qtimer_single_shot_functor(int msec, void* token, QsApiDispatch dispatch)
{
    auto functor = std::make_shared<QsApiManagedFunctor>(token, dispatch);
    QTimer::singleShot(msec, [functor]() {
        if (QsApiDispatch invoke = TakeInvoke(*functor))
            invoke(functor->token, 0, nullptr);
    });
}

extern "C" QS_API_EXPORT void qs_api_qtimer_single_shot_functor_context(
    int msec, void* context, void* token, QsApiDispatch dispatch)
{
    auto functor = std::make_shared<QsApiManagedFunctor>(token, dispatch);
    QTimer::singleShot(msec, static_cast<QObject*>(context), [functor]() {
        if (QsApiDispatch invoke = TakeInvoke(*functor))
            invoke(functor->token, 0, nullptr);
    });
}

extern "C" QS_API_EXPORT bool qs_api_qmetaobject_invoke_method_functor(
    void* context, int connectionType, void* token, QsApiDispatch dispatch)
{
    auto functor = std::make_shared<QsApiManagedFunctor>(token, dispatch);
    return QMetaObject::invokeMethod(
        static_cast<QObject*>(context),
        [functor]() {
            if (QsApiDispatch invoke = TakeInvoke(*functor))
                invoke(functor->token, 0, nullptr);
        },
        static_cast<Qt::ConnectionType>(connectionType));
}

// ------------------------------------------------------------------ thread pool tasks
//
// QThreadPool::start takes a QRunnable, and a managed delegate is not one. This runnable carries
// the same token contract as the functors above - methodId 0 invokes, methodId 1 releases - with
// two differences that come from the pool owning its runnables:
//
//   * autoDelete stays on (Qt's default): the pool deletes the runnable after run() returns, so
//     the managed side must never dispose this object. Its destructor is the release path.
//   * QThreadPool::clear() discards queued runnables without running them; they are deleted, so
//     release fires without an invoke. The managed side turns that into a cancelled task instead
//     of leaving the TaskCompletionSource pending forever.
//
// The dispatch pointer is nulled before the invoke (TakeInvoke), so the destructor that follows a
// completed run cannot release the token a second time.

namespace
{
class QsApiThreadPoolTask : public QRunnable
{
public:
    QsApiThreadPoolTask(void* token, QsApiDispatch dispatch) : m_functor(token, dispatch)
    {
        setAutoDelete(true);
    }

    QsApiThreadPoolTask(const QsApiThreadPoolTask&) = delete;
    QsApiThreadPoolTask& operator=(const QsApiThreadPoolTask&) = delete;

    void run() override
    {
        if (QsApiDispatch invoke = TakeInvoke(m_functor))
            invoke(m_functor.token, 0, nullptr);
    }

private:
    QsApiManagedFunctor m_functor;
};
}

extern "C" QS_API_EXPORT void qs_api_threadpool_run_functor(
    void* pool, int priority, void* token, QsApiDispatch dispatch)
{
    static_cast<QThreadPool*>(pool)->start(new QsApiThreadPoolTask(token, dispatch), priority);
}

// ------------------------------------------------------------------ reflection lookup
//
// The QMetaObject-based counterpart of findChild<T>/findChildren<T>. Qt only publishes the
// templates, but the runtime filter is QMetaObject::cast, the reflection primitive, and doing the
// walk natively keeps the allocation profile of qt_qFindChildren_helper: only matched objects
// cross into managed code. `name` is UTF-16 and null/empty matches any object, exactly like a
// default-constructed QString; `pattern` is a QRegularExpression handle or null.

namespace
{
void CollectByMetaObject(QObject* parent, const QMetaObject* metaObject,
    const QRegularExpression* pattern, const QString& name, int options,
    bool firstOnly, QList<QObject*>& found)
{
    const QObjectList children = parent->children();
    for (QObject* child : children)
    {
        if (metaObject && !metaObject->cast(child))
            continue;
        if (!name.isEmpty() && child->objectName() != name)
            continue;
        if (pattern && !pattern->match(child->objectName()).hasMatch())
            continue;
        found.append(child);
        if (firstOnly)
            return;
    }
    if ((options & Qt::FindChildrenRecursively) == 0)
        return;
    for (QObject* child : children)
    {
        CollectByMetaObject(child, metaObject, pattern, name, options, firstOnly, found);
        if (firstOnly && !found.isEmpty())
            return;
    }
}
}

extern "C" QS_API_EXPORT void* qs_api_qobject_find_children(
    void* self, const void* metaObject, const void* pattern, const char16_t* name,
    int options, int firstOnly)
{
    auto* found = new QList<QObject*>();
    const QString wanted = name ? QString::fromUtf16(name) : QString();
    CollectByMetaObject(static_cast<QObject*>(self),
        static_cast<const QMetaObject*>(metaObject),
        static_cast<const QRegularExpression*>(pattern),
        wanted, options, firstOnly != 0, *found);
    return found;
}

extern "C" QS_API_EXPORT int qs_api_qobject_find_children_count(void* list)
{
    return static_cast<int>(static_cast<const QList<QObject*>*>(list)->size());
}

extern "C" QS_API_EXPORT void* qs_api_qobject_find_children_at(void* list, int index)
{
    return (*static_cast<const QList<QObject*>*>(list))[index];
}

extern "C" QS_API_EXPORT void qs_api_qobject_find_children_free(void* list)
{
    delete static_cast<QList<QObject*>*>(list);
}

// The regex half of the managed walk: a stack QRegularExpressionMatch instead of the owned
// wrapper QRegularExpression::match() returns, so a search over a large tree does not allocate
// one managed handle per visited object.
extern "C" QS_API_EXPORT int qs_api_qregularexpression_has_match(
    const void* pattern, const char16_t* subject)
{
    if (!pattern)
        return 0;
    const QString text = subject ? QString::fromUtf16(subject) : QString();
    return static_cast<const QRegularExpression*>(pattern)->match(text).hasMatch() ? 1 : 0;
}



