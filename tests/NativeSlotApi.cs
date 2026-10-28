using System;
using System.Linq;
using System.Reflection;

// The read-side marker for a method moc already registered in the meta-object: a Q_SLOTS/Q_SLOT
// slot (QMetaMethod::Slot) or a Q_INVOKABLE method (QMetaMethod::Method). Like [QtNativeProperty]
// and [QtNativeSignal] it describes a member Qt generated, so it is never a declaration and never
// reaches AttachMetaObject; [QtSlot] is what a C# subclass uses to add one of its own.
[Test("test-nativeslot")]
public static class NativeSlotApi
{
    static MethodInfo Find(Type type, string name, int parameterCount)
        => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                           BindingFlags.DeclaredOnly)
            .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == parameterCount);

    static QtNativeSlotAttribute Marker(Type type, string name, int parameterCount)
        => Find(type, name, parameterCount)?.GetCustomAttribute<QtNativeSlotAttribute>();

    public static int Run(TestContext context)
    {
        // A Q_SLOTS declaration: qlabel.h has setText(const QString &) inside `public Q_SLOTS:`.
        var setText = Marker(typeof(QLabel), "SetText", 1);
        context.Check(setText != null && !setText.Invokable && setText.Signature == "setText(QString)",
            "a Q_SLOTS method carries [QtNativeSlot] (not invokable, setText(QString))");

        // Q_INVOKABLE: qwidget.h declares grab(const QRect &) as an invokable. The model folds the
        // default argument into the one managed overload, so it is QRect? at the call site.
        var grab = Marker(typeof(QWidget), "Grab", 1);
        context.Check(grab != null && grab.Invokable && grab.Signature == "grab(QRect)",
            "a Q_INVOKABLE method carries [QtNativeSlot(..., invokable: true)] (grab(QRect))");

        // Q_INVOKABLE on a QAbstractItemModel method (hasIndex) - a different class, a different
        // header, and an enum-typed parameter in the signature.
        var hasIndex = Marker(typeof(QAbstractItemModel), "HasIndex", 3);
        context.Check(hasIndex != null && hasIndex.Invokable && hasIndex.Signature == "hasIndex(int,int,QModelIndex)",
            "an inherited-model invokable is marked with its normalized Qt signature");

        // A plain callable member: QLabel::text() is the property getter, with no slot declaration
        // anywhere in its header.
        context.Check(Marker(typeof(QLabel), "Text", 0) == null,
            "an ordinary method carries no [QtNativeSlot]");

        // Neither does an override point: the virtuals are not in the model's method list.
        context.Check(Marker(typeof(QWidget), "PaintEvent", 1) == null,
            "a virtual override point carries no [QtNativeSlot]");

        // A method is either a generated signal wrapper or a generated method wrapper; the two
        // read-side markers must never describe the same member.
        int marked = 0, invokable = 0;
        bool both = false;
        foreach (var assembly in new[] { typeof(QObject).Assembly, typeof(QGuiApplication).Assembly, typeof(QLabel).Assembly })
        {
            foreach (var type in assembly.GetTypes())
            {
                if (!type.IsClass)
                    continue;
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                       BindingFlags.Instance | BindingFlags.Static |
                                                       BindingFlags.DeclaredOnly))
                {
                    var slot = method.GetCustomAttribute<QtNativeSlotAttribute>();
                    if (slot == null)
                        continue;
                    marked++;
                    if (slot.Invokable)
                        invokable++;
                    if (method.GetCustomAttribute<QtNativeSignalAttribute>() != null)
                        both = true;
                }
            }
        }
        context.Check(marked > 100 && invokable > 0,
            $"the generated Qt modules are full of marked slots/invokables (got {marked}, {invokable} invokable)");
        context.Check(!both, "no method carries both [QtNativeSlot] and [QtNativeSignal]");

        // The signature is Qt's, not the binding's: a real QMetaObject resolves it to the entry moc
        // generated. This is what proves the spelling (QString rather than const QString &, QRect
        // rather than a managed type) without comparing the attribute with itself.
        var label = new QLabel();
        var widget = new QWidget();
        context.Check(label.MetaObject().IndexOfMethod("setText(QString)") >= 0,
            "the [QtNativeSlot] signature of setText resolves in Qt's meta-object");
        context.Check(label.MetaObject().IndexOfMethod("clear()") >= 0,
            "another Q_SLOTS method (QLabel.clear) resolves in Qt's meta-object");
        context.Check(widget.MetaObject().IndexOfMethod("grab(QRect)") >= 0,
            "the Q_INVOKABLE signature of grab resolves in Qt's meta-object");
        context.Check(label.MetaObject().IndexOfMethod("deleteLater()") >= 0,
            "an inherited QObject slot resolves in Qt's meta-object");

        return context.ExitCode;
    }
}
