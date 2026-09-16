// PoC #3: ownership / lifetime.
//
// The question this answers: can .NET's non-deterministic GC coexist with Qt's
// parent-child ownership without double frees or use-after-free?
//
// The model under test:
//   * Qt-owned objects  -> the managed side stores only an IntPtr (no SafeHandle, no
//                          finalizer); Qt's parent-child owns the native object.
//   * Managed-owned     -> a SafeHandle releases the native object exactly once.
//   * In both cases     -> QObject::destroyed invalidates the managed wrapper so later
//                          access throws a managed exception instead of crashing.

#include <qobject.h>
#include <qstring.h>

static int g_liveTracked = 0;

// A QObject we can count, so tests can prove exactly-once construction/destruction.
class QsTracked : public QObject
{
public:
    explicit QsTracked(QObject* parent = nullptr) : QObject(parent) { ++g_liveTracked; }
    ~QsTracked() override { --g_liveTracked; }
};

extern "C" __declspec(dllexport) void* qs_own_tracked_create(void* parent)
{
    return new QsTracked(static_cast<QObject*>(parent));
}

extern "C" __declspec(dllexport) int qs_own_tracked_live()
{
    return g_liveTracked;
}

// Destroys an object the way Qt would: deleting a parent deletes its children too.
extern "C" __declspec(dllexport) void qs_own_delete(void* object)
{
    delete static_cast<QObject*>(object);
}

// Touch the object from the native side; dereferences the pointer, so it would crash if
// the managed side had freed an object it does not own.
extern "C" __declspec(dllexport) int qs_own_name_length(void* object)
{
    return int(static_cast<QObject*>(object)->objectName().size());
}

extern "C" __declspec(dllexport) void qs_own_set_name(void* object, const char* name)
{
    static_cast<QObject*>(object)->setObjectName(QString::fromUtf8(name));
}
