using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using QtSharp.Runtime;

namespace QtSharp.QtCore
{
    /// <summary>
    /// A function address Qt will not vouch for a signature on: the managed counterpart of Qt's
    /// <c>QFunctionPointer</c> typedef.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Qt declares <c>QFunctionPointer</c> as "a pointer to a function" and nothing more. The APIs
    /// that return one - <c>QLibrary::resolve</c>, <c>QGuiApplication::platformFunction</c>,
    /// <c>QOpenGLContext::getProcAddress</c> - are how a caller loads a symbol whose signature only
    /// the caller knows. The address is therefore carried opaquely: this struct is one field, and
    /// <see cref="As{TDelegate}"/> is the opt-in point where the caller supplies the signature.
    /// </para>
    /// <para>
    /// That is also why this is not an <see cref="IntPtr"/>: an address with no signature should
    /// not be interchangeable with a handle or an arbitrary pointer. The conversion is explicit in
    /// both directions (<see cref="op_Explicit(IntPtr)"/> and
    /// <see cref="op_Explicit(QFunctionPointer)"/>), and equality is address equality.
    /// </para>
    /// </remarks>
    public readonly struct QFunctionPointer : IEquatable<QFunctionPointer>
    {
        readonly IntPtr address;

        /// <summary>Wraps a raw function address.</summary>
        public QFunctionPointer(IntPtr address)
        {
            this.address = address;
        }

        /// <summary>The raw address, for code that marshals it itself.</summary>
        public IntPtr Address => address;

        /// <summary>Whether this is Qt's <c>nullptr</c> (a symbol that did not resolve).</summary>
        public bool IsNull => address == IntPtr.Zero;

        /// <summary>
        /// Turns the address into the delegate the caller says it is.
        /// </summary>
        /// <typeparam name="TDelegate">
        /// A delegate type whose signature matches the native function. The caller vouches for it:
        /// this is the same contract <c>QOpenGLContext::getProcAddress</c> documents in C++, and a
        /// wrong signature or a dangling address is a process crash, not an exception.
        /// </typeparam>
        /// <returns>
        /// <c>null</c> for a null address. An address this binding installed comes back as the
        /// very delegate instance that was installed; the generated typed returns (for example
        /// <c>QEasingCurve.CustomType()</c> and <c>Qt.QInstallMessageHandler</c>) use the same
        /// registry lookup and keep that identity. Any other address is marshalled with the
        /// platform's default calling convention.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// <typeparamref name="TDelegate"/> is not a concrete delegate type, or its signature
        /// cannot be called by the generic bridge (see the remarks).
        /// </exception>
        /// <remarks>
        /// A non-generic delegate is handed to <see cref="Marshal.GetDelegateForFunctionPointer(IntPtr, Type)"/>
        /// directly. A constructed generic delegate (<see cref="Action"/>, <see cref="Func{TResult}"/>,
        /// …) cannot be: the marshaller rejects generic delegate types. The bridge emits a
        /// <c>calli</c> with the same signature, so those delegates work with the blittable shapes
        /// the C ABI actually carries - scalars other than <see cref="bool"/> and
        /// <see cref="char"/>, enums, and pointers. A signature containing a managed-only type
        /// (string, array, class) is refused; declare a matching non-generic delegate with the
        /// marshalling attributes instead.
        /// </remarks>
        public TDelegate As<TDelegate>() where TDelegate : Delegate
        {
            if (address == IntPtr.Zero)
                return null;
            if (!typeof(TDelegate).IsSubclassOf(typeof(MulticastDelegate)))
                throw new InvalidOperationException(
                    "As<TDelegate> needs a concrete delegate type, not Delegate or MulticastDelegate.");

            // The same registry lookup the generated typed returns use: an address this binding
            // installed resolves to the caller's own delegate rather than a new wrapper. The
            // field is copied out because a struct's lambda cannot capture `this`.
            IntPtr pointer = address;
            try
            {
                return QtCallbackRegistry.Wrap(pointer, () => CreateDelegate<TDelegate>(pointer));
            }
            catch (ArgumentException exception)
            {
                // The marshaller's own rejection of the delegate type; state the contract in the
                // exception type As documents.
                throw new InvalidOperationException(
                    $"QFunctionPointer.As<{typeof(TDelegate).Name}>: the delegate type cannot be "
                    + "marshalled from a function address.", exception);
            }
        }

        /// <summary>
        /// The non-throwing form of <see cref="As{TDelegate}"/>: false for a null address and for
        /// a <typeparamref name="TDelegate"/> the bridge cannot marshal.
        /// </summary>
        public bool TryAs<TDelegate>(out TDelegate callback) where TDelegate : Delegate
        {
            try
            {
                callback = As<TDelegate>();
                return callback != null;
            }
            catch (Exception exception)
                when (exception is InvalidOperationException || exception is ArgumentException)
            {
                callback = null;
                return false;
            }
        }

        static TDelegate CreateDelegate<TDelegate>(IntPtr address) where TDelegate : Delegate
        {
            Type type = typeof(TDelegate);
            if (!type.IsGenericType)
                return (TDelegate)Marshal.GetDelegateForFunctionPointer(address, type);
            return (TDelegate)CreateGenericDelegate(type, address);
        }

        // The generic delegate types (Action<...>/Func<...>) the binding itself uses cannot go
        // through Marshal.GetDelegateForFunctionPointer, which rejects any generic type. A calli
        // with the Invoke signature has the same ABI effect and keeps As<Func<double,double>> as
        // ordinary C#. Only blittable types can be passed without the marshaller, so the rest is
        // refused with an explanation rather than called with a guessed conversion.
        static Delegate CreateGenericDelegate(Type delegateType, IntPtr address)
        {
            MethodInfo invoke = delegateType.GetMethod("Invoke");
            ParameterInfo[] parameters = invoke.GetParameters();
            var parameterTypes = new Type[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                Type parameter = parameters[i].ParameterType;
                if (parameter.IsByRef || !IsBlittable(parameter))
                    throw new InvalidOperationException(
                        $"QFunctionPointer.As<{delegateType.Name}>: '{parameter}' cannot cross the C ABI "
                        + "without marshalling. Use a non-generic delegate with the matching attributes.");
                parameterTypes[i] = parameter;
            }
            if (invoke.ReturnType != typeof(void) && !IsBlittable(invoke.ReturnType))
                throw new InvalidOperationException(
                    $"QFunctionPointer.As<{delegateType.Name}>: the return type '{invoke.ReturnType}' cannot "
                    + "cross the C ABI without marshalling. Use a non-generic delegate with the matching attributes.");

            var method = new DynamicMethod(
                "QtFunctionPointer", invoke.ReturnType, parameterTypes, typeof(QFunctionPointer).Module, true);
            ILGenerator il = method.GetILGenerator();
            for (int i = 0; i < parameterTypes.Length; i++)
                il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Ldc_I8, address.ToInt64());
            il.Emit(OpCodes.Conv_I);
            // The platform's default unmanaged convention, the same one
            // Marshal.GetDelegateForFunctionPointer applies when the delegate declares none.
            il.EmitCalli(OpCodes.Calli, CallingConvention.Winapi, invoke.ReturnType, parameterTypes);
            il.Emit(OpCodes.Ret);
            return method.CreateDelegate(delegateType);
        }

        static bool IsBlittable(Type type)
        {
            if (type.IsEnum || type.IsPointer)
                return true;
            if (type == typeof(IntPtr) || type == typeof(UIntPtr))
                return true;
            return type == typeof(sbyte) || type == typeof(byte)
                || type == typeof(short) || type == typeof(ushort)
                || type == typeof(int) || type == typeof(uint)
                || type == typeof(long) || type == typeof(ulong)
                || type == typeof(float) || type == typeof(double);
        }

        /// <inheritdoc/>
        public bool Equals(QFunctionPointer other) => address == other.address;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is QFunctionPointer other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => address.GetHashCode();

        /// <inheritdoc/>
        public override string ToString() => address == IntPtr.Zero ? "0x0" : $"0x{address.ToInt64():X}";

        /// <summary>Wraps a raw function address.</summary>
        public static explicit operator QFunctionPointer(IntPtr address) => new QFunctionPointer(address);

        /// <summary>Unwraps the raw function address.</summary>
        public static explicit operator IntPtr(QFunctionPointer pointer) => pointer.address;
    }
}
