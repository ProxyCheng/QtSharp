# Model vocabulary

The binding is generated from the checked-in `model/*.json`, and a declaration only gets into a
model when the types it exposes have a spelling the model can express; an unsupported type drops the
declaration, or just the parameter when it is a defaulted trailing one. That set of spellings is the
model vocabulary. It is deliberately small: a type outside it is dropped during extraction rather
than guessed at, which is what keeps the model bounded and the output reproducible.

Three files own it:

| file | what it decides |
|---|---|
| `gen/ClangExtractor.cs` (`ModelName`, `SupportedType`, `Accept`) | whether a clang type has a model spelling, and so whether the declaration is kept at all |
| `gen/ApiModel.cs` (`ApiTypes.Managed`, `BuildVocabulary`/`SupportedTypes`, `ApiTypes.RuntimeStructs`) | what a model spelling means in C#, which spellings are supported, and which Qt classes are value structs |
| `gen/ApiCodeGen.cs` (`Classify`/`ManagedType`, the container helper writers) | the emitted managed signature and the marshalling behind it |

## What the vocabulary covers today

| model spelling | managed spelling | how it crosses |
|---|---|---|
| `bool`, `int`, `float`, `double`, the integer typedefs (`short`, `unsigned short`, `long`, `unsigned long`, `long long`, `unsigned long long`, `char`, `unsigned char`) | the matching C# primitive (`long long` is `long`, `unsigned char` is `byte`, `char` is `sbyte`) | by value; `long`/`unsigned long` are pinned to 32 bits, the MSVC width the model is extracted with |
| `QString`, `QStringView`, `QAnyStringView`, `QLatin1StringView`, a const `char*` | `string` | UTF-16 across the ABI |
| `QStringList`, and any `QList<QString>` | `string[]` | `QStringList` keeps its own name in the model; the runtime builds a native `QStringList` per call (`runtime/ApiRuntime.cs`, `native/QtSharpApiRuntime.cpp`) |
| `QList<E>` | `E[]` | generated per-element helpers (`qs_api_list_<stem>_*`) |
| `QMap<K, V>`, `QHash<K, V>` | `Dictionary<K, V>` | generated per-key/value helpers |
| `QUuid` | `Guid` | erased; the Qt-only operations are extensions on `Guid` (`runtime/QtCore/Uuid.cs`) |
| `QChar` | `char`; a `QChar*` buffer is `string` | erased; the Qt-only operations are extensions on `char` (`runtime/QtCore/Char.cs`) |
| `QByteArrayView`, `uchar*` | `byte[]` | a buffer with its length (passed separately for `uchar*`); `QByteArray` itself is a bound class, not `byte[]` |
| `QVariant` | `object` | the value is unwrapped to its BCL shape, and boxed back on the way in |
| `T*` where T is a bound class | the wrapper class, or `IntPtr` when the pointee is deliberately opaque | a handle; ownership follows the wrapper rule described with the smart pointers |
| `T*` where T is a scalar or a runtime struct | `T[]` | an input array, or an out buffer, when Qt passes the length separately |
| a runtime struct (`ApiTypes.RuntimeStructs`: `QPoint`, `QRect`, `QColor`, `QDate`, `QModelIndex`, …) | a C# struct | by value; equality and hashing stay Qt's |
| an enum or a `QFlags` | a C# enum or flags enum | as its integer |
| a `std::chrono` duration | `TimeSpan` | nanoseconds on the ABI, converted at the call |
| `void*` | `IntPtr` | a raw handle with nothing to dereference |

Containers are converted element by element. Each distinct element type that actually occurs in the
model produces its own set of native entry points — `qs_api_list_<stem>_new`, `_append`, `_count`,
`_at`, `_free`, and the map/hash equivalents that walk the keys and values by index. There is no
generic container marshaller. That is what "element glue" means here: a container spelling is a
mapping plus a helper set per element type in use, plus the tests that exercise them. The model
records only the element types that occur, so the helper set stays bounded by the model.

Two containers are deliberately absent even though they are adjacent to the modelled ones:
`QMultiMap`/`QMultiHash` are different classes and stay unbound, and a container of containers
(`QHash<int, QHash<T, QVariant>>`) is refused because the element codec handles scalars, strings,
enums, boxed values and class pointers, not another container.

## The gap is invisible in the model and in the generated API

When `ModelName` returns null, `SupportedType` rejects the declaration and `Accept` drops it, adding
its type spelling to the `Skipped2` counter. The model never records the declaration, so the
checked-in JSON contains zero occurrences of the spelling and the generated API simply has no such
member. Nothing in either artifact says a method is missing, and a consumer cannot tell "Qt does not
have this" from "the binding does not express this".

The gap is visible only on an extraction run: `ReportCoverage2` prints the twenty most frequent
`blocked by <type> xN` tallies, and `QTSHARP_MODEL_DEBUG=1` prints each skipped declaration with the
type that blocked it. Those logs are not checked in.

Two current examples bear this out, both verified against the Qt 6.12 headers the model is extracted
from:

* `QOpenGLContext` is a root class, and its `extensions()` returns `QSet<QByteArray>`. The method is
  absent from `model/qtsharp.qtgui.json` and therefore from `QtSharp.QtGui`.
* `QXYSeries` (Qt Charts) declares `bestFitLineEquation(bool &ok)` returning `QPair<qreal, qreal>`.
  It is absent from `model/qtsharp.charts.json`.

## The rule for adding a spelling

A new spelling is added only when a concrete API that the binding should expose needs it: a root
class's method in a header the model parses, or a call a port in `demos/` (or a downstream consumer)
actually makes. It is never added because the type exists in Qt, because another binding exposes it,
or in anticipation of a module that might need it later.

That means the whole entry, not just the type-name branch:

1. **Find the dropped declaration.** Run extraction (`qtgen extract2`, or `tools/regen-api.ps1`) and
   read the `blocked by` summary, or set `QTSHARP_MODEL_DEBUG=1` for the per-declaration line. Know
   which class and method the spelling costs before adding anything.
2. **Add the model spelling** in `ModelName`, and allow it in `SupportedType` in the positions that
   make sense (parameter, return, both). Whether a type may be returned is a separate question from
   whether it may be passed.
3. **Add the managed spelling and marshalling** in `Classify`/`ManagedType`, plus the native glue
   when the ABI needs one. A container or pointer-shaped type needs element glue; a scalar does not.
4. **Add a test** that exercises the API and checks the value in both directions where both apply.
   The existing container tests are the model to copy: `QList<int>` in `tests/CoreApi.cs`,
   `QList<QGraphicsItem*>` in `tests/GraphicsApi.cs`, `QList<QTableWidgetSelectionRange>` in
   `tests/ValueTypesApi.cs`, and a `string[]` return in `tests/CustomModelApi.cs`.
5. **Regenerate the checked-in models.** The gate (`tools/regen-api.ps1`) re-extracts and checks the
   split models are byte-identical, so a vocabulary change without regenerated models fails there.

If all five cannot be done, leave the declaration dropped. A spelling half-way into the vocabulary is
worse than an absent method: extraction succeeds, and the failure moves to code generation or to the
first call at runtime, where it is harder to attribute.

## The spellings this policy leaves out

None of these is in the vocabulary today; for `QStringList`, the type is, but none of its methods
are. The sections record what the target would be and what the entry would cost, so the next
concrete need does not have to re-derive it.

### `QSet<T>` to `HashSet<T>`

`ModelName` has branches for `QStringList`, `QList`, `QMap` and `QHash`; a `QSet<...>` specialization
falls through to the class-declaration branch and is rejected there because its spelling contains
`<`. (`IsContainerSpelling` mentions `QSet<` only so the nested-container check recognises the
spelling; it does not map the type.)

Target: `HashSet<T>`. It needs the same element-by-element native glue `QList` has, with one
difference: a `QSet` is unordered and has no index, so the read side cannot be `QList`'s `at(i)`
loop. Only the element types a real API uses need glue; the first concrete need,
`QOpenGLContext::extensions()`, names `QByteArray`. A second element type is added when a second API
needs it.

### `QPair<A, B>` and `std::pair`

Qt 6 defines `QPair<T1, T2>` as an alias of `std::pair<T1, T2>` (`QtCore/qcontainerfwd.h`), so the
two spellings are one vocabulary entry. Neither is mapped today.

Target: a small record or `ValueTuple`. Both halves need element glue, the way a `QMap` entry does:
the native side has to hand each half across the ABI, and the managed side has to build one back.
The concrete need is `QXYSeries::bestFitLineEquation(bool &ok)` returning `QPair<qreal, qreal>` in
Qt Charts; `std::pair<iterator, iterator>` from `QMap::equal_range` is not a candidate, because the
iterators themselves are outside the vocabulary.

### `std::optional<T>`

There is no `std::optional` branch in `ModelName`. It is also not the same thing as the model's
per-parameter `optional` flag, which records a C++ default argument.

The trailing, defaulted form is already handled by parameter dropping: a defaulted unsupported
parameter ends the visible parameter list and the method is kept with the parameters before it. The
permission parameters are one case. In Qt 6.12 `QDir::mkdir` and `QDir::mkpath` are declared only
with `std::optional<Permissions> = std::nullopt` (their non-optional overloads are inside
`QT_CORE_REMOVED_SINCE(6, 10)`), and `QFile::copy` is declared the same way (removed-since 6.12); the
model carries `mkdir(dirName)`, `mkpath(dirPath)` and `copy(newName)` because the optional parameter
is dropped.

So `std::optional<T>` is a gap when the value is required or is the return type: then the whole
declaration is dropped. The current cases in bound types are:

* QtMultimedia: `QScreenCapture` and `QWindowCapture` declare
  `setMaximumFrameRate(std::optional<qreal>)` and `maximumFrameRate()` returning
  `std::optional<qreal>`. Neither method is in the model, and the `maximumFrameRate` property is
  absent with them; only the parameterless `maximumFrameRateChanged` signal survives.
* QtCore: `QMetaEnum::value64`, `keyToValue64` and `keysToValue64` return `std::optional<quint64>`.
* QtWidgets: `QFontComboBox::displayFont` returns `std::optional<QFont>`.

The policy is to surface such a value as `T?`/a nullable only where no non-optional spelling of the
same call is already modelled — which is the case for all of these. Where a defaulted optional
parameter is dropped and the method survives, nothing should be added: a nullable C# parameter would
only restate the default Qt is going to apply, and the method already binds without it.

### `QSharedPointer<T>`, `QPointer<T>`, `QScopedPointer<T>`

All three are class templates, so they are dropped the same way `QSet` is. Ownership has to be
decided before any of them gets a managed spelling, because the current rule has no room for a
reference-counted pointer: a wrapper that C# created deletes its object unless Qt took it over (a
parent, a layout, a scene, a view), the generated destructor asks `QObject::parent()` first, and the
`Owned` flag on a root is false for the classes the binding must never delete (events, graphics items,
objects Qt owns). None of that answers who owns such a pointer in a parameter or a return, whether a
managed wrapper may release the last reference, how a `QPointer` that goes null when its object dies
relates to the wrapper's liveness check, or how a `QScopedPointer` transfer would work. Until those
answers exist, a spelling would be a leak or a double free waiting to happen.

Upstream gives no ready-made rule either: PyQt6 does not bind `QSharedPointer`, and PySide6 binds
only part of the pointer family.

The concrete candidates are in Qt3D. `QAspectEngine::setRootEntity(QEntityPtr)` and `rootEntity()`
use the public typedef `QEntityPtr = QSharedPointer<QEntity>`, and `QTextureData::imageData()`
returns `QList<QTextureImageDataPtr>`; the typedefs resolve to the smart-pointer specialization, so
those declarations and that method are dropped. No port calls them today — the Qt3D demos go through
`Qt3DWindow::setRootEntity`, which takes a raw `QEntity *` — so the ownership question can wait for a
port that actually needs one. Add a spelling only together with the ownership rule and a test.

### `QStringList` methods (`join`, `filter`, `replaceInStrings`, …)

`QStringList` is erased to `string[]` by `ApiTypes.Managed`, and the binding has no `QStringList`
class: the model uses the spelling in signatures, but does not publish a class for it. Its methods
therefore do not exist. A returned list is copied into a fresh array and the native list is freed, so
changing that array does not change anything on the Qt side.

The policy is a static extension class over `string[]`, added one method at a time, only when a port
needs that method. `string.Join` covers `join`, and LINQ covers the common `filter`/`replaceInStrings`
uses. `runtime/QtCore/Char.cs` and `runtime/QtCore/Uuid.cs` are the precedent for Qt-only extensions
on BCL types. There is no such extension class in `runtime/` today.

## `QBindable` is not a vocabulary target

The generator used to carry a return-only `QBindable<T>` handle for the `bindableFoo()` accessors.
It was removed from the public API (issue #25): it exposed only `Value` (get/set) and `Dispose`,
while the parts that make a bindable distinct (`setBinding`, `onValueChanged`, `hasBinding`, …) were
not bound.

The supported way to read and write a property is the normal accessor (and the generated property,
where there is one), with the property's NOTIFY signal for change notification where it declares
one. Do not add vocabulary on top of the `QBindable` handle.
