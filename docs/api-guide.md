# API guide

The API uses Qt concepts with C# properties, events, value types and collections.
See [Hello World](../samples/hello/Program.cs) for a complete entry point.

Each Qt module has its own namespace. When modules contain types with the same name, qualify
the type or use an alias, for example `using QTransform3D = QtSharp.Qt3DCore.QTransform;`.

## Properties

`Q_PROPERTY` becomes a C# property with the PascalCase name, and
the accessors keep their C++ spelling: `edit.Text`, `edit.text()`, `edit.SetText("...")`;
`timer.Interval`, `timer.interval()`. A property whose name would collide with a nested enum, a
virtual or a field is skipped and the accessors stay.

## Signals

Subscribe through C# events or use `QSignal` explicitly:
```csharp
button.Clicked += handler;          // C# event: -= really disconnects (QObject::disconnect)
button.ClickedSignal.Connect(handler);
button.ClickedSignal.Emit(false);   // manual emit; only QSignal has it
```
`Q_SIGNAL`s with more than one overload get the 2,3,… suffix (`Activated2Signal`/`Activated2`).
A handler can ask who sent the signal: `QObject.Sender` and `QObject.SenderSignal` are PyQt's
`sender()`/`senderSignalIndex()`. Inside a handler they name the emitting object (the same
wrapper instance, resolved through the wrapper cache) and the moc signature the connection used:
```csharp
button.Clicked += checkedState =>
{
    if (button.Sender == someButton) { ... }   // the wrapper itself, not a duplicate
    Log(button.SenderSignal);                  // "clicked(bool)"
};
```
Both are per thread and per callback: a nested emission sees the inner sender and restores the
outer one, and a queued delivery sees them on the thread that runs the slot. Outside a callback
both are null. A C#-declared `[QtSignal]` invoked directly runs its handlers after the emit
trampoline returned from Qt, so that path has no frame of its own; raise it with
`EmitSignal(ValueChanged, value)` to get one.

## Qt and .NET value types

Qt values provide explicit conversions to BCL types:
```csharp
var qdt = new QDateTime(dateTime);        // Utc -> Qt::UTC, Local/Unspecified -> Qt::LocalTime
DateTime back = qdt.ToDateTime();         // qdt.ToDateTimeOffset() keeps the offset
var qd = new QDate(dateOnly);             // qdate.ToDateOnly()
var qt = new QTime(timeOnly);             // qtime.ToTimeOnly()
byte[] data = qba.ToArray();              // new QByteArray(data) is binary-safe

Point pt = (Point)qpoint;                 // QPoint/QSize/QRect <-> System.Drawing
Color c = (Color)qcolor;                  // QColor <-> System.Drawing.Color
Vector3 v = (Vector3)qvector;             // QVector2D/3D/4D <-> System.Numerics
Quaternion q = (Quaternion)qquaternion;   // QQuaternion <-> System.Numerics.Quaternion
Uri uri = (Uri)qurl;                      // QUrl <-> System.Uri
Guid id = Guid.CreateV5(ns, name);        // QUuid is a Guid; v3/v5 are extensions on it
char upper = 'a'.ToUpper();               // QChar is a char; Unicode extras are extensions
object value = model.Data(index);         // a QVariant signature is object: QColor, int, string, ...
model.SetData(index, value);              // and back, without naming a variant type
```
Handle-wrapped types use identity equality. Convert explicitly when comparing them with BCL
values. Date/time conversions throw `InvalidOperationException` for invalid or out-of-range values.

One type goes the other way: **QUuid is `System.Guid`** - there is no QUuid type, the same way
there is no QString. It is 16 bytes of data, so every Qt signature that takes or returns one
surfaces as a `Guid` (value equality, no disposal), and Qt stores the same 16 bytes in a
QVariant's Uuid type. The Qt-only operations are C# 14 extensions on `Guid`
(`Guid.CreateV3`/`Guid.CreateV5`, `Guid.FromRfc4122`, `id.Version()`, `id.Variant()`,
`id.ToRfc4122()`), so a call site needs the .NET 10 compiler. A null QUuid is `Guid.Empty`;
parsing is `Guid.Parse`/`Guid.TryParse`; v4 and v7 ids are `Guid.NewGuid`/`Guid.CreateVersion7`;
Qt's string formats are Guid's "B"/"D"/"N".

QChar gets the same treatment: it is one UTF-16 code unit, so it is `char` and there is no QChar
type. The classification and case methods are extensions (`'A'.IsLetter()`, `'a'.ToUpper()`,
`'́'.CombiningClass()`), `Category()` returns the BCL's `System.Globalization.UnicodeCategory`,
and the properties the BCL has no API for (script, bidi direction, joining type, decomposition,
mirroring, case folding, Unicode version) call Qt's own tables. The static code-point overloads
are extensions too: `char.Category(0x41)`, `char.Script(0x41)`. A `QChar*` buffer is a `string`
(`QCollator.Compare`, `QRawFont.GlyphIndexesForChars`).

The net types have the same conversions: `new QHostAddress(ipAddress)` / `ToIPAddress()`,
`new QTimeZone(zoneInfo)` / `ToTimeZoneInfo()`, `new QProcessEnvironment(dictionary)` /
`ToDictionary()` (a snapshot; `InheritsFromParent` has no .NET equivalent),
`new QLocale(culture)` / `ToCultureInfo()` (identity only - Qt uses CLDR, .NET uses ICU),
`new QBitArray(bitArray)` / `ToBitArray()` (bit order preserved), `new QVersionNumber(version)` /
`ToVersion()` (one to four segments; Build/Revision -1 means omitted), and
`new QMatrix4x4(matrix4x4)` / `ToMatrix4x4()` (column-vector vs row-vector transposes).
`QCollator` implements `IComparer<string>`, so it can be handed to any BCL sort.

The same rule covers every other trivially copyable Qt value: **QModelIndex, QColor, QSizePolicy,
QTransform, QElapsedTimer, QDeadlineTimer, QOperatingSystemVersion, QTextLength, QRgba64,
QPixelFormat, QPointingDeviceUniqueId, QMetaMethod/QMetaEnum/QMetaClassInfo, QMetaType,
QCalendar, QMetaProperty, QTableWidgetSelectionRange, QTextLine, QTextInlineObject, QAudioFormat
and Qt3D's QNodeId/QKeyFrame are C# structs**, not handles - no `using`, no finalizer, value
equality. A parameter Qt passes as `const T&` crosses as the value, a `T&` as `ref`/`out`, and a
method that mutates in place writes back. `QCalendar()` (Gregorian) and `QKeyFrame()` (Bezier)
keep their parameterless constructors because their zero values are not Qt's defaults.
Equality and hashing are Qt's: the generated struct calls `operator==`/`qHash` when Qt declares
them and falls back to the value's fields (or bytes) when it does not, so `-0.0` vs `0.0`,
`QTextLength`'s fuzzy comparison and `QColor`'s spec rules are exactly Qt's. The comparison
crosses the native boundary once per call.
QMatrix4x4 is the one deliberate hold-out: its non-const `QMatrix4x4*` out parameters need a
dedicated rule before it can stop being a handle. `QMetaObject` stays a handle too: Qt returns it
as a `const QMetaObject*` to static data, and the struct-pointer convention would surface that as
an `IntPtr` instead.

## JSON collections

`QJsonObject` implements
`IDictionary<string, QJsonValue>` (a writable view over the Qt object; `Insert` returns the value
the C++ iterator points at) and `QJsonArray` implements `IList<QJsonValue>`. The LINQ-friendly
snapshots are `ToObjectDictionary()` and `ToObjectList()`, with values unwrapped the way a
variant's `ToObject()` unwraps them (nested objects as `Dictionary<string, object>`, arrays as
`object[]`). Object keys enumerate in Qt's sorted order.

## Streams and QIODevice

`StreamIODevice` (in `QtSharp.QtCore`) implements
`QIODevice`'s virtuals (`readData`/`writeData`/`seek`/`size`/...), so `QImage.Load`,
`QXmlStreamReader`, `QDataStream` and anything else taking a `QIODevice*` reads from or writes to
managed IO. The generator publishes QIODevice's virtuals through a small bridge registry
(`gen/Bridges.cs`), which is also where future bridges belong.

## Image buffers

`QImage`'s buffer constructors do not copy what they are given: the image reads the array until its
last copy is destroyed. The binding pins the array for that window and installs the cleanup that
releases the pin, then runs your `cleanupFunction`:
```csharp
var pixels = new byte[width * height * 4];          // ARGB32, filled however you like
using var image = new QImage(pixels, width, height, QImage.Format.Format_ARGB32);
pixels[0] = 0xFF;                                   // the image reads the same memory in place
```
This is PySide's buffer-plus-decref design with a .NET pin, because the GC may move a `byte[]`:
the address is stable only while the pin lives, so the array is not copied and writes through it are
visible in the image. `image.Copy()` returns an image with pixels of its own, but the original
keeps reading the pinned array until it is destroyed. An image handed to Qt (a copy kept elsewhere)
delays the cleanup until the last copy dies, exactly as in Qt; until then the array stays pinned
and reachable.

The generator recognizes the shape - a `uchar*` buffer followed by a `void(*)(void*)` cleanup
callback - and only that shape is pinned; a bare `uchar*` parameter is borrowed for the length of
the call.

## Application lifetime

Create one application object: `new QApplication(args)`,
`app.Exec()`, `app.Quit()`, `app.Exit(code)`. From code that has no application reference (a menu
action), use `QCoreApplication.Instance().Quit()`.

## Subclassing

`override` the virtuals the model publishes — `sizeHint` (the `SizeHint`
property reads it, the same way Qt's Q_PROPERTY does), `PaintEvent`, `MousePressEvent`,
`KeyPressEvent`, model/delegate virtuals (`RowCount`, `Data`, `Paint`, `CreateEditor`),
`Runnable.Run`, `FilterAcceptsRow`, `QGraphicsItem::boundingRect`/`paint`/`advance`, … The
native side forwards to your override. Only virtuals exposed by the generator can be
overridden; private and `final` C++ virtuals cannot be overridden.

## Declaring signals and properties

(PyQt's `pyqtSignal`/`pyqtProperty`): put the
attribute on the member, and the member is the declaration:
```csharp
[QtSignal] public event Action<int> ValueChanged;          // the Qt signal valueChanged(int)
[QtProperty] public int Value { get; set; }                // readable and writable
[QtProperty("caption")] public string Text { get; set; }   // an explicit Qt name
[QtProperty] public string Label => "read-only";           // get-only: a read-only property

void SetValue(int value)
{
    Value = value;
    ValueChanged?.Invoke(value);              // the pyqtSignal emit: Qt sees it, then the handlers
}
```
Raising the signal is invoking the event, the way PyQt's `signal.emit()` is: the runtime keeps
an emit trampoline in the event's handler field, so `?.Invoke` calls `QMetaObject::activate` and
a native Qt `connect` sees it too. `EmitSignal(ValueChanged, value)` is the explicit spelling of
the same emit. The trampoline is always there, so the event is never null and `?.Invoke` emits
even with no C# subscriber; `ValueChanged == null` is not a "has subscribers" test. Only
`+=`/`-=` the event: assigning it (`ValueChanged = null`) also drops the trampoline, and the
Invoke spellings stop reaching Qt.
The Qt name is the member's name with a lower-case first letter. C# events and native Qt
connections reach the same handlers, and Qt reaches a property through
`QObject::setProperty`/`property`, just like a moc-generated one.
`[QtSignal]` needs a field-like event (the compiler's handler list is what the runtime reads).
The rest of `Q_PROPERTY` maps to `QtProperty`'s properties: `Notify` (the signal named by its
C# member or Qt name; `<Property>Changed` is used when omitted), `Reset` (a parameterless
method; `Reset<Property>` by convention), `Constant`, `Final`, `Required`, `Designable`,
`Scriptable`, `Stored`, `User` and `Revision`. `BINDABLE` is not exposed: it needs binding
support in the native runtime, not just a flag.
`tests/MetaObjectApi.cs` is the same declaration under test.

## Slots and by-name invocation

`[QtSlot]` (PyQt's `pyqtSlot`) puts a public instance method in the meta-object as a real slot, so
Qt reaches it by name:
```csharp
[QtSlot] public int Add(int a, int b) => a + b;            // the Qt slot add(int,int)
[QtSlot("multiply")] public int Times(int a, int b) => a * b;   // an explicit Qt name
```
Overloads stay ordinary C# overloads and are told apart by their normalized signature
(`"add(int,int)"`); the attribute's argument renames the slot. Every parameter and a non-void
return type must have a Qt type name, and a method that cannot be a slot (static, generic,
`ref`/`out`, `params`, an unmapped type) throws when the meta-object is built instead of being
silently omitted. The slot runs through reflection, so a throwing slot is reported through
`ApiRuntime.UnhandledCallbackException` rather than unwinding into Qt. `QMetaObject.ConnectSlotsByName`
finds managed slots with the usual `on_<objectName>_<signal>` naming.

A method can also be called by name, the script/plugin case:
```csharp
QMetaObject.InvokeMethod(target, "setText", Qt.ConnectionType.DirectConnection,
    new object[] { "hi" }, out object result);
QMetaMethod.Invoke(target, args, out result);        // the resolved member, by index
```
The arguments may be C# primitives, `string`, `byte[]`, enums, bound Qt values and QObject
pointers; anything else crosses as a `QVariant` and is converted to the parameter's declared type.
A failure is `false` plus a reason, never an exception or a crash, and a queued call cannot carry
a result. The generated members carry `[QtNativeSlot(signature, invokable)]` so reflection can
tell a generated `Q_SLOTS`/`Q_SLOT` member from a `Q_INVOKABLE` one (Qt's `QMetaMethod::Slot`
vs `QMetaMethod::Method`).
The generated Qt classes are deliberately different: their properties and signals already exist
in moc's meta-object, so they do not carry these attributes. Their types are marked
`[GeneratedCode("qtgen", …)]`, and the XML docs say what a member is — for example
`The windowTitle property (Q_PROPERTY, QString, read/write)` and
`The windowTitleChanged(QString) signal as a C# event`. `[QtProperty]`/`[QtSignal]` are only for
members *you* declare on a C# subclass. A generated member instead carries the read-side
`[QtNativeProperty(name, type, writable)]` or `[QtNativeSignal(mocSignature)]`, so reflection
and tooling can tell the two apart without guessing from the type. Only the types the
hand-written runtime extends (the application classes, `QObject`, and the date/time/byte-array
types) are `partial`; every other
wrapper is a whole declaration, and `unsafe` sits on the few constructors that pass a function
pointer rather than on the class.

## Object wrappers are actual types

A wrapper is created as the object's actual bound class, the PyQt/PySide object model, so `is`/`as`
work on values Qt hands back as a base type:
```csharp
foreach (var child in spinBox.Children())      // QObject::children() is QList<QObject*>
    if (child is QLineEdit edit) { ... }       // the editor is a QLineEdit wrapper, not a QObject one
QPushButton button = (QPushButton)widget.Window();   // the window was created as a QPushButton
```
The type is resolved once, when the wrapper is made, from the native meta-object: an existing
compatible wrapper wins (reference identity, and a C# subclass keeps its own type), then the
meta-object chain gives the most-derived class the binding publishes, and an unpublished internal
class resolves to its nearest bound base (`QComboBox.View()` is a `QListView`). The declared type
is the fallback when nothing matches, for example a module whose assembly has not loaded yet.
`QList<QObject*>` elements are read through the identity cache, so repeated `Children()` calls
return the same wrapper instances.
The same resolution powers the two lookups Qt only offers as templates, rewritten as C# generics:
```csharp
QPushButton button = window.FindChild<QPushButton>();                  // first child of that type
QLineEdit edit = window.FindChild<QLineEdit>("edit");                  // ... with a matching objectName
QWidget[] all = window.FindChildren<QWidget>();                        // the whole subtree
QWidget[] direct = window.FindChildren<QWidget>(null, Qt.FindChildOptions.FindDirectChildrenOnly);
```
The order is Qt's (`findChildren` tests a whole level before descending), `null` or an empty name
matches every object, a miss is `null` or an empty array, and a managed subclass is found by its
own type as well as by its base. `tests/FindChildApi.cs` is the same lookup under test.

## Ownership

For disposable wrappers, `Dispose()` releases native objects owned by the
wrapper. Objects transferred to a Qt parent, layout, scene or view follow that owner's
lifetime. Native destruction invalidates the managed wrapper; subsequent handle access
throws `ObjectDisposedException`. See the [ownership tests](../tests/OwnershipApi.cs) and
[known limitations](limitations.md) for covered cases and remaining teardown issues.

## Object liveness

A wrapper whose Qt object is gone compares equal to null the
way a destroyed `UnityEngine.Object` does, so `if (w != null) w.Use();` is the guard to write, and
`w.IsAlive` asks the same question by name (PyQt's `sip.isValid`). Only the operator is
liveness-aware — `w?.Use()`, `w ?? fallback` and `w is null` test the reference itself, exactly as
they do for ordinary C# references.

## Callback exceptions

The runtime catches exceptions from signal handlers and virtual
overrides at the native boundary, writes them to stderr and raises
`ApiRuntime.UnhandledCallbackException`. Subscribe to this event to handle or report them.

## Function pointers and callbacks

Qt's C function-pointer APIs take an `Action` (void return) or `Func<..., R>`, and the same
delegate comes back when Qt hands the pointer out again:
```csharp
var curve = new QEasingCurve();
curve.SetCustomType(p => p * p);              // double(*)(qreal)
Func<double, double> installed = curve.CustomType();   // the same delegate instance
var previous = Qt.QInstallMessageHandler(handler);     // Action<QtMsgType, IntPtr, string>?
```
The native side stores only the address, so the binding roots the thunk and the caller's delegate
in `QtCallbackRegistry` for the process lifetime; the registry is keyed by delegate instance, so
the same instance always gives the same address (`qRemovePostRoutine` needs that) and a callback
installed through the binding comes back as the very instance that was installed. A `QString`
callback argument is borrowed and copied with `ApiRuntime.ToManagedTransient`; a pointer argument
is an `IntPtr`. A callback parameter on a virtual or a signal is not published, and a variadic,
nested or class-returning callback stays out of the vocabulary.

`QFunctionPointer` is the deliberate exception: Qt promises no signature for `QLibrary.Resolve`,
`QGuiApplication.PlatformFunction` or `QOpenGLContext.GetProcAddress`, so it surfaces as the
one-field `QtSharp.QtCore.QFunctionPointer` struct rather than a callable:
```csharp
QFunctionPointer address = lib.Resolve("qVersion");       // a carrier, not a Func
if (!address.IsNull && address.TryAs<Func<IntPtr>>(out var version))
    Console.WriteLine(Marshal.PtrToStringAnsi(version()));
```
`As<TDelegate>`/`TryAs<TDelegate>` are the explicit opt-in, the C# spelling of C++'s
`reinterpret_cast`: the caller vouches for the signature, and a wrong signature or a stale address
is a crash, not an exception. Identity is a property of this binding's registry; a pointer
installed by native code returns a wrapper over the same address. `docs/model-vocabulary.md` has
the full rule and the lifetime notes.

## Async and threads

C# has `await`, so the binding does not mirror `QFuture<T>` — a template with nothing to bind.
Instead it installs a `SynchronizationContext` on the Qt thread, the same role PySide's
`QtAsyncio` plays for asyncio, and maps Qt's two threading primitives onto `Task`:
```csharp
var sync = QtSynchronizationContext.Install();          // the GUI thread, after the QApplication

async void OnActivated()
{
    var rows = await Task.Run(() => ReadRows());        // .NET worker thread
    list.AddItems(rows);                                // back on the GUI thread
}

using var pool = new QThreadPool();
int count = await pool.RunAsync(() => CountRows());     // Qt's thread pool, result as a Task
await window.InvokeAsync(() => SetStatus("done"));      // queued onto the object's thread
```

`QtSynchronizationContext.Post` queues a continuation with `QMetaObject::invokeMethod` and
`QueuedConnection`, so the target thread must be pumping an event loop. `Send` runs inline on the
target thread and blocks from any other. Neither `QFutureWatcher<T>` nor `QtConcurrent` is bound:
the first is a template, and the second is templates all the way down; `QThreadPool.RunAsync` is
the same capability without them. PyQt and PySide do not expose them either.

`RunAsync` takes an optional `CancellationToken`; the token is checked when a worker picks the task
up, so cancelling later needs cooperation inside the work. A task whose queued invocation is
dropped — the pool was cleared, or the context object was destroyed before Qt dispatched it — comes
back cancelled rather than pending. Exceptions from the work fault the task; nothing unwinds into
Qt's C++ frames. `test-async-await` covers the thread hops, results, faults, cancellation and the
context-lifetime paths.

## Event arguments

Qt event arguments expose methods such as `e.Position()`, `e.Key()`, `e.Buttons()` and
`e.TimerId()`. They are transient; do not retain them after the callback returns.
