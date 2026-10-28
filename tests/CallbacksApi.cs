using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

// The few Qt APIs that take a plain C function pointer: QEasingCurve's custom easing function,
// QLibrary::resolve / QGuiApplication::platformFunction (QFunctionPointer, an address coming back
// out of Qt), the post-routine list and the message handler.
//
// A concrete function type is an Action/Func in C# in both positions. Qt keeps only the address,
// so the thunk behind it must outlive the installing call - QtCallbackRegistry roots it - and an
// address Qt hands back is looked up in the same registry so a callback comes back as the instance
// that was installed. QFunctionPointer promises no signature and is the QtCore carrier struct,
// with As<TDelegate> supplying the signature at the call site.
[Test("test-callbacks")]
public static class ApiCallbacksDemo
{
    // A test-local native function: its address is real, but this test installs it rather than the
    // binding. That is the "foreign pointer" path Wrap has to marshal with the declared signature.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate double NativeUnary(double value);

    static double StaticSquare(double value) => value * value;

    public static int Run(TestContext context)
    {
        // ---------------------------------------------------------------- QEasingCurve
        // qreal(*)(qreal) is Func<double, double>; Qt calls it from valueForProgress().
        var curve = new QEasingCurve();
        context.Check(curve.CustomType() == null,
            "a curve without a custom function reports no callback (null for Qt's nullptr)");

        Func<double, double> square = progress => progress * progress;
        curve.SetCustomType(square);

        // A concrete function type has a declared signature, so the return position wraps back
        // into the same delegate type. The registry's reverse lookup makes it the very instance
        // that was installed rather than a new delegate over the same address.
        Func<double, double> installed = curve.CustomType();
        context.Check(installed != null,
            "QEasingCurve::customType returns the installed delegate");
        context.Check(ReferenceEquals(installed, square),
            "the delegate comes back as the same instance that was installed");

        double value = curve.ValueForProgress(0.5);
        context.Check(Math.Abs(value - 0.25) < 1e-9,
            $"the custom easing function runs when Qt calls it (valueForProgress(0.5) = {value:F6})");
        context.Check(Math.Abs(installed(0.5) - 0.25) < 1e-9,
            $"the returned delegate is invokable (customType()(0.5) = {installed(0.5):F6})");

        // One delegate instance is one native routine, so two curves given the same delegate read
        // that same instance back. This is the identity qRemovePostRoutine relies on.
        var secondCurve = new QEasingCurve();
        secondCurve.SetCustomType(square);
        context.Check(ReferenceEquals(secondCurve.CustomType(), square),
            "the same delegate installed on two curves comes back on both");

        // Delegate.Equals compares target+method, not identity. Build two separate delegate objects
        // over the same method (the compiler may cache a method-group conversion, so spell it
        // through Delegate.CreateDelegate); they are equal but distinct. The registry is keyed by
        // reference identity, so each curve gets back exactly the instance installed on it; a
        // value-keyed map would collapse both onto g1.
        MethodInfo squareMethod = typeof(ApiCallbacksDemo).GetMethod(
            nameof(StaticSquare), BindingFlags.NonPublic | BindingFlags.Static);
        Func<double, double> g1 = (Func<double, double>)Delegate.CreateDelegate(typeof(Func<double, double>), squareMethod);
        Func<double, double> g2 = (Func<double, double>)Delegate.CreateDelegate(typeof(Func<double, double>), squareMethod);
        context.Check(!ReferenceEquals(g1, g2), "the two delegates are distinct objects");
        context.Check(g1.Equals(g2), "the two delegates are equal by Delegate.Equals (the trap)");
        var curveA = new QEasingCurve();
        var curveB = new QEasingCurve();
        curveA.SetCustomType(g1);
        curveB.SetCustomType(g2);
        context.Check(ReferenceEquals(curveA.CustomType(), g1) && ReferenceEquals(curveB.CustomType(), g2),
            "distinct-but-equal delegates come back as the instance installed on their own curve");
        context.Check(!ReferenceEquals(curveB.CustomType(), g1),
            "the second curve does not get the first curve's equal delegate");

        // ---------------------------------------------------------------- QFunctionPointer
        // QFunctionPointer is Qt's signature-less function typedef: a carrier for an address, with
        // the signature supplied only at the call site. As<TDelegate> is the same registry-aware
        // wrap the typed returns use, so an address this binding installed resolves to the
        // caller's own delegate, while a foreign address is marshalled with the declared shape.
        // qVersion is exported by every Qt6Core.
        QFunctionPointer qVersion = ResolveQVersion(out string library);
        context.Check(!qVersion.IsNull,
            $"QLibrary::resolve returns a non-null QFunctionPointer (qVersion in '{library}' -> {qVersion})");

        // The conversion is explicit in both directions, and the carrier compares by address.
        IntPtr rawAddress = (IntPtr)qVersion;
        QFunctionPointer roundTrip = (QFunctionPointer)rawAddress;
        context.Check(roundTrip.Equals(qVersion) && roundTrip.Address == rawAddress,
            "QFunctionPointer round-trips through its explicit IntPtr conversion");

        // A default carrier and a symbol that does not resolve are both null, not a wrapped zero.
        QFunctionPointer none = default;
        context.Check(none.IsNull && none.Address == IntPtr.Zero,
            "default(QFunctionPointer) is null");
        context.Check(QLibrary.Resolve(library, "qtsharp_no_such_symbol").IsNull,
            "an unresolved symbol is a null QFunctionPointer");
        context.Check(none.As<Func<double, double>>() == null,
            "As<TDelegate> on a null QFunctionPointer is null");
        context.Check(!none.TryAs<Func<double, double>>(out Func<double, double> missingCallback)
                && missingCallback == null,
            "TryAs<TDelegate> is false on a null QFunctionPointer");

        // A pointer produced outside the binding: the address is real, the signature is the
        // caller's. As<Func<double,double>> bridges it through a calli (the marshaller rejects
        // generic delegate types) and the registry memoizes the wrapper per address and delegate
        // type.
        NativeUnary native = StaticSquare;
        IntPtr foreignPointer = Marshal.GetFunctionPointerForDelegate(native);
        QFunctionPointer carrier = (QFunctionPointer)foreignPointer;
        Func<double, double> foreign = carrier.As<Func<double, double>>();
        context.Check(foreign != null && Math.Abs(foreign(0.5) - 0.25) < 1e-9,
            $"As<Func<double,double>> calls a foreign address (got {foreign?.Invoke(0.5):F6})");
        Func<double, double> foreignAgain = carrier.As<Func<double, double>>();
        context.Check(ReferenceEquals(foreign, foreignAgain),
            "As<TDelegate> on the same address returns the memoized wrapper");
        context.Check(!ReferenceEquals(foreign, native),
            "the wrapper over a foreign address is not the delegate that installed it");

        // The caller may name another signature for the same address; the non-generic delegate
        // goes through the marshaller and must coexist with the Func wrapper already cached.
        // (Marshal may even hand back the original instance when the pointer came from it.)
        NativeUnary sameAddressOtherSignature = carrier.As<NativeUnary>();
        context.Check(Math.Abs(sameAddressOtherSignature(0.5) - 0.25) < 1e-9,
            "the same address can be read under a second delegate signature");

        // A signature the bridge cannot carry is the one documented failure: As throws
        // InvalidOperationException, and TryAs reports it without throwing.
        context.Check(!carrier.TryAs<Func<string, string>>(out Func<string, string> unsupported)
                && unsupported == null,
            "TryAs<TDelegate> is false for a signature the bridge cannot marshal");
        bool rejected = false;
        try
        {
            carrier.As<Func<string, string>>();
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        context.Check(rejected,
            "As<TDelegate> throws InvalidOperationException for a signature the bridge cannot marshal");
        GC.KeepAlive(native);

        // QGuiApplication::platformFunction is the same carrier with a QByteArray argument. The
        // process cannot name a function the offscreen plugin actually has without knowing the
        // QPA plugin, so this checks the crossing itself: an unknown name is a null pointer.
        using (var unknown = new QByteArray("qtsharp-no-such-platform-function"))
        {
            context.Check(QGuiApplication.PlatformFunction(unknown).IsNull,
                "QGuiApplication::platformFunction returns a QFunctionPointer (null for an unknown name)");
        }

        // ---------------------------------------------------------------- post routines
        // qAddPostRoutine stores the address; qRemovePostRoutine has to find the same address, so
        // both calls must map one delegate instance to one pointer. Neither call may throw.
        Action postRoutine = () => { };
        try
        {
            Qt.QAddPostRoutine(postRoutine);
            Qt.QRemovePostRoutine(postRoutine);
            context.Check(true, "qAddPostRoutine and qRemovePostRoutine accept the same delegate");
        }
        catch (Exception exception)
        {
            context.Check(false,
                $"qAddPostRoutine/qRemovePostRoutine threw {exception.GetType().Name}: {exception.Message}");
        }

        // Qt's qRemovePostRoutine searches its list and has nothing to report when the routine is
        // absent: removing one that was never added is a no-op, not a crash or an exception.
        Action neverAdded = () => { };
        try
        {
            Qt.QRemovePostRoutine(neverAdded);
            context.Check(true, "removing a post routine that was never added is a no-op");
        }
        catch (Exception exception)
        {
            context.Check(false,
                $"removing an unknown post routine threw {exception.GetType().Name}: {exception.Message}");
        }

        // ---------------------------------------------------------------- message handler
        // QtMessageHandler is void(*)(QtMsgType, const QMessageLogContext&, const QString&). The
        // context is a class the binding does not publish, so it crosses as the raw pointer (the
        // documented void* exception); the message is read from Qt's QString by reference.
        string captured = null;
        QtMsgType capturedType = default;
        Action<QtMsgType, IntPtr, string> handler = (type, contextPointer, message) =>
        {
            if (message.Contains("negative interval", StringComparison.OrdinalIgnoreCase))
            {
                captured = message;
                capturedType = type;
            }
        };
        Action<QtMsgType, IntPtr, string> secondHandler = (type, contextPointer, message) => { };

        // Qt 6.12's initial handler is a real function (qDefaultMessageHandler), not nullptr, so
        // the first install returns a wrapper over that foreign address. The chain below is what
        // the binding promises: each install returns the handler it replaced, as the same instance.
        // (On a Qt whose default handler is null, the first value is null instead.)
        Action<QtMsgType, IntPtr, string> defaultHandler = Qt.QInstallMessageHandler(handler);
        try
        {
            context.Check(defaultHandler == null || !ReferenceEquals(defaultHandler, handler),
                "qInstallMessageHandler returns what it replaced, not the new handler");

            // A negative interval is a Qt warning, logged through the installed handler.
            var timer = new QTimer();
            timer.Start(-5);
            context.Check(captured != null,
                $"the message handler saw QTimer's warning (got '{captured ?? "nothing"}')");
            context.Check(capturedType == QtMsgType.QtWarningMsg,
                $"the handler receives the message type (got {capturedType})");

            Action<QtMsgType, IntPtr, string> replaced = Qt.QInstallMessageHandler(secondHandler);
            context.Check(ReferenceEquals(replaced, handler),
                "installing a second handler returns the first, as the same instance");

            Action<QtMsgType, IntPtr, string> removed = Qt.QInstallMessageHandler(null);
            context.Check(ReferenceEquals(removed, secondHandler),
                "null restores the default and returns the second handler, as the same instance");

            Action<QtMsgType, IntPtr, string> removedAgain = Qt.QInstallMessageHandler(null);
            context.Check(defaultHandler == null
                    ? removedAgain == null
                    : ReferenceEquals(removedAgain, defaultHandler),
                "restoring the default twice returns the same default wrapper (or null)");
        }
        finally
        {
            // Leave the process with Qt's default handler whatever the assertions above did.
            Qt.QInstallMessageHandler(null);
        }

        return context.ExitCode;
    }

    // Qt6Core is already loaded (the glue links it), so the platform's loader finds it by name.
    // The candidates cover the name spellings of the Windows and Unix runtimes; a name that does
    // not exist resolves to a null pointer, so this needs no OS check of its own.
    static QFunctionPointer ResolveQVersion(out string library)
    {
        string baseDirectory = AppContext.BaseDirectory;
        string[] candidates =
        {
            "Qt6Core",
            Path.Combine(baseDirectory, "Qt6Core.dll"),
            "libQt6Core.so.6",
            Path.Combine(baseDirectory, "lib", "libQt6Core.so.6"),
        };

        foreach (string candidate in candidates)
        {
            QFunctionPointer address = QLibrary.Resolve(candidate, "qVersion");
            if (!address.IsNull)
            {
                library = candidate;
                return address;
            }
        }

        library = string.Join("', '", candidates);
        return default;
    }
}
