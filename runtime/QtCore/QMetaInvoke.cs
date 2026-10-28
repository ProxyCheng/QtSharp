using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using QtSharp.Runtime;

// Dynamic invocation: calling a method Qt knows only by its meta-object. QMetaMethod::invoke and
// QMetaObject::invokeMethod take QGenericArgument values, which are C++ objects managed code
// cannot build; the native shim (qs_api_qmeta_method_invoke / qs_api_qmeta_object_invoke) does
// that from a compact (type name, value pointer) ABI and answers with a status code.
//
// The shapes match PyQt's QMetaMethod.invoke / QMetaObject.invokeMethod: the arguments are a
// params array of managed values, and the return value comes back through the same QVariant
// bridge every other call site uses. A failure is `false`, with an optional reason; nothing on
// this path throws except a null target or member, which is a programming error rather than a
// dynamic-call outcome.
namespace QtSharp.QtCore
{
    public partial struct QMetaMethod
    {
        /// <summary>
        /// Invokes this method on <paramref name="target"/> with Qt's automatic connection type.
        /// </summary>
        /// <param name="target">The object the method runs on. Must not be null.</param>
        /// <param name="args">
        /// The arguments, one per declared parameter. Supported values: the C# primitives
        /// (<see cref="int"/>, <see cref="bool"/>, <see cref="double"/>, ...), <see cref="string"/>,
        /// <see cref="byte"/>[], enums, bound Qt types (a QObject-derived wrapper crosses as its
        /// pointer), and any value Qt's QVariant can hold (it is converted to the parameter's
        /// declared type). null is the invalid QVariant, and a null pointer for a pointer
        /// parameter. At most ten arguments, the limit of QMetaMethod::invoke.
        /// </param>
        /// <param name="result">
        /// The method's return value converted through <see cref="VariantMarshalling.ToManaged"/>;
        /// null when the method returns void, when the call failed, or when a queued connection
        /// could not carry it.
        /// </param>
        /// <returns>
        /// true when Qt invoked the method. false - never an exception - when the method is not
        /// valid, the target does not have it, the arguments do not match its parameters, the
        /// argument count exceeds ten, or Qt refused the call (for example a queued connection
        /// asked for a return value, which Qt does not support).
        /// </returns>
        public bool Invoke(QObject target, object[] args, out object result)
            => Invoke(target, Qt.ConnectionType.AutoConnection, args, out result);

        /// <summary>
        /// Invokes this method on <paramref name="target"/> with the given connection type.
        /// </summary>
        /// <param name="target">The object the method runs on. Must not be null.</param>
        /// <param name="connectionType">
        /// Qt's connection type. A queued connection cannot carry a return value: Qt refuses it
        /// (the call returns false and <paramref name="result"/> is null), so ask for the result
        /// only with a direct connection.
        /// </param>
        /// <param name="args">The arguments; see the three-argument overload for the supported set.</param>
        /// <param name="result">The return value, or null.</param>
        /// <returns>true when Qt invoked the method; false otherwise.</returns>
        public bool Invoke(QObject target, Qt.ConnectionType connectionType, object[] args, out object result)
            => Invoke(target, connectionType, args, out result, out _);

        /// <summary>
        /// Invokes this method and also reports why the call failed.
        /// </summary>
        /// <param name="target">The object the method runs on. Must not be null.</param>
        /// <param name="args">The arguments; see the three-argument overload for the supported set.</param>
        /// <param name="result">The return value, or null.</param>
        /// <param name="error">A human-readable reason when the call fails; null on success.</param>
        /// <returns>true when Qt invoked the method; false otherwise.</returns>
        public bool Invoke(QObject target, object[] args, out object result, out string error)
            => Invoke(target, Qt.ConnectionType.AutoConnection, args, out result, out error);

        /// <summary>
        /// Invokes this method and also reports why the call failed.
        /// </summary>
        /// <param name="target">The object the method runs on. Must not be null.</param>
        /// <param name="connectionType">Qt's connection type; a queued connection cannot return a value.</param>
        /// <param name="args">The arguments; see the three-argument overload for the supported set.</param>
        /// <param name="result">The return value, or null.</param>
        /// <param name="error">A human-readable reason when the call fails; null on success.</param>
        /// <returns>true when Qt invoked the method; false otherwise.</returns>
        public bool Invoke(QObject target, Qt.ConnectionType connectionType, object[] args,
                           out object result, out string error)
        {
            if (!IsValid())
            {
                result = null;
                error = "the QMetaMethod is not valid (a default QMetaMethod or one from a deleted meta-object)";
                return false;
            }
            QMetaObject meta = EnclosingMetaObject();
            if (meta == null)
            {
                result = null;
                error = "the QMetaMethod has no enclosing meta-object";
                return false;
            }
            return MetaInvocation.Invoke(meta.Handle, MethodIndex(), null, target, connectionType,
                                         args, true, out result, out error);
        }

        /// <summary>
        /// Invokes this method and discards its return value. A void method and one whose result
        /// is not wanted are both just "call it"; a queued connection can be used here even for a
        /// method that returns a value, because no result storage is handed to Qt.
        /// </summary>
        /// <param name="target">The object the method runs on. Must not be null.</param>
        /// <param name="args">The arguments, one per declared parameter.</param>
        /// <returns>true when Qt invoked the method; false otherwise.</returns>
        public bool Invoke(QObject target, params object[] args)
            => Invoke(target, Qt.ConnectionType.AutoConnection, args);

        /// <summary>
        /// Invokes this method with the given connection type and discards its return value.
        /// </summary>
        /// <param name="target">The object the method runs on. Must not be null.</param>
        /// <param name="connectionType">Qt's connection type.</param>
        /// <param name="args">The arguments, one per declared parameter.</param>
        /// <returns>true when Qt invoked the method; false otherwise.</returns>
        public bool Invoke(QObject target, Qt.ConnectionType connectionType, params object[] args)
        {
            QMetaObject meta = IsValid() ? EnclosingMetaObject() : null;
            if (meta == null)
                return false;
            // No result storage: a queued connection is then legal even for a returning method.
            return MetaInvocation.Invoke(meta.Handle, MethodIndex(), null, target, connectionType,
                                         args, false, out _, out _);
        }
    }

    public partial class QMetaObject
    {
        /// <summary>
        /// Invokes the member of <paramref name="target"/> with Qt's automatic connection type.
        /// This is the by-name form of <see cref="QMetaMethod.Invoke(QObject, object[], out object)"/>:
        /// the member is resolved against the target's meta-object at call time.
        /// </summary>
        /// <param name="target">The object the member runs on. Must not be null.</param>
        /// <param name="member">
        /// The method name ("setValue"), or its normalized signature ("setValue(int)") to pick one
        /// overload. The member must be in the meta-object: a slot, a signal, or a Q_INVOKABLE.
        /// A plain C++ method that is not declared to moc is not there.
        /// </param>
        /// <param name="type">Qt's connection type; a queued connection cannot return a value.</param>
        /// <param name="args">
        /// The arguments, one per declared parameter. Supported values: the C# primitives,
        /// <see cref="string"/>, <see cref="byte"/>[], enums, bound Qt types (a QObject-derived
        /// wrapper crosses as its pointer), and any value Qt's QVariant can hold (converted to the
        /// parameter's declared type). null is the invalid QVariant, and a null pointer for a
        /// pointer parameter. At most ten arguments.
        /// </param>
        /// <param name="result">The return value, or null for a void method or a failed call.</param>
        /// <returns>
        /// true when Qt invoked the member. false - never an exception - when no member of that
        /// name and argument count exists, the argument types do not match, there are more than
        /// ten arguments, or Qt refused the call (a queued connection asked for a return value,
        /// for example).
        /// </returns>
        public static bool InvokeMethod(QObject target, string member, Qt.ConnectionType type,
                                        object[] args, out object result)
            => InvokeMethod(target, member, type, args, out result, out _);

        /// <summary>
        /// Invokes the member of <paramref name="target"/> and also reports why the call failed.
        /// </summary>
        /// <param name="target">The object the member runs on. Must not be null.</param>
        /// <param name="member">The method name or its normalized signature.</param>
        /// <param name="type">Qt's connection type; a queued connection cannot return a value.</param>
        /// <param name="args">The arguments; see the overload without <paramref name="error"/>.</param>
        /// <param name="result">The return value, or null.</param>
        /// <param name="error">A human-readable reason when the call fails; null on success.</param>
        /// <returns>true when Qt invoked the member; false otherwise.</returns>
        public static bool InvokeMethod(QObject target, string member, Qt.ConnectionType type,
                                        object[] args, out object result, out string error)
        {
            if (member == null)
                throw new ArgumentNullException(nameof(member));
            return MetaInvocation.Invoke(IntPtr.Zero, -1, member, target, type, args, true,
                                         out result, out error);
        }

        /// <summary>
        /// Invokes the member of <paramref name="target"/> with Qt's automatic connection type and
        /// discards its return value.
        /// </summary>
        /// <param name="target">The object the member runs on. Must not be null.</param>
        /// <param name="member">The method name or its normalized signature.</param>
        /// <param name="args">The arguments, one per declared parameter.</param>
        /// <returns>true when Qt invoked the member; false otherwise.</returns>
        public static bool InvokeMethod(QObject target, string member, params object[] args)
            => InvokeMethod(target, member, Qt.ConnectionType.AutoConnection, args);

        /// <summary>
        /// Invokes the member of <paramref name="target"/> and discards its return value. A queued
        /// connection is allowed here even for a returning method: no result storage is handed to
        /// Qt, so the queued call simply drops the result.
        /// </summary>
        /// <param name="target">The object the member runs on. Must not be null.</param>
        /// <param name="member">The method name or its normalized signature.</param>
        /// <param name="type">Qt's connection type.</param>
        /// <param name="args">The arguments, one per declared parameter.</param>
        /// <returns>true when Qt invoked the member; false otherwise.</returns>
        public static bool InvokeMethod(QObject target, string member, Qt.ConnectionType type,
                                        params object[] args)
        {
            if (member == null)
                throw new ArgumentNullException(nameof(member));
            return MetaInvocation.Invoke(IntPtr.Zero, -1, member, target, type, args, false,
                                         out _, out _);
        }
    }

    /// <summary>
    /// The managed side of the dynamic-invocation shim: argument storage, the status mapping and
    /// the P/Invokes. Internal: the public surface is the partial QMetaMethod/QMetaObject members.
    /// </summary>
    internal static class MetaInvocation
    {
        // The native QsApiInvokeArgument: a type name and the address of a value in that type's
        // native representation. The array marshals as a block of these structs.
        [StructLayout(LayoutKind.Sequential)]
        struct InvokeArgument
        {
            [MarshalAs(UnmanagedType.LPStr)] public string TypeName;
            public IntPtr Value;
        }

        // The status codes QsApiInvokeStatus spells in native/QtSharpApiRuntime.cpp.
        const int Ok = 0;
        const int Invalid = 1;
        const int NotFound = 2;
        const int TooManyArguments = 3;
        const int Failed = 4;
        const int ReturnUnavailable = 5;

        // QMetaMethod::invoke's own limit (Q_METAMETHOD_INVOKE_MAX_ARGS).
        const int MaxArguments = 10;

        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = "qs_api_qmeta_method_invoke", CallingConvention = CallingConvention.Cdecl)]
        static extern int QMetaMethodInvoke(IntPtr metaObject, int methodIndex, IntPtr target,
            int connectionType, [In] InvokeArgument[] arguments, int argumentCount, IntPtr result);

        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = "qs_api_qmeta_object_invoke", CallingConvention = CallingConvention.Cdecl)]
        static extern int QMetaObjectInvoke(IntPtr target, [MarshalAs(UnmanagedType.LPStr)] string member,
            int connectionType, [In] InvokeArgument[] arguments, int argumentCount, IntPtr result);

        internal static bool Invoke(IntPtr metaObject, int methodIndex, string member, QObject target,
                                    Qt.ConnectionType connectionType, object[] arguments,
                                    bool wantResult, out object result, out string error)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            result = null;
            error = null;
            object[] values = arguments ?? Array.Empty<object>();
            string label = member ?? "the method";
            if (values.Length > MaxArguments)
            {
                error = $"'{label}' has {values.Length} arguments; QMetaMethod::invoke carries at most {MaxArguments}";
                return false;
            }

            IntPtr targetHandle;
            try
            {
                targetHandle = target.Handle;
            }
            catch (ObjectDisposedException)
            {
                error = "the target object has been destroyed";
                return false;
            }

            var storage = new ArgumentStorage(values.Length);
            IntPtr resultSlot = IntPtr.Zero;
            try
            {
                var entries = new InvokeArgument[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    try
                    {
                        entries[i] = storage.Prepare(values[i]);
                    }
                    catch (ObjectDisposedException)
                    {
                        error = $"argument {i} is a Qt value whose object has been destroyed";
                        return false;
                    }
                    catch (Exception exception)
                    {
                        error = $"argument {i} cannot cross as a Qt argument: {exception.Message}";
                        return false;
                    }
                }

                if (wantResult)
                {
                    resultSlot = Marshal.AllocHGlobal(IntPtr.Size);
                    Marshal.WriteIntPtr(resultSlot, IntPtr.Zero);
                }
                int status = metaObject != IntPtr.Zero
                    ? QMetaMethodInvoke(metaObject, methodIndex, targetHandle, (int)connectionType,
                                        entries, entries.Length, resultSlot)
                    : QMetaObjectInvoke(targetHandle, member, (int)connectionType,
                                        entries, entries.Length, resultSlot);

                IntPtr variant = resultSlot == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(resultSlot);
                if (status != Ok)
                {
                    // Defensive: a failed call must not leave a native QVariant behind.
                    if (variant != IntPtr.Zero)
                        VariantMarshalling.ToManaged(variant);
                    error = Describe(status, label, values);
                    return false;
                }
                result = variant == IntPtr.Zero ? null : VariantMarshalling.ToManaged(variant);
                return true;
            }
            finally
            {
                if (resultSlot != IntPtr.Zero)
                    Marshal.FreeHGlobal(resultSlot);
                storage.Dispose();
            }
        }

        static string Describe(int status, string label, object[] arguments)
        {
            switch (status)
            {
                case Invalid:
                    return $"'{label}' cannot be invoked on the target";
                case NotFound:
                    return $"no method '{label}' with {arguments.Length} argument(s) exists on the target";
                case TooManyArguments:
                    return $"'{label}' has more than {MaxArguments} arguments";
                case ReturnUnavailable:
                    return $"the return type of '{label}' cannot be copied into a QVariant";
                case Failed:
                    return $"Qt refused the call to '{label}' (the argument types did not match its "
                           + "parameters, or a queued connection asked for a return value)";
                default:
                    return $"Qt refused the call to '{label}' (status {status})";
            }
        }

        // Owns the native storage one call's arguments point at. A string is a heap QString and a
        // byte[] a heap QByteArray (both released by the runtime's own free functions); scalars and
        // pointer slots are AllocHGlobal blocks; a value handed over as a QVariant is a lease.
        sealed class ArgumentStorage : IDisposable
        {
            readonly List<IntPtr> blocks;
            readonly List<IntPtr> qstrings;
            readonly List<IntPtr> qbyteArrays;
            readonly List<VariantLease> leases;

            public ArgumentStorage(int capacity)
            {
                blocks = new List<IntPtr>(capacity);
                qstrings = new List<IntPtr>(capacity);
                qbyteArrays = new List<IntPtr>(capacity);
                leases = new List<VariantLease>(capacity);
            }

            internal InvokeArgument Prepare(object value)
            {
                if (value == null)
                    return Variant(null);

                Type type = value.GetType();
                if (value is IQtObject qtObject)
                    return Pointer(MetaType.QtName(type), qtObject.Handle);
                if (value is IQtHandle handle)
                    return new InvokeArgument { TypeName = MetaType.QtName(type), Value = handle.Handle };
                if (type == typeof(string))
                {
                    IntPtr text = ApiRuntime.NewQString((string)value);
                    qstrings.Add(text);
                    return new InvokeArgument { TypeName = "QString", Value = text };
                }
                if (type == typeof(byte[]))
                {
                    IntPtr bytes = ApiRuntime.NewByteArray((byte[])value);
                    qbyteArrays.Add(bytes);
                    return new InvokeArgument { TypeName = "QByteArray", Value = bytes };
                }
                // A Qt enum crosses as its four-byte value; Int64 keeps a uint-backed flag with
                // the top bit set rather than overflowing the conversion.
                if (type.IsEnum)
                    return Scalar(MetaType.QtName(type), Convert.ToInt64(value), 4);

                switch (Type.GetTypeCode(type))
                {
                    case TypeCode.Boolean:
                        return Scalar("bool", (bool)value ? 1 : 0, 1);
                    case TypeCode.SByte:
                        return Scalar("char", (sbyte)value, 1);
                    case TypeCode.Byte:
                        return Scalar("uchar", (byte)value, 1);
                    case TypeCode.Int16:
                        return Scalar("short", (short)value, 2);
                    case TypeCode.UInt16:
                        return Scalar("ushort", (ushort)value, 2);
                    case TypeCode.Int32:
                        return Scalar("int", (int)value, 4);
                    case TypeCode.UInt32:
                        return Scalar("uint", (uint)value, 4);
                    case TypeCode.Int64:
                        return Scalar("qint64", (long)value, 8);
                    case TypeCode.UInt64:
                        return Scalar("quint64", unchecked((long)(ulong)value), 8);
                    case TypeCode.Single:
                        return Scalar("float", BitConverter.SingleToInt32Bits((float)value), 4);
                    case TypeCode.Double:
                        return Scalar("double", BitConverter.DoubleToInt64Bits((double)value), 8);
                    case TypeCode.Char:
                        return Scalar("QChar", (char)value, 2);
                    default:
                        if (type == typeof(IntPtr))
                            return Scalar("void*", ((IntPtr)value).ToInt64(), IntPtr.Size);
                        // Everything else crosses as a QVariant: the native shim converts it to the
                        // parameter's declared type when it can, and Qt refuses the call cleanly
                        // when it cannot.
                        return Variant(value);
                }
            }

            InvokeArgument Variant(object value)
            {
                VariantLease lease = VariantMarshalling.Value(value);
                leases.Add(lease);
                return new InvokeArgument { TypeName = "QVariant", Value = lease.Handle };
            }

            InvokeArgument Pointer(string typeName, IntPtr handle)
            {
                // A pointer parameter's QGenericArgument points at a pointer variable.
                IntPtr slot = Marshal.AllocHGlobal(IntPtr.Size);
                Marshal.WriteIntPtr(slot, handle);
                blocks.Add(slot);
                return new InvokeArgument { TypeName = typeName, Value = slot };
            }

            InvokeArgument Scalar(string typeName, long bits, int size)
            {
                IntPtr slot = Marshal.AllocHGlobal(8);
                Marshal.WriteInt64(slot, 0);
                if (size == 8)
                    Marshal.WriteInt64(slot, bits);
                else if (size == 4)
                    Marshal.WriteInt32(slot, unchecked((int)bits));
                else if (size == 2)
                    Marshal.WriteInt16(slot, unchecked((short)bits));
                else
                    Marshal.WriteByte(slot, unchecked((byte)bits));
                blocks.Add(slot);
                return new InvokeArgument { TypeName = typeName, Value = slot };
            }

            public void Dispose()
            {
                foreach (IntPtr text in qstrings)
                    ApiRuntime.FreeQString(text);
                foreach (IntPtr array in qbyteArrays)
                    ApiRuntime.FreeByteArray(array);
                foreach (IntPtr block in blocks)
                    Marshal.FreeHGlobal(block);
                foreach (VariantLease lease in leases)
                    lease.Dispose();
                qstrings.Clear();
                qbyteArrays.Clear();
                blocks.Clear();
                leases.Clear();
            }
        }
    }
}
