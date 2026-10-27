using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using QtSharp.Runtime;

// QVariant is Qt's boxed value. PyQt's QVariant.value() unwraps the primitive Qt values into
// Python objects, so a C# caller gets the same convenience through ToObject()/From().
//
// The object-shaped entry points follow PyQt/PySide too:
//   * a BCL primitive or BCL value type crosses through the matching QVariant constructor
//     (DateTime -> QDateTime and friends keep their conversion semantics; see BclInterop.cs);
//   * a Qt value type that qvariant.h has a constructor for (QPoint, QUrl, QDate, QModelIndex, ...)
//     crosses through that generated constructor, and comes back through the generated ToXxx();
//   * a Qt value type that declares `operator QVariant()` (QColor, QBrush, QIcon, QVector3D, ...)
//     crosses as a real Qt QVariant: through its metatype when it has a handle, through the
//     generated conversion glue when it is a trivially copyable struct;
//   * a QObject crosses as QObject*, and any other managed object is carried as an opaque managed
//     value (the PySide PyObjectWrapper shape).
// Both Qt tables are registered by the generated module initializers in QtVariantConversion; this
// file only keeps the BCL half, which has no Qt counterpart to be generated from.
//
// QVariant is the bridge, not API: every generated signature says object (as in PySide6, where the
// class is not exposed at all), and the class is internal to this assembly. The generated code and
// the other modules reach it only through VariantMarshalling and QtVariantConversion, whose public
// surface is object/IntPtr. Inside the assembly it still carries the metatype inspection and the
// value of a Qt type the runtime cannot map.
namespace QtSharp.QtCore
{
    internal partial class QVariant
    {
        /// <summary>
        /// Wraps any managed value the way PySide's QVariant does:
        /// <list type="bullet">
        /// <item>a primitive, string, byte[], date, Uri, Guid, enum or System.Drawing value uses the
        /// matching real QVariant constructor (small integers and enums widen to the Qt type that
        /// means the same thing);</item>
        /// <item>a Qt value type QVariant has a constructor for (QPoint, QUrl, QDate, ...) becomes a
        /// real Qt QVariant of that type;</item>
        /// <item>a Qt value type with <c>operator QVariant()</c> (QColor, QBrush, QIcon, ...) becomes
        /// a real Qt QVariant of that type;</item>
        /// <item>an array or dictionary becomes a QVariantList / QVariantMap, recursively;</item>
        /// <item>a QObject-derived wrapper becomes a QObject* variant;</item>
        /// <item>anything else is carried as an opaque managed value and survives QVariant copies
        /// until the last one is destroyed.</item>
        /// </list>
        /// A null becomes an invalid QVariant.
        /// </summary>
        public QVariant(object value)
        {
            // Create()/Clone() hand back an owned native QVariant. Take it over rather than copying
            // it again: this temporary wrapper is never handed out.
            QVariant created = value is QVariant source ? source.Clone() : Create(value);
            IntPtr handle = created.Handle;
            ApiRuntime.Forget(handle, created);
            created.__handle = IntPtr.Zero;
            Handle = handle;
            ApiRuntime.Register(Handle, this);
            __ownsNative = true;
            __managedSubclass = true;
        }

        /// <summary>
        /// Wraps a BCL or Qt value as a QVariant. The BCL types (the primitives, string, string[],
        /// byte[], the date types, Uri, Guid and the System.Drawing geometry) and the Qt value types
        /// QVariant has a constructor for are converted; arrays and dictionaries recurse into a
        /// QVariantList/QVariantMap. A managed type with no Qt mapping is carried as an opaque
        /// managed value, as in PySide. A null becomes an invalid QVariant.
        /// </summary>
        public static QVariant From(object value) => Create(value);

        /// <summary>The generic spelling of <see cref="From(object)"/>; T is only a convenience.</summary>
        public static QVariant From<T>(T value) => Create(value);

        static QVariant Create(object value)
        {
            switch (value)
            {
                case null: return new QVariant();
                case bool b: return new QVariant(b);
                case int i: return new QVariant(i);
                case uint ui: return new QVariant(ui);
                case long l: return new QVariant(l);
                case ulong ul: return new QVariant(ul);
                case float f: return new QVariant(f);
                case double d: return new QVariant(d);
                // The small integral types have no QVariant constructor of their own; they widen to
                // the Qt type that means the same thing (PySide's int).
                case sbyte sb: return new QVariant((int)sb);
                case byte by: return new QVariant((int)by);
                case short sh: return new QVariant((int)sh);
                case ushort ush: return new QVariant((int)ush);
                case char c: return FromChar(c);
                case Enum e: return FromEnum(e);
                case string s: return new QVariant(s);
                case string[] strings: return new QVariant(strings);
                case QVariant[] variants: return new QVariant(variants);
                case byte[] bytes:
                    using (var array = new QByteArray(bytes))
                        return new QVariant(array);
                case DateOnly date:
                    return new QVariant(new QDate(date));
                case TimeOnly time:
                    return new QVariant(new QTime(time));
                case DateTime dateTime:
                    using (var qtDateTime = new QDateTime(dateTime))
                        return new QVariant(qtDateTime);
                case DateTimeOffset dateTimeOffset:
                    using (var qtDateTime = new QDateTime(dateTimeOffset))
                        return new QVariant(qtDateTime);
                case Uri uri:
                    using (var url = new QUrl(uri))
                        return new QVariant(url);
                case Guid guid:
                    // QUuid is erased to Guid: the generated constructor takes the value itself.
                    return new QVariant(guid);
                case Point point: return new QVariant(new QPoint(point));
                case PointF pointF: return new QVariant(new QPointF(pointF));
                case Size size: return new QVariant(new QSize(size));
                case SizeF sizeF: return new QVariant(new QSizeF(sizeF));
                case Rectangle rectangle: return new QVariant(new QRect(rectangle));
                case RectangleF rectangleF: return new QVariant(new QRectF(rectangleF));
                // Lists and maps recurse, exactly as ToObject() unwraps them. string[], byte[] and
                // QVariant[] are above: their element types are what Qt expects, so they need no
                // conversion pass. Any other array, and List<T>/IList, becomes a QVariantList.
                case System.Collections.IList items: return FromList(items);
                case IDictionary<string, QVariant> entries: return FromMap(entries);
                case IDictionary<string, object> entries: return FromMap(entries);
                case System.Collections.IDictionary entries: return FromDictionary(entries);
                // A QVariant argument means "copy this variant"; the constructor uses the native
                // copy constructor rather than nesting one QVariant inside another.
                case QVariant variant: return variant.Clone();
                // A bound QObject is a Qt object pointer, not a managed payload; PySide stores it
                // in the QObject* metatype and gives the wrapper back on the way out.
                case IQtObject qtObject:
                    return FromOwnedHandle(VariantNative.FromQObject(qtObject.Handle));
            }

            // A Qt value type qvariant.h has a constructor for (QPoint, QUrl, QDate, QModelIndex,
            // QJsonValue, ...): the generated table calls the bound constructor, so the set is
            // derived from the header instead of a hand-kept list here.
            if (QtVariantConversion.TryCreate(value, out var created))
                return FromOwnedHandle(created);

            // A Qt value type with `operator QVariant()` (QColor, QBrush, QIcon, ...): QMetaType
            // owns the copy, so no per-type constructor is bound (and none is needed).
            if (value is IQtVariantValue qtValue)
                return FromQtValue(qtValue);

            // Everything else is opaque to Qt but still has to live as long as any QVariant copy
            // does. The native side holds a ref-counted id into the managed table.
            return FromManaged(value);
        }

        static QVariant FromChar(char value)
        {
            // QChar is erased to char: the generated QVariant constructor takes the code unit.
            return new QVariant(value);
        }

        // Qt has no QVariant constructor for a C# enum. Its own enums are ints (QFlags is a
        // uint-sized set), so the underlying type is the one Qt expects to see. System.Type and
        // System.Convert are spelled out: QVariant.Type and QVariant.Convert shadow them here.
        static QVariant FromEnum(Enum value)
        {
            switch (System.Type.GetTypeCode(Enum.GetUnderlyingType(value.GetType())))
            {
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.Int32:
                    return new QVariant(System.Convert.ToInt32(value));
                case TypeCode.Byte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                    return new QVariant(System.Convert.ToUInt32(value));
                case TypeCode.UInt64:
                    return new QVariant(System.Convert.ToUInt64(value));
                default:
                    return new QVariant(System.Convert.ToInt64(value));
            }
        }

        static QVariant FromList(System.Collections.IList items)
        {
            // The generated QVariant(object[]) constructor (QList<QVariant>) converts every element.
            var values = new object[items.Count];
            for (int i = 0; i < values.Length; i++)
                values[i] = items[i];
            return new QVariant(values);
        }

        static QVariant FromDictionary(System.Collections.IDictionary entries)
        {
            // The typed dictionaries above miss Dictionary<string, int> and friends; QVariantMap
            // keys are strings, so anything else is a caller error rather than an opaque payload.
            var map = new Dictionary<string, object>(entries.Count, StringComparer.Ordinal);
            foreach (System.Collections.DictionaryEntry entry in entries)
            {
                if (entry.Key is not string key)
                    throw new ArgumentException(
                        "A QVariantMap key must be a string; got "
                        + (entry.Key == null ? "null" : entry.Key.GetType().Name) + ".",
                        nameof(entries));
                map[key] = entry.Value;
            }
            return FromMap(map);
        }

        static QVariant FromMap(IDictionary<string, object> entries)
        {
            // PySide's dict -> QVariantMap. The generated QVariant constructor only binds the QHash
            // overload of the same shape, so the map is built through the generated QMap helper.
            var map = entries as Dictionary<string, object>
                      ?? new Dictionary<string, object>(entries, StringComparer.Ordinal);
            IntPtr nativeMap = QsApiMap_QString_QVariant.New(map);
            try
            {
                return FromOwnedHandle(VariantNative.FromVariantMap(nativeMap));
            }
            finally
            {
                QsApiMap_QString_QVariant.Free(nativeMap);
            }
        }

        static QVariant FromMap(IDictionary<string, QVariant> entries)
        {
            // A caller that already has variants: hand the same values through as the managed values
            // they stand for, so the map builder does not have to special-case them.
            var values = new Dictionary<string, object>(entries.Count, StringComparer.Ordinal);
            foreach (var entry in entries)
                values[entry.Key] = entry.Value;
            return FromMap(values);
        }

        static QVariant FromQtValue(IQtVariantValue value)
        {
            // The module initializer resolved the metatype id when the assembly loaded, so the hot
            // path is a dictionary lookup instead of QMetaType::type("QColor") on every value.
            if (QtVariantConversion.TryGet(value.GetType(), out var entry) && entry.MetaTypeId != 0)
                return FromOwnedHandle(VariantNative.FromMetaTypeId(entry.MetaTypeId, value.Handle));

            // Qt has no metatype for it (QBitmap is the one value type whose conversion operator
            // has no registered type). It is "unknown to QVariant" then, so it takes the opaque
            // managed path rather than failing.
            int typeId = QMetaType.Type(value.QtTypeName);
            if (typeId == 0)
                return FromManaged(value);
            return FromOwnedHandle(VariantNative.FromMetaTypeId(typeId, value.Handle));
        }

        static QVariant FromManaged(object value)
            => FromOwnedHandle(VariantNative.FromManaged(ManagedVariantStore.Add(value)));

        QVariant Clone() => FromOwnedHandle(VariantNative.Clone(Handle));

        // The bridge's handle-based entry point: give up the native QVariant so the caller (a
        // VariantLease) owns it. The wrapper must leave the identity registry before zeroing its
        // handle: the address can be reused by the next QVariant, and a live wrapper parked at 0
        // would be handed out for that new object (a zero-handle wrong-type wrapper). The same
        // Forget/invalidate pair the generated Dispose uses is what keeps Wrap() honest.
        internal IntPtr DetachHandle()
        {
            IntPtr handle = Handle;
            ApiRuntime.Forget(handle, this);
            __handle = IntPtr.Zero;
            __invalidated = true;
            __ownsNative = false;
            GC.SuppressFinalize(this);
            return handle;
        }

        /// <summary>
        /// The contained value as the closest managed type, the way PyQt's QVariant.value()
        /// unwraps: int/uint/long/ulong/float/double/bool, string, string[], byte[], char,
        /// DateOnly, TimeOnly, DateTime, Uri, Guid, the System.Drawing geometry, the Qt value types
        /// registered by QtVariantConversion (QColor, QBrush, QPoint, QUrl, QJsonValue, ...), a
        /// QObject wrapper, or the original managed object. Lists and maps are unwrapped
        /// recursively (object[] / Dictionary&lt;string, object&gt;). A value with no managed
        /// equivalent is returned as this wrapper: an external caller sees an opaque object that
        /// round-trips through variant-taking APIs and can be disposed, but whose members are
        /// internal to this assembly.
        /// </summary>
        public object ToObject()
        {
            // The opaque managed payload is checked first: its metatype is allocated by this
            // binding, and the id is the only way back to the object.
            long managedId = VariantNative.ManagedId(Handle);
            if (managedId >= 0)
                return ManagedVariantStore.TryGet(managedId, out var managed) ? managed : null;

            // QObject* holds a non-owning pointer; hand back the wrapper the program already has
            // (a QWidget, not a plain QObject), or a QObject view if this is the first sight of it.
            IntPtr qobject = VariantNative.ToQObject(Handle);
            if (qobject != IntPtr.Zero)
                return ApiRuntime.TryGetWrapper(qobject) ?? QObject.FromHandle(qobject);

            switch ((QVariant.Type)type())
            {
                case QVariant.Type.Invalid: return null;
                case QVariant.Type.Bool: return ToBool();
                case QVariant.Type.Int: return ToInt();
                case QVariant.Type.UInt: return ToUInt();
                case QVariant.Type.LongLong: return ToLongLong();
                case QVariant.Type.ULongLong: return ToULongLong();
                case QVariant.Type.Double: return ToDouble();
                case QVariant.Type.Char:
                    return ToChar();
                case QVariant.Type.String: return ToString();
                case QVariant.Type.StringList: return ToStringList();
                case QVariant.Type.ByteArray:
                    using (var bytes = ToByteArray()) return bytes.ToArray();
                case QVariant.Type.Date: return ToDate().ToDateOnly();
                case QVariant.Type.Time: return ToTime().ToTimeOnly();
                case QVariant.Type.DateTime:
                    using (var dateTime = ToDateTime()) return dateTime.ToDateTime();
                case QVariant.Type.Url:
                    using (var url = ToUrl()) return url.ToUri();
                case QVariant.Type.Uuid:
                    return ToUuid();
                case QVariant.Type.Point: return ToPoint().ToPoint();
                case QVariant.Type.PointF: return ToPointF().ToPointF();
                case QVariant.Type.Size: return ToSize().ToSize();
                case QVariant.Type.SizeF: return ToSizeF().ToSizeF();
                case QVariant.Type.Rect: return ToRect().ToRectangle();
                case QVariant.Type.RectF: return ToRectF().ToRectangleF();
                // The generated helpers already turn every element into the managed value it
                // stands for (a QVariantList is object[], a QVariantMap a Dictionary).
                case QVariant.Type.List:
                    return ToList();
                case QVariant.Type.Map:
                case QVariant.Type.Hash:
                    return type() == (int)QVariant.Type.Map ? ToMap() : ToHash();
                default:
                    int typeId = TypeId();
                    // A class Qt converts with `operator QVariant()` (QColor, QBrush, QIcon...).
                    // QMetaType::create gives the wrapper an owned copy, exactly what
                    // FromOwnedHandle expects.
                    if (QtVariantConversion.TryGet(typeId, out var conversion))
                    {
                        IntPtr copy = QMetaType.Create(typeId, ConstData());
                        if (copy != IntPtr.Zero)
                            return conversion.FromOwnedHandle(copy);
                        return this;
                    }
                    // A value type QVariant has its own accessor for (QPoint, QUrl, QModelIndex,
                    // QJsonValue...): the generated table calls it.
                    if (QtVariantConversion.TryUnwrap(typeId, Handle, out var unwrapped))
                        return unwrapped;
                    return this;
            }
        }

        /// <summary>
        /// The contained value as T: the generic spelling of <see cref="ToObject"/> for callers
        /// that know what they put in, or what Qt is documented to put there. Asking for the Qt
        /// type itself works even where ToObject() prefers the BCL equivalent (QUrl, QDate).
        /// </summary>
        public T To<T>()
        {
            object value = ToObject();
            if (value is T typed)
                return typed;
            if (value == null)
                return default;
            if (QtVariantConversion.TryUnwrap(TypeId(), Handle, out var qtValue) && qtValue is T qtTyped)
                return qtTyped;
            throw new InvalidCastException(
                $"The QVariant holds {TypeName()}, not a {typeof(T).Name}.");
        }
    }
}
