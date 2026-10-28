using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// Hand-written runtime that ships with the generated bindings (not model-derived).
// Qt takes function pointers only as raw addresses; this is where the managed delegate behind
// each address is kept alive, and where an address Qt hands back is turned into a delegate.
namespace QtSharp.Runtime
{
    /// <summary>
    /// The managed delegates behind the function pointers handed to Qt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Qt stores a function pointer as an address and can call it at any later time, from C++
    /// frames the managed side no longer controls (a message handler on the GUI thread, a
    /// post-routine at application exit, a custom easing function until the curve is destroyed).
    /// <see cref="Marshal.GetFunctionPointerForDelegate"/> does not keep the delegate alive, and a
    /// collected delegate would leave Qt calling freed memory. Every address handed out here is
    /// therefore rooted, together with its thunk, for the lifetime of the process.
    /// </para>
    /// <para>
    /// The key is the caller's delegate instance, compared by <em>reference identity</em>. It has to
    /// be: <see cref="Delegate.Equals(object)"/> compares target and method, so two distinct
    /// delegates over the same method would collapse into one entry, and the reverse lookup would
    /// hand back the wrong instance. Reference identity is also what lets <c>qRemovePostRoutine</c>
    /// find the routine <c>qAddPostRoutine</c> registered - pass the same delegate object to both.
    /// </para>
    /// <para>
    /// Identity is a property of this registry. An address the binding installed comes back from
    /// <see cref="Wrap{TDelegate}(IntPtr, Func{TDelegate})"/> as the very delegate instance that was
    /// installed, for as long as the entry lives - which here means the lifetime of the process.
    /// An address installed by native code has no managed instance to return: the wrapper is a
    /// delegate over that address (same behaviour, not the same object), memoized per address and
    /// delegate type so repeated reads return one instance while one address can still be read
    /// under a second signature through <c>QFunctionPointer.As&lt;TDelegate&gt;</c>.
    /// </para>
    /// <para>
    /// Rooting forever is the .NET analogue of the fixed pool PySide6 uses for the same problem
    /// (it keeps a handful of static trampolines and frees a slot when the QEasingCurve wrapper is
    /// collected). The trade-off differs: this registry does not limit how many distinct callbacks
    /// a program installs, but a callback installed on a long-lived Qt object stays reachable even
    /// after the managed wrapper is gone, exactly as the native object itself can outlive its
    /// wrapper. That is the safe failure direction: the delegate may be released late, never early.
    /// </para>
    /// </remarks>
    public static class QtCallbackRegistry
    {
        sealed class Entry
        {
            // Named rather than kept in a tuple so the rooting is explicit: the thunk must live as
            // long as the address in Pointer is reachable.
            internal Delegate Thunk;
            internal IntPtr Pointer;
        }

        static readonly object gate = new object();

        // The key roots the caller's delegate and the value roots the thunk, so neither can be
        // finalized while Qt still holds the address. ReferenceEqualityComparer is the identity
        // comparer the remarks above explain; IEqualityComparer<object> is contravariant, so it
        // serves the Delegate key directly.
        static readonly Dictionary<Delegate, Entry> entries =
            new Dictionary<Delegate, Entry>(ReferenceEqualityComparer.Instance);

        // The same registrations keyed the other way, for a callback Qt hands back
        // (QEasingCurve::customType, qInstallMessageHandler's return value). A hit means the address
        // is ours and the caller's own delegate instance can be returned.
        static readonly Dictionary<IntPtr, Delegate> installedByAddress = new Dictionary<IntPtr, Delegate>();

        // Wrappers over addresses this registry did not install. Kept apart from the installed map
        // so a wrapper can never be mistaken for the caller's delegate, and bounded by the number
        // of distinct address/signature pairs the program reads. The delegate type is part of the
        // key: QFunctionPointer.As<TDelegate> lets the caller name a different signature for the
        // same address, and both wrappers have to coexist. It holds only managed state (the
        // wrappers and the native delegates over the address); nothing here owns native memory.
        static readonly Dictionary<(IntPtr Address, Type DelegateType), Delegate> foreignByAddress =
            new Dictionary<(IntPtr Address, Type DelegateType), Delegate>();

        /// <summary>
        /// Returns the native address that stands for <paramref name="callback"/>, creating and
        /// rooting a thunk with <paramref name="create"/> the first time the delegate is seen.
        /// </summary>
        /// <param name="callback">
        /// The managed callback, or <c>null</c>. Null is Qt's own <c>nullptr</c> - the generated
        /// wrapper uses it to restore a default handler - and maps to <see cref="IntPtr.Zero"/>.
        /// </param>
        /// <param name="create">
        /// Builds the thunk delegate. It is only called once per distinct callback instance.
        /// </param>
        /// <returns>The address to pass to Qt.</returns>
        public static IntPtr Get(Delegate callback, Func<Delegate> create)
        {
            if (callback == null)
                return IntPtr.Zero;
            if (create == null)
                throw new ArgumentNullException(nameof(create));

            lock (gate)
            {
                if (entries.TryGetValue(callback, out Entry entry))
                    return entry.Pointer;

                Delegate thunk = create();
                IntPtr pointer = Marshal.GetFunctionPointerForDelegate(thunk);
                entries.Add(callback, new Entry { Thunk = thunk, Pointer = pointer });
                installedByAddress.Add(pointer, callback);
                return pointer;
            }
        }

        /// <summary>
        /// Turns an address Qt returned into the delegate the declaration says it is.
        /// </summary>
        /// <param name="pointer">The address Qt returned; <see cref="IntPtr.Zero"/> means none.</param>
        /// <param name="create">
        /// Builds the managed delegate over a foreign address. It is called once per address and
        /// delegate type, and the generated thunk classes pass a factory that marshals the address
        /// with Qt's declared signature and converts the values. It is never called for an address
        /// this registry installed, nor for null.
        /// </param>
        /// <remarks>
        /// <para>
        /// An address this registry handed out comes back as the very delegate instance that was
        /// installed: the reverse lookup runs first, so <c>curve.CustomType()</c> after
        /// <c>curve.SetCustomType(f)</c> is <c>f</c> by reference, not a new wrapper. That holds for
        /// the lifetime of the process, which is as long as the entry is rooted.
        /// </para>
        /// <para>
        /// Any other address was installed by native code. Qt's declaration is the contract there,
        /// and the generated factory marshals it with that signature. The resulting wrapper is not
        /// the delegate that installed the address (there is none to return) and is memoized per
        /// address and delegate type. Calling a stale or dangling pointer is a process crash rather
        /// than an exception; the marshalling uses the platform's default calling convention, which
        /// matches the Cdecl callbacks on the x64 RIDs this project ships.
        /// </para>
        /// </remarks>
        internal static TDelegate Wrap<TDelegate>(IntPtr pointer, Func<TDelegate> create) where TDelegate : Delegate
        {
            if (pointer == IntPtr.Zero)
                return null;
            if (create == null)
                throw new ArgumentNullException(nameof(create));

            lock (gate)
            {
                if (installedByAddress.TryGetValue(pointer, out Delegate installed) && installed is TDelegate callback)
                    return callback;
                var key = (pointer, typeof(TDelegate));
                if (foreignByAddress.TryGetValue(key, out Delegate wrapper) && wrapper is TDelegate cached)
                    return cached;

                TDelegate created = create();
                foreignByAddress.Add(key, created);
                return created;
            }
        }
    }
}
