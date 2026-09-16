using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32.SafeHandles;

// PoC #3: ownership / lifetime. See native/QtSharp_ownership.cpp for the model.
public static class Ownership
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MetaCallback(IntPtr userData, int argc, IntPtr argv, IntPtr typeIds);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_meta_connect", CallingConvention = CallingConvention.Cdecl)]
    private static extern int MetaConnect(IntPtr sender, [MarshalAs(UnmanagedType.LPUTF8Str)] string signature,
        MetaCallback callback, IntPtr userData, int connectionType);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_own_tracked_create", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr CreateTracked(IntPtr parent);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_own_tracked_live", CallingConvention = CallingConvention.Cdecl)]
    public static extern int LiveCount();

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_own_delete", CallingConvention = CallingConvention.Cdecl)]
    public static extern void DeleteNative(IntPtr objectPtr);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_own_name_length", CallingConvention = CallingConvention.Cdecl)]
    public static extern int NameLength(IntPtr objectPtr);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_own_set_name", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SetName(IntPtr objectPtr, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    static readonly Dictionary<IntPtr, WeakReference> wrappers = new();
    static readonly HashSet<IntPtr> destroyed = new();
    static readonly List<object> keepAlive = new();

    // Registers a managed wrapper for a native object and invalidates it when Qt destroys
    // the native object (QObject::destroyed is emitted from ~QObject).
    public static void Watch(IntPtr native)
    {
        destroyed.Remove(native);

        var context = GCHandle.Alloc(native);
        MetaCallback callback = (userData, argc, argv, typeIds) =>
        {
            var ptr = (IntPtr)GCHandle.FromIntPtr(userData).Target;
            destroyed.Add(ptr);
            wrappers.Remove(ptr);
            GCHandle.FromIntPtr(userData).Free();
        };
        keepAlive.Add(callback);

        if (MetaConnect(native, "destroyed(QObject*)", callback, GCHandle.ToIntPtr(context), 0) == 0)
            context.Free();
    }

    public static void Register(IntPtr native, object wrapper) => wrappers[native] = new WeakReference(wrapper);

    public static bool IsAlive(IntPtr native) => !destroyed.Contains(native);

    // The PySide "RuntimeError on a deleted C++ object" equivalent: fail loudly instead of
    // dereferencing freed memory.
    public static object Resolve(IntPtr native)
    {
        if (destroyed.Contains(native))
            throw new ObjectDisposedException("QtObject", "The underlying C++ object was destroyed");
        wrappers.TryGetValue(native, out var weak);
        return weak?.Target;
    }
}

// A native object that .NET owns: released exactly once by SafeHandle, on Dispose or on
// finalization. (Note for the product: SafeHandle finalization happens on the finalizer
// thread, so for real QObjects you want deleteLater()/owning-thread deletion instead.)
public sealed class OwnedNativeObject : SafeHandleZeroOrMinusOneIsInvalid
{
    public OwnedNativeObject(IntPtr native) : base(ownsHandle: true) => SetHandle(native);

    protected override bool ReleaseHandle()
    {
        Ownership.DeleteNative(handle);
        return true;
    }
}

public static class OwnershipTest
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void DropManagedOwnedAndCollect()
    {
        var owned = new OwnedNativeObject(Ownership.CreateTracked(IntPtr.Zero));
        GC.KeepAlive(owned);
    }

    public static int Run()
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        // --- A. Qt parent-child destroys children; managed wrappers get invalidated ---
        IntPtr parent = Ownership.CreateTracked(IntPtr.Zero);
        IntPtr child = Ownership.CreateTracked(parent);
        Ownership.Watch(parent);
        Ownership.Watch(child);
        Check(Ownership.LiveCount() == 2, $"two native objects alive (got {Ownership.LiveCount()})");

        Ownership.DeleteNative(parent); // Qt deletes the child as well
        Check(Ownership.LiveCount() == 0, $"deleting the parent destroyed the child (live={Ownership.LiveCount()})");
        Check(!Ownership.IsAlive(child), "child wrapper was invalidated by QObject::destroyed");
        bool threw = false;
        try { Ownership.Resolve(child); } catch (ObjectDisposedException) { threw = true; }
        Check(threw, "accessing the destroyed object throws instead of crashing");

        // --- B. Qt-owned objects must survive a GC that knows nothing about them ---
        IntPtr parent2 = Ownership.CreateTracked(IntPtr.Zero);
        IntPtr child2 = Ownership.CreateTracked(parent2);
        Ownership.Watch(child2);
        Ownership.SetName(child2, "still-alive");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Check(Ownership.LiveCount() == 2, $"GC did not free Qt-owned objects (live={Ownership.LiveCount()})");
        Check(Ownership.NameLength(child2) == "still-alive".Length, "Qt-owned object still usable from native");
        Ownership.DeleteNative(parent2);
        Check(Ownership.LiveCount() == 0, "Qt-owned objects released by their Qt parent");

        // --- C. Managed-owned objects are released exactly once ---
        var owned = new OwnedNativeObject(Ownership.CreateTracked(IntPtr.Zero));
        Check(Ownership.LiveCount() == 1, "managed-owned object alive");
        owned.Dispose();
        Check(Ownership.LiveCount() == 0, "SafeHandle released it exactly once");
        owned.Dispose();
        Check(Ownership.LiveCount() == 0, "double Dispose is a no-op (no double free)");

        DropManagedOwnedAndCollect();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Check(Ownership.LiveCount() == 0, "finalizer released an abandoned managed-owned object exactly once");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
