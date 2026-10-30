# Changelog

All notable changes to QtSharp will be documented here.

The project is pre-1.0, so APIs and package layouts may change between releases.

## 0.25.0 - 2026-10-30

- **`async`/`await` on the Qt thread (`QtSynchronizationContext`).** `QtSynchronizationContext.Install()`
  installs a `SynchronizationContext` that queues continuations with
  `QMetaObject::invokeMethod`/`QueuedConnection`, so `await Task.Run(...)` in a handler resumes on
  the GUI thread — the C# counterpart of PySide's `QtAsyncio` scheduler, without a second event
  loop. `Post` is always asynchronous even from the target thread; `Send` runs inline there and
  blocks from elsewhere. A continuation posted to a destroyed context object is dropped, the same
  way Qt drops a queued functor whose context died.
- **`QThreadPool.RunAsync` and `QObject.InvokeAsync` — `Task` in place of `QFuture`.** `QFuture<T>`,
  `QFutureWatcher<T>`, `QPromise<T>` and `QtConcurrent` are templates and stay unbound (PyQt and
  PySide do not expose them either); these helpers cover the same ground: work on Qt's thread pool
  with an optional `CancellationToken`, and a queued invocation on a specific object's thread, both
  returning `Task`/`Task<T>`. Exceptions fault the task instead of unwinding into Qt; a task Qt
  drops without running (pool cleared, context destroyed) completes as cancelled instead of
  hanging. `test-async-await` covers the hops, results, faults, cancellation and lifetime paths.

## 0.24.0 - 2026-10-30

- **QMetaObject-based `FindChild`/`FindChildren` (reflection lookup).**
  `FindChild(QMetaObject, ...)` and `FindChildren(QMetaObject, ...)`, with a name or a
  `QRegularExpression`, filter by `QMetaObject::cast` at runtime — the C++ reflection primitive,
  and the equivalent of PyQt's `findChild(type)`. The walk runs natively (Qt's order and
  allocation profile: only matched objects are wrapped), and a managed subclass matches its Qt
  base's meta-object through the inheritance chain.
- **Cheaper managed lookup.** The generic walk no longer reads `ObjectName` when the name matches
  everything, and its pattern path uses a native `hasMatch` helper instead of allocating a
  `QRegularExpressionMatch` per visited object.
- **Regular-expression `FindChild`/`FindChildren`.** `FindChild<T>(QRegularExpression, ...)` and
  `FindChildren<T>(QRegularExpression, ...)` add Qt's pattern overloads: the pattern is searched
  against each candidate's object name (`re.match(name).hasMatch()`, so `"di"` matches `"edit"`),
  with the same type filter, Qt traversal order and `Qt.FindChildOptions`. A null pattern throws
  `ArgumentNullException`. Because the name and pattern overloads are both reference types, a bare
  `null` argument now needs a cast: `FindChild<T>((string)null)`.
- **Qt's functor-taking template APIs.** `QTimer.singleShot(int, Action)`,
  `QTimer.singleShot(int, QObject, Action)` and
  `QMetaObject.InvokeMethod(QObject, Action, Qt.ConnectionType)` bind the
  `QTimer::singleShot` and `QMetaObject::invokeMethod` functor overloads, which are C++ templates
  and therefore have no exported symbol for the generator to see. The native runtime instantiates
  them around a managed token; the delegate runs on the target thread, and the token is released
  through native code when Qt destroys the functor without calling it (the context object died, the
  queued event was discarded), so a cancelled call does not root its delegate until process exit.
  The functor-based `QObject::connect` overloads are not added: a signal connection is already a
  C# event. `test-functor-interop` covers firing, context cancellation and the queued invocation.

## 0.23.0 - 2026-10-30

- **BCL -> Qt extension methods (`ToQ*`).** The bridges now have a source-side spelling:
  `dateTime.ToQDateTime()`, `dateOnly.ToQDate()`, `timeOnly.ToQTime()`, `uri.ToQUrl()`,
  `culture.ToQLocale()`, `zone.ToQTimeZone()`, `version.ToQVersionNumber()`,
  `bits.ToQBitArray()`, `regex.ToQRegularExpression()`, `bytes.ToQByteArray()`,
  `node.ToQJsonValue()`/`jsonObject.ToQJsonObject()`/`jsonArray.ToQJsonArray()`,
  `directory.ToQDir()`, `info.ToQFileInfo()`; QtGui's `point.ToQPoint()`/`ToQPointF()`,
  `size.ToQSize()`/`ToQSizeF()`, `rectangle.ToQRect()`/`ToQRectF()`, `color.ToQColor()`,
  `vector.ToQVector2D()`/`ToQVector3D()`/`ToQVector4D()`, `quaternion.ToQQuaternion()`,
  `matrix.ToQMatrix4x4()`; and QtNetwork's `address.ToQHostAddress()`. They replace the named
  constructors at the call site (`new QPointF(x, y)` -> `point.ToQPointF()`); because a method
  name is new, the overload resolution of existing calls does not change. A handle result is the
  caller's to dispose, a null reference receiver throws `ArgumentNullException`, and a null
  JsonNode converts to `QJsonValue::Null` (in a JSON tree that is what null means).
- **System.Text.Json bridges for the JSON DOM.** `QJsonValue.FromJsonNode(JsonNode)` and
  `ToJsonNode()`, `QJsonObject(JsonObject)`/`ToJsonObject()`,
  `QJsonArray(JsonArray)`/`ToJsonArray()`, and `QJsonDocument.FromJsonNode(JsonNode)`/
  `ToJsonNode()`. `System.Text.Json.Nodes` is the counterpart rather than
  `JsonDocument`/`JsonElement`: a `JsonNode` owns its data, while a `JsonElement` is only valid
  for as long as its document lives. Numbers cross as doubles (Qt's representation), a null node
  and Qt's Undefined both become null, and Qt's sorted object key order is kept.
- **`QTextStream.ToTextReader()`.** A `TextReader` over Qt's decoded text stream: `Peek` uses a
  one-character lookahead, `ReadLine`/`ReadToEnd` come from the stream, and `leaveOpen` decides
  whether disposing the reader disposes the `QTextStream`. There is no `TextWriter` counterpart:
  QTextStream writes through `operator<<`, which is not bound; use `QIODevice.AsStream()` with a
  `StreamWriter` instead. (The XML stream pair is still out of reach: `QXmlStreamAttributes` is
  not in the model, so `QXmlStreamReader.Attributes()` is not bound and an `XmlReader` adapter
  could not see attributes.)
- **System.IO bridges for the filesystem and QIODevice.** `QDir(DirectoryInfo)`/`ToDirectoryInfo()`,
  `QFileInfo(FileSystemInfo)` with `ToFileSystemInfo()` plus the strict `ToFileInfo()`/
  `ToDirectoryInfo()` (a type mismatch throws instead of returning the other type; a path that does
  not exist yet converts to an `FileInfo`, the only choice `FileSystemInfo` allows), and
  `QDirIterator.EnumeratePaths()`/`EnumerateFileInfos()` for `foreach` (the extensions dispose the
  iterator they own, including when the loop breaks early). `QIODevice.AsStream()` is the missing
  direction of `StreamIODevice`: an open Qt device becomes a `System.IO.Stream`, for a seekable
  device `Position`/`Length`/`Seek` follow `pos()`/`size()`/`seek()`, `Flush` reaches
  `QFileDevice::flush`, `SetLength` reaches `QFileDevice::resize` (`NotSupportedException` for a
  QBuffer), and `leaveOpen: false` closes the Qt device on dispose. The conversions stay explicit:
  `QDir`/`QFileInfo` keep their Qt spelling because their filter/sort state, the FileInfo/
  DirectoryInfo split and native ownership have no lossless BCL equivalent. `test-filesystem-interop`
  covers both directions, the iterator lifetime and binary writes.
- **Fixed: the `writeData` hooks carried binary data as a string.** The derived declarations
  (`QBuffer`, `QFileDevice`, `QAbstractSocket`, `QLocalSocket`, `QNetworkReply`, `QProcess`,
  `QSaveFile`) were bound as `WriteData(string, long)`, so the managed dispatch round-tripped the
  buffer through UTF-8: writing `0xFA` to a QBuffer stored `0xEF`. `QioDeviceBridge` now maps
  `const char *` + length on every declaring class to the same `IntPtr` buffer the `QIODevice` base
  already used. A managed override of those hooks changes from `string` to `IntPtr`.

## 0.22.0 - 2026-10-29

- **`QtSharp.WebView`: a QWidget host for Edge WebView2.** `WebView2Widget` puts web content in a
  QtSharp window without binding QtWebEngine and without shipping Chromium: the browser is the
  Microsoft Edge WebView2 runtime the system updates. The widget is a plain `QWidget` in the Qt
  layout; it parents the WebView2 controller to its own `WinId()`, keeps bounds, rasterization
  scale and visibility in step with Qt's resize/show/hide/DPI events, and pumps Qt's event loop
  while WebView2 completes its asynchronous start-up (a plain `GetResult()` on the UI thread would
  deadlock on the message-queue completion). `Navigate`, `NavigateToString`, `ExecuteScript(Async)`,
  `PostWebMessage`, the navigation/process events and `RunOnUiThread` (a queued `[QtSlot]`
  invocation) are exposed; the raw `CoreWebView2` remains available for the rest. The package is
  `QtSharp.WebView.win-x64`, depends on `QtSharp.Api.win-x64` and `Microsoft.Web.WebView2`, and
  the Windows release archive carries that dependency so a local feed restores offline. Windows
  only: WebView2 has no Linux implementation, and Qt WebView (QtWebEngine on Linux) remains the
  cross-platform route. `test-webview2` asserts load, title, script execution and the worker-thread
  dispatch under a real window; the offscreen gate reports it as skipped. Airspace is the known
  limitation: the native browser window cannot be painted over by Qt content. The research and the
  measured cost of QtWebEngine/QtWebView are in `docs/webview.md`.

## 0.21.0 - 2026-10-29

- **C function pointers are part of the model.** A `ret(*)(T1,T2)` type now maps to `Action` /
  `Func<..., R>` in both positions, so `QEasingCurve.SetCustomType`/`CustomType`,
  `qInstallMessageHandler`, `qAddPreRoutine`/`qAddPostRoutine`/`qRemovePostRoutine` and the QImage
  cleanup constructors are bound. The runtime roots each thunk and the caller's delegate and keys
  them by instance, so a callback Qt hands back is the very delegate that was installed
  (`curve.CustomType()` after `SetCustomType(f)` is `f` by reference) and `qRemovePostRoutine` finds
  the routine `qAddPostRoutine` registered. A callback parameter on a virtual or a signal stays out,
  as does a variadic or nested one. `QFunctionPointer` - Qt's signature-less "pointer to a
  function" - keeps its own name and surfaces as the `QtSharp.QtCore.QFunctionPointer` carrier:
  explicit `IntPtr` conversions, equality by address, and `As<TDelegate>()`/`TryAs<TDelegate>()` at
  the call site, which is where the C++ `reinterpret_cast` lives. `docs/model-vocabulary.md` has
  the full rule.
- **QObject wrappers are their actual bound type** (issue #11). `FromHandle`,
  `FromTransientHandle` and `FromOwnedHandle` resolve the most-derived bound class through the
  native meta-object before the wrapper is made, so `is`/`as` work on a `QObject*` return, a
  `QList<QObject*>` element (`QObject.Children()`), a signal argument and a parent/child accessor.
  An existing compatible wrapper wins (identity and C# subclasses), an unpublished internal class
  resolves to its nearest bound base (`QComboBox.View()` is a `QListView`), and the declared type is
  the fallback when the module is not loaded. Owned returns set the ownership flag inside the actual
  type's own class.
- **`QObject.FindChild`/`FindChildren` as C# generics.** Qt only offers the two lookups as
  templates; they are now methods over the wrapper tree, with Qt's order (a whole level is tested
  before the search descends), `null`/empty name matching any object, `Qt.FindChildOptions` deciding
  whether it descends, and managed subclasses found by their own type.
- **`QImage` buffer constructors pin their `byte[]`.** Qt does not copy the buffer the image is
  built from; it reads it until its last copy is destroyed and then calls the cleanup function. The
  binding now pins the array for exactly that window (`QtSharp.Runtime.PinnedBuffer`), hands Qt a
  cleanup that first runs the caller's `cleanupFunction(cleanupInfo)` and then releases the pin and
  its thunk registry entry, and releases it directly when the native call fails. This is PySide6's
  buffer-plus-decref design in C#, where the reference has to be a pin because the GC may move the
  array.
- Also in this release: the 0.20.1 fixes below (the Windows package's headless platform plugins, and
  the QImage cleanup constructor's function-pointer parameter type on Linux), plus the
  `collidingmice` example no longer culls an edge item from its own paint assertion.

## 0.20.1 - 2026-10-29

- **The Windows package carries the headless platforms.** 0.20.0 said automated runs are headless,
  and the kit stages `qoffscreen.dll` and `qminimal.dll` for that, but the packaging project listed
  only `platforms/qwindows.dll` and `buildTransitive` copied only that one: a consumer setting
  `QT_QPA_PLATFORM=offscreen` had no such plugin and Qt aborted the process instead of drawing
  nothing. Both are packed now (when the kit produced them) and copied next to the executable on
  Build and on Publish, so the recipe in the 0.20.0 note works for a consumer of the package, not
  only for this repository's own test host.
- **The `QImage` cleanup-function constructor compiles on Linux.** The generated subclass
  constructor declared the function-pointer parameter as `void*` while the entry point handed it the
  address cast to `QImageCleanupFunction`; g++ rejects that conversion (MSVC accepts it as an
  extension), so the Linux packages could not be built. The constructor now takes the type Qt
  declared. `test-imagebuffer` covers the path.

## 0.20.0 - 2026-10-28

- **Generated methods are marked as Qt slots.** A method Qt declares in a `Q_SLOTS`/`Q_SLOT`
  section or with `Q_INVOKABLE` now carries `[QtNativeSlot(signature, invokable)]`, the read-side
  counterpart of `[QtNativeSignal]`/`[QtNativeProperty]` (`invokable: true` is Qt's
  `QMetaMethod::Method`, the default is a `QMetaMethod::Slot`). The extractor records the new
  `slot`/`invokable` method flags; `test-nativeslot` checks that the signatures resolve through
  `QMetaObject::indexOfMethod`.
- **The dynamic meta-object cache is keyed by the base class too.** Two C# types can share a simple
  name (different namespaces) and declare identical members while deriving from different Qt
  classes; the built table used to be reused for both, so the second type counted its method and
  property ids from the wrong superclass. The cache key now includes the base's meta-object, and
  `test-metaobjectcache` covers the collision.
- **`QBindable<T>` is removed from the public API.** The generated `bindableFoo()` handles exposed
  only `Value` (get/set) and `Dispose`, which the normal property accessors already provide, while
  the members that make a bindable distinct (`setBinding`, `onValueChanged`, `hasBinding`, …) were
  not bound. Use the property accessor and its NOTIFY signal. The properties without a NOTIFY
  signal (`QTimer.active`/`interval`/`singleShot`/`timerType`, `QMovie.cacheMode`/`speed`,
  `QGeoPositionInfoSource.updateInterval`) are consequently unobservable until dependency-tracked
  bindings or per-class signals are added. The model vocabulary and the rule for extending it are
  documented in `docs/model-vocabulary.md`.
- **Automated runs are headless.** `qoffscreen.dll` / `qminimal.dll` are staged by the Qt kit and
  copied next to every built app, and the example/test gate runs with `QT_QPA_PLATFORM=offscreen`, so
  the conformance suite no longer maps windows on the desktop. The hosts write that variable before
  the `QApplication` exists; on Unix they go through `setenv` because .NET keeps its own copy of the
  environment there and Qt reads the C library's. Checks that need a real font engine report an
  `[info]` line where the headless platform cannot provide one.
- **Explicit regex interop.** `QRegularExpressionInterop` converts a `QRegularExpression` to a
  `Regex` and back, carrying the five options both engines spell the same way and throwing for the
  rest. See the README for the mapping and the engine caveat.
- **One C#-to-Qt type-name mapper.** `MetaType.QtName` is used by declared members and by the new
  `QMetaType.FromType<T>()`, with a generated per-module table that preserves Qt's spelling
  (`Qt3DCore::QEntity*`) and distinguishes value classes from QObject pointers.
- **Disposable signal connections.** `QSignal.Connect` returns a `QtConnection`; `Dispose`
  disconnects exactly that connection, and overloads take a `Qt.ConnectionType` and a context
  `QObject`.
- **Owned method returns.** `QScrollArea.TakeWidget()` and `QWidget.CreateWindowContainer()` without
  a parent return a wrapper whose `Dispose` deletes the object. Ownership is a model annotation
  (`ownedReturn`, and `ownedWhenNull` for the conditional case) that the generator turns into
  `FromOwnedHandle`, so a parented container stays Qt's and is never deleted twice.
- **`QObject.Sender` and `QObject.SenderSignal`.** A managed signal handler can ask which object
  emitted the signal and which moc signature the connection used (PyQt's `sender()` /
  `senderSignalIndex()`). The values are per thread and per callback; a nested emission restores the
  outer frame, and a queued delivery sees them on the thread that runs the slot. A C#-declared
  `[QtSignal]` invoked directly has no frame of its own — documented on both members.
- **Dynamic invocation.** `QMetaMethod.Invoke` and `QMetaObject.InvokeMethod` call a method by name
  or normalized signature through a compact native shim. Primitives, `string`, `byte[]`, enums,
  bound Qt values and QObject pointers cross; the result arrives through the QVariant bridge. A
  failure is `false` plus a reason, never an exception or a crash (a queued call cannot carry a
  result).
- **More explicit BCL interop.** `QHostAddress` ↔ `IPAddress`, `QTimeZone` ↔ `TimeZoneInfo`,
  `QProcessEnvironment` ↔ `IReadOnlyDictionary`, `QLocale` ↔ `CultureInfo`, `QBitArray` ↔
  `BitArray`, `QVersionNumber` ↔ `Version`, `QMatrix4x4` ↔ `System.Numerics.Matrix4x4`, and
  `QCollator` implementing `IComparer<string>`. The README lists every mapping and its exception
  policy; the QNetwork conversions live in `runtime/QtNetwork/`.
- **C# methods as real Qt slots.** `[QtSlot]` registers a public instance method in the class's
  dynamic meta-object, so Qt can reach it by name (`QMetaObject.InvokeMethod`, `QMetaMethod.Invoke`)
  and `QMetaObject.ConnectSlotsByName` finds the `on_<objectName>_<signal>` spelling. Overloads are
  told apart by their normalized signature, `[QtSlot("name")]` renames the slot, a declaration that
  cannot be a slot throws at construction, and a throwing slot is reported through
  `UnhandledCallbackException` instead of unwinding into C++.

## 0.19.0 - 2026-10-27

- **One package per platform.** Every binding and Qt-runtime package is now published twice, with
  the RID in the id: `QtSharp.Api.win-x64` / `QtSharp.Api.linux-x64`, and the same for Runtime,
  Qt6, Charts, Qt3D, Multimedia, Positioning and Sql. Only the matching platform's Qt runtime and
  native glue are inside, so a Windows consumer no longer downloads the Linux Qt tree and vice
  versa (`QtSharp.Qt6` alone carried both and weighed 97 MB). The managed assemblies are identical
  in both variants; `QtSharp.Tools` stays Windows-only.
  - A project references the variant for the machine it builds on. The buildTransitive targets
    deploy that package's payload (flat DLLs + `platforms/` on Windows, `lib/` + `plugins/` +
    `qt.conf` on Linux), and `pack-qt-binding.ps1` asserts every variant contains its own payload
    and none of the other platform's.
  - `tools/test-packages.ps1` picks the variant from the host, so the Windows and Linux package
    smoke jobs each restore only their own packages.

- **The item roles take the named enum.** Qt declares a role as `int` because roles are an open
  namespace (a custom role is `Qt.UserRole + n`), so a C# call site needed `(int)` for every
  standard role. The role-taking members now have overloads for `Qt.ItemDataRole`:
  `QAbstractItemModel.Data`/`SetData`/`HeaderData`/`SetHeaderData`/`Match`, a
  `SetItemData` that takes an enum-keyed dictionary plus `ItemDataByRole`, `QModelIndex.Data`,
  `QPersistentModelIndex.Data`, `QStandardItem.Data`/`SetData`, `QFileSystemModel.MyComputer`,
  the `QComboBox` item-data accessors, the three widget items, the proxy sort/filter roles, and
  `QCompleter.SetCompletionRole`. `QTextCharFormat.SetFontWeight` takes `QFont.Weight`. The int
  overload stays the primary spelling, so custom roles keep working.

## 0.18.0 - 2026-10-27

- **QVariant is not in the API; values cross as `object`.** Every generated signature that meant a
  QVariant now says `object`, the way PySide6 exposes it (the class is not part of the API there at
  all): a model's `Data()` returns the `QColor` itself, a combo's item data comes back as the
  payload, and Qt's invalid variant is `null`. The wrapper converts at the boundary - it builds a
  real QVariant for a call and unwraps (and releases) the one a call returns - so a call site no
  longer holds or disposes a temporary variant. There is no QVariant type in the API at all: the
  class is internal to QtCore, the implicit `operator QVariant` conversions are gone, the
  deprecated `QVariant::Type` appears as `int` (`QMetaProperty.Type()`), and the generated code
  crosses through an internal handle lease rather than a wrapper object.
  - The type table is generated from qvariant.h: every type QVariant has a constructor for
    (QPoint, QUrl, QDate, QModelIndex, QJsonValue, ...), plus the trivially copyable types whose
    only route in is `operator QVariant()` (QVector2D, QQuaternion). `QVariant.From(qpoint)` is a
    real QPoint variant instead of an opaque payload.
  - Arrays, `IList` and string-keyed dictionaries recurse into QVariantList/QVariantMap;
    `ToList`/`ToMap`/`ToHash` hand back the unwrapped `object[]`/`Dictionary<string, object>`.
    Small integers and enums widen to the Qt type that means the same thing (`byte` -> int, a
    `Qt.ItemDataRole` -> int), a `char` becomes a QChar variant, and `To<T>()` can ask for the Qt
    type even where `ToObject()` prefers the BCL one (QUrl -> Uri).
  - The generator classifies a model type once (`TypeKind`) instead of re-testing the same string
    through twenty `IsXxx` predicates, and reads the model's `ParamMode` enum for in/out/ref
    instead of comparing the mode string. The generated trees are byte-identical across that
    refactor.

- **QUuid is `System.Guid`, and there is no QUuid type** - the same treatment QString gets
  (`string`). A QUuid is 16 bytes of data that owns nothing, so it was the one trivially copyable
  type still modelled as a handle: a native allocation, a wrapper-registry entry, a finalizer and
  reference equality per value. Every Qt signature that takes or returns one now surfaces as
  `Guid`, which crosses the ABI as the same 16 bytes and gets value equality and no disposal for
  free.
  - The Qt-only operations are extensions on `Guid`: `Guid.CreateV3`/`Guid.CreateV5` (RFC 4122,
    checked against the RFC's DNS example), `Guid.FromRfc4122`, `id.Version()`, `id.Variant()` and
    `id.ToRfc4122()`. They are C# 14 extension members, so a call site needs the .NET 10 compiler.
  - The version and variant results are `UuidVersion` and `UuidVariant`. Construction, parsing,
    v4/v7 and the B/D/N string forms stay the BCL's (`Guid.Parse`, `Guid.NewGuid`,
    `Guid.CreateVersion7`, `Guid.ToString("N")`).
  - QUuid is no longer exported as a native class either; the glue keeps only the inline
    `QsApiGuid` conversion the QVariant/QCborValue/qHash entry points need.
  - Semantics that changed: a null QUuid is `Guid.Empty` (an indistinguishability the old
    `ToGuid()` already had), and an unparseable string fails `TryParse` instead of becoming a
    silent null QUuid. `QUuid` values and the `(Guid)quuid` cast are gone - the value *is* a Guid.
  - Tests cover the RFC v3/v5 vectors, both byte orders, version/variant, `QVariant.type() == Uuid`,
    QCborValue and qHash, plus leak checks: 10,000 conversions register no wrappers and watch no
    objects, and 200 Guid variants leave the managed payload table and the native box counter at
    zero. The tests also assert that no `QtSharp.QtCore.QUuid` type exists.

- **QChar is `System.Char`, and there is no QChar type** - the same erasure QUuid and QString get.
  A QChar is one UTF-16 code unit that owns nothing, and it was the other trivially copyable type
  still modelled as a handle.
  - The API is extensions on `char` (C# 14 extension members, so a call site needs the .NET 10
    compiler): `'A'.IsLetter()`, `'a'.ToUpper()`, `'́'.CombiningClass()`, `'('.MirroredChar()`,
    `'\uD83D'.IsHighSurrogate()`, plus the code-point overloads `char.Category(0x41)`,
    `char.Script(0x41)`, `char.SurrogateToUcs4(high, low)`.
  - Classification and `Category()` use the BCL's Unicode tables (`UnicodeCategory`); the
    properties the BCL has no API for - script, bidi direction, joining type, decomposition,
    combining class, mirroring, case folding, Unicode version - call Qt's tables so they agree
    with Qt. Qt's `digitValue` is pinned to its actual behaviour (decimal digits only; letters
    return -1, unlike its documentation).
  - A `const QChar*` buffer is a UTF-16 `string` (`QCollator.Compare`,
    `QRawFont.GlyphIndexesForChars`). The one constructor that retains such a pointer,
    `QTextBoundaryFinder`, is not published: the string's buffer would not outlive the call.
  - `setCell`/`setRow` are dropped (a `char` is immutable); three of Qt's enums are published at
    namespace level (`CharScript`, `CharDirection`, `CharJoiningType`, `CharDecomposition`,
    `UnicodeVersion`), and `QChar::Category` is the BCL's `UnicodeCategory`.
  - Tests cover classification, case mapping, script/direction/mirroring, surrogate arithmetic,
    the code-point overloads, `QVariant`'s Char type, `QDir.Separator`, a 10,000-conversion leak
    check, and that no `QtSharp.QtCore.QChar` type exists.

- **The rest of the trivially copyable value types are C# structs, not disposable handles.** Each
  of these is a plain value in Qt (a copy owns nothing), so it now crosses the ABI by value and
  needs no `using`: `QModelIndex`, `QColor`, `QSizePolicy`, `QTransform`, `QElapsedTimer`,
  `QDeadlineTimer`, `QOperatingSystemVersion`, `QTextLength`, `QRgba64`, `QPixelFormat`,
  `QPointingDeviceUniqueId`, `QMetaMethod`/`QMetaEnum`/`QMetaClassInfo` - on top of the geometry,
  date and vector types that already were.
  - The struct table describes every field's type; a type whose layout is private (QModelIndex,
    QColor, QSizePolicy, QTransform, ...) converts by bit copy, and every generated translation
    unit asserts `sizeof(POD) == sizeof(T)`.
  - QColor, QTransform and QDeadlineTimer compare through Qt's own `operator==` and a matching
    hash: their equality is not field-wise (a colour compares across specs, a transform ignores
    its dirty bits, a deadline only its deadline). QSizePolicy's equality is its data word.
  - A struct has no inheritance, so a base class's methods are folded in
    (`QOperatingSystemVersion.MajorVersion()` lives on `QOperatingSystemVersionBase`).
  - Value-returning in-place mutators (`QTransform.Translate`/`Scale`/`Rotate`/`Shear` return
    `*this` for chaining) write back through `ref this`; the old handle hid that by mutating the
    native object.
  - Optional struct parameters with a non-plain default keep Qt's declared default
    (`QPixmap.Fill()` fills with `Qt::white`, not an invalid colour).
  - A non-const reference to a struct surfaces as `ref`/`out` and crosses as a pointer to the
    caller's POD; the extractor records `mode: ref`, and the model was re-extracted.
  - `QMatrix4x4` deliberately stays a handle: its non-const `QMatrix4x4*` out parameters
    (`QGraphicsTransform::applyTo`) collide with the struct-pointer-as-array convention and would
    demote a pure virtual and drop a constructor.
  - The `using` statements for these types are gone from the demos and tests, which also assert
    that they are value types (not disposable) and that a 10,000-colour conversion loop registers
    no wrappers.

- **The trivially copyable types the first pass missed are structs too.** A full `std::is_trivially_copyable`
  sweep of every non-`QObject` model class (not the earlier hand-picked list) found nine more:
  `QMetaType`, `QCalendar`, `QMetaProperty`, `QTableWidgetSelectionRange`, `QTextLine`,
  `QTextInlineObject`, `QAudioFormat`, and Qt3D's `QNodeId` and `QKeyFrame`. None of them needs a
  `using` any more, and each crosses the ABI by value.
  - Two Qt3D types are namespaced in C++ (`Qt3DCore::QNodeId`, `Qt3DAnimation::QKeyFrame`), so the
    struct table carries their qualified C++ spelling and only that exact class may claim the row -
    `Qt3DCore::QTransform` is still a class even though QtGui's `QTransform` is a struct.
  - `QCalendar()` is the Gregorian calendar, not the zero value, and `QKeyFrame()` is
    `BezierInterpolation`, not `LinearInterpolation`; both parameterless constructors are published
    so `new QCalendar()` / `new QKeyFrame()` mean what they mean in Qt.
  - `QTextLine` and `QTextInlineObject` are views into a `QTextLayout`: their four padding bytes are
    spelled out in the POD and zeroed on the way in, so equality has no indeterminate bits.
  - `QAudioFormat` ignores its channel config and reserved word in `operator==`, and `QKeyFrame`
    ignores its control points unless the interpolation is Bezier; both compare and hash through Qt.
  - `QMetaObject`, `QJsonParseError` and `QOperatingSystemVersionBase`/`Unexported` stay as they
    are: the first is Qt's static meta-object descriptor returned as a `const QMetaObject*`; the
    second's public fields are now in the model (`offset` included) but it stays a class because
    its optional `QJsonParseError*` is an out parameter that would surface as an `IntPtr`; and the
    last two are the struct's base classes (their fold is a separate decision).
  - Tests cover the value semantics, Qt's non-field-wise equality (an equal linear pair of key
    frames hashes equally), `QCalendar()` vs `default(QCalendar)`, writing `objectName` through
    `QMetaProperty`, and that 10,000 `QKeyFrame` round-trips register no wrappers.

- **A struct's `==`, `Equals` and `GetHashCode` are Qt's.** The generated glue no longer compares
  the managed fields: it calls `operator==` when the type declares one (the compile-time trait in
  `QtSharpApiRuntime.h`), compares the POD bytes when it does not, and hashes with `qHash` when the
  type declares one, otherwise with a generated field-wise `qHashMulti` (or the bytes of an opaque
  value). That makes the comparison right for the types whose equality is not field-wise without a
  hand-written expression - `QColor` across specs, `QMetaMethod`'s data pointer, QTextLength's
  `qFuzzyCompare`, `-0.0` vs `0.0` in QPointF/QLineF/QMarginsF (the hash goes through Qt's
  `qHash(double)`, which normalizes them). The remaining hand-written hashes are the three types
  Qt gives no compatible qHash: QColor (`rgba()`), QAudioFormat (the three fields its `==` looks
  at), QKeyFrame (control points only when Bezier). QTextLength's POD is now its two words: the old
  field list had the enum and the qreal in accessor order, which put Qt's value in the padding the
  P/Invoke marshaller does not preserve. The cost is that `==` and `GetHashCode` cross the native
  boundary once per call; correctness over a per-type C# comparison.

## 0.17.0 - 2026-10-26

- **Every expressible virtual is an override point.** The curated name whitelist is gone: a managed
  subclass can override any virtual whose signature the glue can reproduce and dispatch, including
  `QGraphicsItem::boundingRect`/`paint`/`shape`/`advance`, `QGraphicsEffect::draw`, the `QStyle`
  hooks and Qt3D's `QAbstractAspect` hooks. Private and `final` virtuals, operators, reference
  returns and unbounded out buffers stay callable only - C++ forbids the override or the ABI
  cannot carry it.
- **A `Q_PROPERTY` and its virtual accessor coexist.** When the getter is also a virtual, the
  property keeps the Pascal name (`QXYSeries.Color`, `QWidget.SizeHint`) and the override uses the
  C++ spelling (`color()`, `sizeHint()`); the property reads the overridden value. Previously the
  property disappeared as soon as its accessor became an override point.
- **In-out virtual parameters write back on the wrapper path too.** `QValidator::fixup`/`validate`
  return the rewritten string through the virtual forwarder, not only through the dispatch into a
  managed override.
- **One flags list per declaration** in the model instead of a growing set of booleans: classes
  (`abstract`, `subclassable`, `subclassCtor`, `owned`, `nonCopyable`), methods (`static`,
  `protected`, `const`, `iterator`), parameters (`buffer`, `optional`, plus a `mode` of
  `out`/`ref`) and virtuals (`pure`, `const`, `noBase`, `final`).
- **`QStyleFactory` is bound** (`Keys`/`Create`), so the Widget Gallery lists Qt's own styles.
- **Demo audit:** stale workarounds were removed where the API is now bound - the calendar's
  locale row and `addItem(text, variant)`, the system tray's `setVisible` override and combo item
  data, the gallery's `QCommandLinkButton`, the scribble's `QColorDialog::getColor(color)`, and the
  save-game's field-by-field JSON construction.
- **New official examples:** Flow Layout (a `QLayout` subclass) and Colliding Mice (a
  `QGraphicsItem` subclass with `paint`/`boundingRect`/`shape`/`advance`).
- **Linux x64 (glibc) is supported.** `build_api.sh` and `tools/build-native-module.sh` compile the
  generated C++ and the runtime with g++ (`-std=c++17`, `-Wall -Wextra -Werror`, `-fvisibility=hidden`,
  `-Wl,-z,defs`), and `tools/build-qt-kit.sh` fetches the Qt 6.12.0 `gcc_64` kit from Qt's own
  repository, merges the addons and stages a deployable `lib/` + `plugins/` + `qt.conf` tree with
  `$ORIGIN` rpaths (patchelf), so applications need no `LD_LIBRARY_PATH`. The staged runtime
  includes QtDeclarative (Qt6Qml/Qt6Quick) because QtMultimedia's ffmpeg backend links it, and
  the app still needs the distribution's FFmpeg runtime libraries for playback.
- **The packages carry both RIDs.** Every package with native code holds `tools/native/win-x64/`
  and `tools/native/linux-x64/`; `buildTransitive` selects by `RuntimeIdentifier` (so
  `dotnet publish -r linux-x64` works from any host) and falls back to the SDK's host RID. On
  Linux the base `QtSharp.Qt6` package deploys the whole Qt tree, addons included; the addon
  packages carry the addon's native glue for both RIDs.
- **Platform code lives in platform-specific files.** The generator's host difference is
  `gen/HostPlatform.Windows.cs` / `HostPlatform.Unix.cs` (gen.csproj compiles one) and the
  platform-dependent test helpers are `tests/TestPlatform.Windows.cs` / `.Unix.cs` (the test
  project compiles one); the shared sources contain no `OperatingSystem` checks. The managed
  runtime needs no platform branch at all: Qt finds its plugins through the deployed `qt.conf`
  (`Prefix=./Plugins=plugins`) on Linux and through the application directory on Windows.
- **The C ABI pins `long` to 32 bits.** The model comes from the MSVC headers, so the glue emits
  `int`/`unsigned int` and casts back to Qt's `long` at the call; the C# API stays identical on
  both platforms. `QByteArray::toLong`, `QLocale::toLong` and the input-event timestamps therefore
  truncate to 32 bits on Linux (see README, Known limitations).
- **Windows-only Qt members are not bound.** `QProcess.nativeArguments`/`setNativeArguments` exist
  only under `#if defined(Q_OS_WIN)`, so the generator filters them out of both the C# and the
  native glue; use `ProcessStartInfo.Arguments` instead. This keeps one API surface that compiles
  on both platforms.
- **CI runs Windows and Linux.** The Windows job is unchanged; a new Ubuntu job builds from the
  checked-in model, compiles with g++, and runs the conformance suite under Xvfb with software
  OpenGL. A pack job combines the two payloads and verifies the packages with a consumer on each
  platform. `release.yml` produces the same multi-RID packages.
- **Warnings are errors on both native builds too:** `/WX` for MSVC, `-Werror` for GCC (Qt's
  headers are included with `-isystem`, so only this repository's code has to be warning-free).
  Deprecated Qt declarations are one suppressed class (`-Wno-deprecated-declarations`): the
  glue's job is to call exactly the Qt function the model declares, and 6.12 still declares many
  deprecated APIs. Uninitialized-value diagnostics are the other (`-Wno-uninitialized
  -Wno-maybe-uninitialized`): Qt's `Qt::Uninitialized` constructors intentionally leave the
  storage untouched and the generated wrapper reads the fields to build the POD.
- Windows behaviour is unchanged: the same generated sources, the same MSVC build, the same test
  suite (46 windows, 1379 assertions).

## 0.16.0 - 2026-10-26

- **Abstract Qt classes can be subclassed from C# (issue #9).** A pure virtual is now an override
  point wherever it is declared, not only when its name was on the curated callback list, so
  `QAbstractButton`, `QLayout`, `QValidator` and `QAbstractItemView` publish the hooks their
  subclasses have to implement (`QLayout::addItem/itemAt/takeAt/count`, `QValidator::validate`,
  `QAbstractItemView::moveCursor`). Those classes now get a protected constructor and a native
  subclass that is concrete; a class with a pure virtual the binding cannot express stays
  unconstructible instead of producing an abstract native subclass.
- **`ref`/`out` parameters work on overrides, not just calls.** `QValidator::validate(QString &,
  int &)` can be overridden: the dispatch stub reads the values Qt passed and writes the override's
  results back into Qt's variables after it returns. Calls keep their existing spelling - `ref
  string`/`ref int` for in-out values, `out` for Qt's output pointers.
- **Protected nested types in override signatures are expressible.** `QAbstractItemView::moveCursor`
  takes `CursorAction`, which Qt declares protected. The generated forwarders now convert inside a
  member of the generated subclass - the only scope that may name the type - instead of in an
  exported free function, which MSVC rejected with C2248. Base-call chaining goes through the same
  path.
- **A virtual taking a non-copyable value** (`QStandardItem::read(QDataStream &)`) hands the
  override a transient wrapper over Qt's object rather than a copy that cannot exist (C2280).
- **Issue #8:** a non-core module publishes its free functions in a class named after the module
  (`QtSharp.QtGui.QtGui`), so only QtCore owns `Qt`. `Qt.Alignment` no longer needs an alias when
  QtGui is imported, and the demos and binding tests now compile without one - a second module
  publishing a `Qt` is a compile error again.
- The single-translation-unit native binding is compiled with `/bigobj`; the published virtual and
  conversion surface crossed MSVC's section limit (C1128).
- **`test-subclass`** covers the seam end to end: Qt paints a managed `QAbstractButton`, activates
  and measures a managed `QLayout`, calls a `QListView::moveCursor` override that chains to the
  protected base member, and drives `QLineEdit`'s acceptability through a managed
  `QValidator::validate` with reference parameters. The official **Flow Layout** example (a
  `QLayout` subclass, `qtwidgets-flowlayout`) joins the demo suite - the ported examples
  subclassed only concrete widgets, which is why the abstract-class construction path stayed
  untested.

## 0.15.0 - 2026-10-26

- **`QtSharp.Sql` restores the two Qt model classes that went with the QtSql binding**, implemented
  over ADO.NET instead: `QSqlQueryModel` (a read-only `QAbstractTableModel` over a query) and
  `QSqlTableModel` (staged edits, written by `SubmitAll` in one transaction, `RevertAll` to
  discard). They keep the `QtSharp.QtSql` namespace and class names, so code written against the
  removed binding still compiles, but there is no Qt SQL module behind them: pass any
  `DbConnection` (Microsoft.Data.Sqlite, SqlClient, Npgsql, ...).
- **`test-sqlmodel`** covers the seam rather than SQLite itself: a .NET result set behind a
  `QTableView`, cell reads through `QVariant`, and a staged edit reaching the database.
- Why this one gets a substitute and QtXml/QtWebSockets do not: `QSqlQueryModel` is an
  `QAbstractItemModel`, the interface Qt views consume; `QDomDocument` and `QWebSocket` are leaf
  services whose .NET counterparts can be used directly.

## 0.14.0 - 2026-10-26

- **QtPositioning is bound**, as the separate `QtSharp.Positioning` package: `QGeoCoordinate` and
  the geo value types (`QGeoRectangle`, `QGeoCircle`, `QGeoPath`, `QGeoPolygon`, `QGeoShape`,
  `QGeoAddress`, `QGeoLocation`), `QGeoPositionInfo`, `QGeoSatelliteInfo` and the position/
  satellite sources. `QtSharp.Positioning.Qt6` carries `Qt6Positioning.dll`.
- **`test-positioning`** covers the value math (Berlin-Paris distance and azimuth, altitude,
  rectangle/circle containment, path length), a position info carrying a coordinate, and the
  source enumeration (which tolerates a machine with no positioning plugin).

## 0.13.0 - 2026-10-26

- **QtXml, QtSql and QtWebSockets are gone.** .NET covers the same ground (System.Xml,
  EF Core/SQLite, ClientWebSocket), none of the three leaked types into the remaining bound
  modules, and `QtSharp.Api` is back to nine assemblies. `QXmlStreamReader`/`QXmlStreamWriter`
  stay in QtCore, so Qt-style XML streaming is still available.
- **The seams are tested, not the .NET libraries.** `test-xmlseam` hands documents between
  System.Xml and QXmlStreamReader/Writer; `test-docformats` keeps SVG/PDF and drops the DOM part.
  The QtSql and QtWebSockets demos and their packages went with their modules.
- The Qt6 runtime payload no longer ships Qt6Sql.dll, Qt6Xml.dll or the SQL driver plugins.

## 0.12.0 - 2026-10-26

- **A .NET `Stream` can sit behind Qt's `QIODevice`.** `StreamIODevice` implements the device
  virtuals (`readData`/`writeData`/`seek`/`pos`/`size`/`atEnd`/`isSequential`/`reset`), so
  `QImage.Load`, `QXmlStreamReader`, `QDataStream` and every other `QIODevice*` consumer read from
  or write to managed IO. `test-iodevice` covers both directions plus 1000 create/dispose cycles
  and 500 abandoned instances as a leak check.
- **The generator has a bridge registry.** `gen/Bridges.cs` is the single place where a .NET
  capability wraps itself in a Qt interface: a bridge says which classes it owns, which virtuals
  it publishes and how it maps special parameters (QIODevice's raw byte buffers become `IntPtr`).
  Adding the next bridge is a class plus a row, not another branch in the extractor.

## 0.11.0 - 2026-10-26

- **QJsonArray is a C# list.** It implements `IList<QJsonValue>` (indexer, Add, Insert, RemoveAt,
  Remove, IndexOf, Contains, Clear), and `ToObjectList()` gives the unwrapped snapshot for LINQ.
  Same ownership rule as the object: the wrappers the list hands out are the caller's, the snapshot
  is plain BCL values.

## 0.10.0 - 2026-10-26

- **QJsonObject is a writable C# dictionary.** It implements `IDictionary<string, QJsonValue>`
  (indexer, Add, Remove, ContainsKey, TryGetValue, Clear, the `ICollection` shape), and
  `ToObjectDictionary()` keeps the LINQ-friendly snapshot of unwrapped values. A second
  `IEnumerable<T>` on the same type would make LINQ inference ambiguous, so the unwrapped view is
  an extension method rather than an interface.
- **An insert that returns a C++ iterator now returns the value it points at.** The extractor maps
  `QJsonObject::insert`/`QJsonArray::insert` to `QJsonValue` (the glue calls `insert(...).value()`)
  instead of dropping the return; other iterator-returning members stay unpublished, and a
  non-iterator unsupported return is still a void call.

## 0.9.0 - 2026-10-26

- **QJsonObject is a C# dictionary now.** It implements `IReadOnlyDictionary<string, object>`, so
  `foreach`, `TryGetValue` and LINQ work over a document; values go through `QVariant.ToObject()`
  (nested objects as `Dictionary<string, object>`, arrays as `object[]`), and keys enumerate in
  Qt's sorted order. `test-json` covers it. The interface is read-only: `insert`/`operator[]`
  return an iterator the model does not publish, so `IDictionary` waits for that fix.

## 0.8.0 - 2026-10-26

- **QtCore has official examples in the gate now.** `qtcore-savegame` ports the JSON Save Game
  example's document model (parse, nested reads, a `toJson` round trip) and `qtcore-regexp` ports
  the regexp example's global match/capture walk, so the module no longer relies on `test-core`
  alone for conformance.
- `QJsonObject` is read-only in the binding for now: `insert` and `operator[]` return an iterator,
  which the extractor does not publish, so a document is built from text (or replaced wholesale)
  rather than field by field.

## 0.7.0 - 2026-10-26

- **QtMultimedia is bound**, as the separate `QtSharp.Multimedia` package: playback
  (`QMediaPlayer`, `QAudioOutput`, `QAudioSink`), capture (`QCamera`, `QMediaCaptureSession`,
  `QImageCapture`, `QMediaRecorder`), the audio device/format and media format/metadata classes,
  and the widget half (`QVideoWidget`, `QGraphicsVideoItem`). `QtSharp.Multimedia.Qt6` carries the
  two libraries and the `multimedia` backend plugin.
- **`qtmultimedia-player`** ports the core of Qt's player example without needing a sound card: it
  writes a one-second PCM WAV, loads it through `QMediaPlayer` (ffmpeg backend), and asserts the
  media status, duration, audio track and the video widget's sink.
- Anonymous enums are no longer published: `QGraphicsVideoItem`'s `enum { Type = 14 }` came back
  from clang as `(unnamed enum at ...)`, which is not a C# type name.

## 0.6.0 - 2026-10-26

- **QtSql rows are C# sequences now.** `QSqlQuery.Rows()` yields one `Dictionary<string, object>`
  per row (values unwrapped through `QVariant.ToObject()`), `SelectRows` projects typed rows, and
  `QSqlRecord.Get<T>` does the conversion SQLite's `LongLong` needs. `QSqlQueryModel.Rows()` gives
  the same shape for a model. This is LINQ to Objects over the rows Qt returned: there is no
  `IQueryable` provider translating expressions to SQL and no ADO.NET bridge. `test-qtsql` covers
  the whole path in the gate.

## 0.5.0 - 2026-10-26

- **Invoking an `[QtSignal]` event is now an emit.** `ValueChanged?.Invoke(value)` in the declaring
  class activates the Qt signal, so `connect()`, QML and C++ see it, instead of only running the
  C# handlers. The runtime seeds an emit trampoline into the event's handler field and keeps its
  own connection out of that emission, so every handler still runs exactly once.
  `EmitSignal(ValueChanged, value)` remains as the explicit spelling. The trampoline makes the
  event non-null, so `?.Invoke` emits even with no subscriber, and `== null` no longer means
  "no handlers".
- **QtWebSockets is bound**, as a separate `QtSharp.WebSockets` package against `QtSharp.Api`:
  `QWebSocket`, `QWebSocketServer`, the CORS authenticator, the handshake options and the
  protocol enums. `QtSharp.WebSockets.Qt6` carries `Qt6WebSockets.dll`, the way the Charts and Qt3D
  packages carry their runtimes.
- **`qtwebsockets-echo`** runs Qt's WebSockets "echoserver" example against its own client over
  loopback: connect, text echo, a binary frame and a clean close, all asserted in one process.

## 0.4.0 - 2026-10-26

- **QtSql is bound**, as the eleventh base module of `QtSharp.Api`: `QSqlDatabase`/`QSqlDriver`,
  `QSqlQuery`/`QSqlResult`, the `QSqlRecord`/`QSqlField`/`QSqlIndex`/`QSqlError` value classes and
  the three table models. The Qt runtime payload now ships `Qt6Sql.dll` and the SQL driver plugins
  under `sqldrivers\`, so a consumer can open a SQLite database without deploying Qt itself.
- **`qtsql-querymodel`** ports Qt's SQL "Query Model" example: the `person` table built through
  prepared statements, a `QSqlQueryModel` shown in a `QTableView`, and a `QSqlTableModel` edit
  committed through `submitAll`.
- The generator now emits the container helper types used by a class's *inherited* virtuals:
  `QSqlQueryModel` dispatches `QAbstractItemModel::itemData`/`mimeData`, and those signatures use
  helpers that so far only existed inside QtCore's assembly.

## 0.3.0 - 2026-10-26

- **The binding is generated from Qt 6.12.0.** The checked-in model was re-extracted from the
  6.12.0 `msvc2022_64` kit, so the assemblies, the native glue and the packages now carry 6.12's
  API: `QSortFilterProxyModel::DataChangeRelevanceFlags`, `QStyleHints::toolTipWakeUpDelay`,
  `QImageReader::effectiveSize`, `QTreeView::itemDecorationRect`, `QMetaType::rawId`,
  `QEasingCurve::fromCubicBezier`, `QHostInfo::clearCache`, `QDialogButtonBox`'s
  `standardButtonText`/`standardButtonShortcut`, and the `QScatterSeries` signals that Qt now
  declares for the property move to `QXYSeries`.
- **The seven addon models are extracted by the gate now.** `regen-api.ps1` step `[2b]` extracts
  the Charts and Qt3D models from their own headers and compares them to the checked-in files
  byte for byte, exactly like the combined base model; previously they were checked in but never
  re-extracted, so a Qt upgrade could leave them behind.
- **The header scanners unwrap `QT6_ONLY(...)`/`QT7_ONLY(...)`.** Qt 6.12 wraps declarations that
  differ between Qt 6 and Qt 7 in those macros (`QScatterSeries::colorChanged` is one), and the
  Q_PROPERTY/signal scanners read the raw header text, so the wrapper made them invisible.
- **The model files are pinned to LF** in `.gitattributes`. The model gate compares bytes, and
  `core.autocrlf=true` was checking the checked-in models out as CRLF, which made the gate report
  a model change on every Windows checkout.
## 0.2.0 - 2026-10-24

- **The standalone `QtSharp.Charts` repository was merged in.** Its 38-example conformance suite
  (the official Qt Charts examples, 570 assertions) now lives in the main repository as
  `demos.charts/` and runs in the gate, CI and release against the separate `QtSharp.Charts`
  assembly. Charts' model, native glue and package had already been built here; the second
  repository is now redundant.
- **The demos are now only the official Qt examples, grouped and named by module.** `demos/`
  contains the 28 ported examples under `QtGui/`, `QtWidgets/`, `QtSvg/`, `QtSvgWidgets/`,
  `QtXml/`, `QtPrintSupport/` and `QtNetwork/`, and both the files and the `--list` names say which
  module they exercise (`qtgui-basicdrawing`, `qtwidgets-analogclock`, …).
  The Qt Charts examples are `qtcharts-*`; Qt3D keeps `qt3d-*`.
- **Binding-specific tests moved to `tests/`.** Ownership, the exception boundary, C#-declared
  signals/properties, value types, the QtCore tour, network transports, document formats, managed
  models and the graphics-view tour are still run by the gate, but under `test-*` names instead of
  pretending to be official demos. The redundant API smoke test and the widget tour (covered by the
  official gallery) were removed.
- Generated Q_PROPERTY wrappers now carry `[QtNativeProperty(name, type, writable)]` and generated
  signals (the `QSignal` property and the C# event) carry `[QtNativeSignal(mocSignature)]`. They
  are the read-side markers for members whose Qt metadata already exists, as opposed to the
  declaration-side `[QtProperty]`/`[QtSignal]` a C# subclass uses; `test-metaobject` asserts both.
- **BCL conversions for the Qt value types with a natural C# counterpart**, following PyQt's
  explicit `toPy...`/constructor model rather than replacing Qt types in signatures: QPoint/
  QPointF/QSize/QSizeF/QRect/QRectF <-> System.Drawing, QColor <-> System.Drawing.Color,
  QVector2D/3D/4D <-> System.Numerics.Vector2/3/4, QQuaternion <-> System.Numerics.Quaternion,
  QUrl <-> System.Uri, QUuid <-> System.Guid, QByteArray <-> byte[], plus `QVariant.ToObject()`
  and `QVariant.From(object)` (the QVariant.value() equivalent). The existing QDateTime/QDate/QTime
  conversions are unchanged. A new `test-bclinterop` exercises them in the gate.
- The IDE view is clean now that the module projects link their generated and runtime sources under
  `Generated/` and `Runtime/` virtual folders instead of listing every out-of-project path, and the
  package projects hide their payload DLLs (`Visible=false`, still packed). Generated files that
  would contain no declarations (a module with no namespace-level enums, for example) are not
  written at all.
- The demo/test projects now reference the ten base assemblies instead of compiling the generated
  binding into QtSharp.Demos. The public API is exercised across the same assembly boundaries the
  package ships, and the IDE no longer lists the generated `out/**/*.cs` as files of the demo
  project.
- Added the remaining feasible official example ports as module-named demos:
  `qtsvg-svgviewer`, `qtsvgwidgets-svgwidget`, `qtxml-dombookmarks`,
  `qtprintsupport-notepad` and `qtnetwork-fortune`. OpenGL remains deferred because
  `QOpenGLFunctions` is not bound and CI has no guaranteed GL context.
- The repository builds with `TreatWarningsAsErrors` (`Directory.Build.props`). The generated
  `QPrinter.PdfVersion()` now says `new` where it hides `QPagedPaintDevice.PdfVersion` (the
  generator adds nested enum names to the inherited-member table), and the GitHub Actions were
  upgraded past the Node 20 deprecation line.
- The demo projects are named consistently - `QtSharp.Demos`, `QtSharp.Charts.Demos`,
  `QtSharp.Qt3D.Demos` - and `QtSharp.slnx` groups the generator, runtime packages, base modules,
  addons, demos, tests and samples into solution folders instead of one flat list.
- Demo registration is attribute-based: `[Demo("qtwidgets-analogclock")]` on a static class with
  `Run(QApplication)`, discovered by each catalog. The hand-maintained runner/name tables are
  gone; duplicate or malformed registrations fail at startup.
- `samples/hello` was removed. `testbind` is the one entry point; consumers start from the package
  reference in the README, and the Qt3D/resource samples remain.
- **The whole binding is split the way PyQt6 is.** `QtSharp.Api` now contains ten assemblies, one
  per Qt module and one C# namespace each: `QtSharp.QtCore`, `QtSharp.QtGui`, `QtSharp.QtWidgets`,
  `QtSharp.QtNetwork`, `QtSharp.QtSvg`, `QtSharp.QtSvgWidgets`, `QtSharp.QtPrintSupport`,
  `QtSharp.QtXml`, `QtSharp.QtOpenGL`, `QtSharp.QtOpenGLWidgets`. A type owned by another module is
  emitted fully qualified, so the namespaces cannot collide.
- **New runtime and companion packages**, matching PyQt6's dependency graph:
  `QtSharp.Runtime` (managed handles/signals/marshalling + the shared native `QtSharp.Api.dll`
  glue), `QtSharp.Qt6` (the Qt base runtime DLLs, MSVC runtime and platform plugin),
  `QtSharp.Charts.Qt6` and `QtSharp.Qt3D.Qt6` (each addon's Qt runtime and plugins). Consumers
  reference only `QtSharp.Api`, `QtSharp.Charts` or `QtSharp.Qt3D`; the companions flow as NuGet
  dependencies and copy their payload through `buildTransitive`.
- `qtgen split-core` derives one model per base Qt module from the combined `qtsharp.api.json`.
  The ten split models are checked in and are what the assemblies are generated from; the combined
  model remains the extraction gate and produces only the shared native glue
  (`qtgen codegen --native-only`). The splitter assigns classes by header, enums by the module
  that declares them, and fails on a dependency cycle instead of silently emitting one.
- The runtime is split along the same assembly lines: `runtime/ApiRuntime.cs` (Qt-type-free) builds
  `QtSharp.Runtime`, while the `QObject` signal half, `QtSignals`, the BCL conversions and the
  command-line constructors live in `runtime/QtCore`, `runtime/QtGui` and `runtime/QtWidgets`,
  beside the generated classes they extend. A partial class cannot span assemblies.
- The generator no longer has any addon-specific branch. All module rows (the ten base modules,
  Charts and Qt3D's six) live in `gen/ApiModel.Modules.cs`; the libclang front end moved to
  `ClangExtractor.cs`.
- Demos, samples, CI and release moved to the module namespaces and now pack, upload and verify all
  seven packages; `tools/regen-api.ps1` gates the split models and codegen determinism per module,
  and `tools/pack-qt-binding.ps1` inspects every package's payload.

- Added the `QtSharp.Qt3D` extension package: 230 classes from Qt3DCore, Qt3DRender, Qt3DInput,
  Qt3DLogic, Qt3DAnimation and Qt3DExtras, as **six assemblies with six namespaces**
  (`QtSharp.Qt3DCore` … `QtSharp.Qt3DExtras`) in one package, mirroring Qt's module split and the
  `PyQt6-3D` / `PySide6-Addons` package shape. The payload carries the six native glues, the Qt3D
  runtime DLLs and the renderer/geometry/scene plugins.
- Extension modules can now span several Qt include directories and defines, and can bind classes
  that live in a C++ namespace (Qt3DCore::QEntity). The extractor identifies every class, base and
  signature type by its fully qualified C++ name; the class table carries the C++ spelling for the
  native glue. Namespace-scope declarations of the module's *own* headers are flattened into the
  extension's C# namespace (Qt3DRender::API becomes `API`); core declarations such as
  `Qt::Alignment` keep their namespace because they resolve in the core assembly. Nothing in the
  generator branches on a module's name.
- `tools/build-qt-kit.ps1` describes the addons as a table (marker header, include/lib/bin
  patterns, runtime modules, plugin directories) and merges them generically; adding an extension
  module is a row, not another copy of the merge code.
- Ported Qt's official C++ Qt3D examples as the extension's conformance suite:
  `qt3d-simple` (simple-cpp), `qt3d-basicshapes` (basicshapes-cpp) and `qt3d-text` (3d-text),
  run by `tools/run-window-tests.ps1` exactly like the core demos.
- `QtSharp.Charts` is now packable with the same `buildTransitive` native-payload handling as the
  core and Qt3D packages; CI/release build and upload the addon packages alongside the core.
- Fixed the Q_PROPERTY scanner for a type whose template arguments contain spaces
  (`Q_PROPERTY(QList<QSpriteSheetItem *> sprites …)`), which it read as a property named `>`, and
  for declarations with a leading space after the parenthesis (`Q_PROPERTY( bool hasTopEndcap …)`),
  which it read as properties named `Bool`/`Float`/...
- `tools/build-qt-kit.ps1` now fetches the Qt3D addon along with qtbase/qtsvg/QtCharts, and copies
  its runtime DLLs and plugins.
- The generated C# binding is now split one file per Qt header
  (`out/<module>/generated/<Module>/<header>.cs`), mirroring the C++ include layout instead of one
  9 MB / 194k-line source file. The determinism gate hashes the generated tree. The native C++
  glue stays a single translation unit.
- Generated wrapper types now carry `[GeneratedCode("qtgen", …)]`, and their XML docs spell out
  each Q_PROPERTY's Qt name/type/access and each signal's Qt signature. `[QtProperty]`/`[QtSignal]`
  remain only for members a C# subclass declares, which is where they have runtime meaning. A
  generated type is `partial` only when `runtime/*.cs` extends it (read from those sources, not a
  second list), and `unsafe` is confined to the members that use pointers or function pointers.
- The demo windows now run several at a time: `tools/run-window-tests.ps1` starts them in a pool
  (default `min(6, cores)`, `-Jobs` to change), keeps each window's output attributable, and
  retries the known teardown flake once. The gate, CI and the release job all use it.
- Added `[QtProperty]` and `[QtSignal]` declarations: an attributed C# property or field-like event
  becomes a Qt property or signal. They are the declaration mechanism (the old static
  `QMetaProperty<>`/`QMetaSignal<>` fields are gone). `[QtProperty]` carries the rest of
  `Q_PROPERTY`: `Notify` (inferred as `<Property>Changed` when omitted), `Reset` (inferred as
  `Reset<Property>`), `Constant`, `Final`, `Required`, `Designable`, `Scriptable`, `Stored`,
  `User` and `Revision`.
- Added the `QtSharp.Api` binding package: reference it and write Qt applications in C# with no Qt
  SDK or C++ toolchain installed. It ships the managed binding, the native wrapper and the Qt
  runtime, and copies them to the build and publish output.
- Added `tools/build-qt-kit.ps1`, which fetches and discovers the Qt dev files and runtime DLLs
  from Qt's own repository on a machine with nothing Qt installed.
- CI now builds the binding package from the checked-in model on every push.
- Added a tag-driven GitHub Release workflow for `QtSharp.Tools`, including a SHA-256 checksum.
- Added the `QtSharp.Tools` development package with Qt command-line tools.
- Added MSBuild `QtResource` support for compiling `.qrc` files to `.rcc` and copying resources
  during build and publish.
- Added the resources integration sample.
- Added repository governance files, contribution guidance, and CI scaffolding.

## 0.1.0

- Initial development package metadata for `QtSharp.Tools`.
