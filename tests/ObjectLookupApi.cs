using System;
using System.Linq;
using QtSharp.Qt3DCore;

// The PyQt/PySide object model: a wrapper is created as the object's actual bound type, resolved
// once from the native meta-object, so `is`/`as` work on every path that hands a QObject back -
// a QObject* return, a QList<QObject*> element, a signal argument, a parent/child accessor.
//
// The tests deliberately create the "no cached wrapper yet" situation in the supported way: an
// object whose wrapper was never made (an internal child Qt built itself), or a wrapper whose
// native object is parented and then disposed (the wrapper leaves the identity cache while Qt
// keeps the object). They never free an owner to force a lookup - the ownership test owns that.
//
// The issue's FindChild<T>/FindChildren<T> do not exist in this tree, so the "unpublished internal
// child resolves to the nearest bound base" case is covered through QComboBox.View() and
// Children(), which use the same resolution path FindChild would.
[Test("test-objectlookup")]
public static class ObjectLookupApi
{
    public static int Run(TestContext context)
    {
        // ---------------------------------------------------------------- QList<QObject*> elements
        // QSpinBox builds its editor itself and nothing in the binding ever wrapped it. Children()
        // therefore has to resolve it through the meta-object; a second call has to hand back the
        // same wrapper (the identity cache), not a fresh base view.
        var spin = new QSpinBox();
        var children = spin.Children();
        var editor = children.OfType<QLineEdit>().FirstOrDefault();
        context.Check(editor != null,
            $"QSpinBox.Children() returns the internal editor as a QLineEdit (got {Describe(children)})");
        context.Check(editor != null && ReferenceEquals(editor, spin.Children().OfType<QLineEdit>().FirstOrDefault()),
            "the resolved editor is the same wrapper instance on every Children() call");
        context.Check(editor != null && ReferenceEquals(editor.Parent(), spin),
            "the editor's Parent() is the QSpinBox wrapper that owns it");
        var childrenAgain = spin.Children();
        context.Check(children.Length == childrenAgain.Length &&
              children.Zip(childrenAgain, (a, b) => ReferenceEquals(a, b)).All(same => same),
            "every QList<QObject*> element is identity-stable between two reads");

        // ------------------------------------------------------------------------ FromHandle
        var host = new QWidget();
        var button = new QPushButton("handled", host);
        context.Check(ReferenceEquals(QObject.FromHandle(button.Handle), button),
            "FromHandle returns the compatible wrapper that already exists");
        IntPtr buttonHandle = button.Handle;
        button.Dispose();   // parented to host: Qt keeps the object, the wrapper leaves the cache
        var lookedUp = QObject.FromHandle(buttonHandle);
        context.Check(lookedUp is QPushButton,
            $"QObject.FromHandle on a QPushButton handle resolves QPushButton (got {lookedUp?.GetType().Name})");
        context.Check(lookedUp is QPushButton pressed && pressed.text() == "handled",
            "and the resolved wrapper is usable as its actual type");
        context.Check(ReferenceEquals(lookedUp, QObject.FromHandle(buttonHandle)),
            "the resolved wrapper is registered, so the next lookup returns it");

        // ------------------------------------------------------- base-typed accessors (cache hit)
        var windowButton = new QPushButton("window");
        var childButton = new QPushButton("child", windowButton);
        context.Check(childButton.Window() is QPushButton,
            $"QWidget.Window() on a QPushButton child returns the QPushButton wrapper (got {childButton.Window()?.GetType().Name})");
        context.Check(ReferenceEquals(childButton.Window(), windowButton),
            "and it is the very wrapper the program built");
        context.Check(childButton.Parent() is QPushButton && ReferenceEquals(childButton.Parent(), windowButton),
            "QObject.Parent() likewise returns the QPushButton wrapper");

        // -------------------------------------------------- QObject.Parent() through the walk
        // The parent's wrapper is gone (its native object is parented to the host, so Dispose only
        // detaches the wrapper). Parent() now has to find the type in the meta-object chain.
        var parentHost = new QWidget();
        var nativeParent = new QPushButton("walk-parent", parentHost);
        var leaf = new QPushButton("walk-leaf", nativeParent);
        IntPtr parentHandle = nativeParent.Handle;
        nativeParent.Dispose();
        var parentView = leaf.Parent();
        context.Check(parentView is QPushButton,
            $"QObject.Parent() resolves a QPushButton with no cached wrapper (got {parentView?.GetType().Name})");
        context.Check(parentView is QPushButton parentButton && parentButton.text() == "walk-parent",
            "and the resolved wrapper exposes the parent's state");
        context.Check(ReferenceEquals(parentView, leaf.Parent()),
            "the wrapper resolved by the walk is then identity-cached");

        // -------------------------------------------------------------- signal arguments
        // The button's own wrapper is detached from the identity cache (its native object stays
        // parented to the host), so both the base-typed lookup and the signal argument have to
        // resolve the type from the meta-object.
        var signalHost = new QWidget();
        var signalButton = new QPushButton("signal-button", signalHost);
        var group = new QButtonGroup();
        group.AddButton(signalButton);
        QAbstractButton received = null;
        group.ButtonClicked += sender => received = sender;
        IntPtr signalHandle = signalButton.Handle;
        ApiRuntime.Forget(signalHandle, signalButton);
        var clicker = QAbstractButton.FromHandle(signalHandle);
        context.Check(clicker is QPushButton,
            $"a base-typed FromHandle resolves the QPushButton (got {clicker?.GetType().Name})");
        ApiRuntime.Forget(signalHandle, clicker);   // the signal handler has to resolve it itself
        clicker?.Click();
        context.Check(received is QPushButton,
            $"a QObject* signal argument arrives as QPushButton (got {received?.GetType().Name})");
        context.Check(received is QPushButton receivedButton && receivedButton.text() == "signal-button",
            "and the signal's wrapper is usable as its actual type");
        context.Check(!ReferenceEquals(received, clicker) && ReferenceEquals(received, QAbstractButton.FromHandle(signalHandle)),
            "the signal argument's wrapper was resolved and is now identity-cached");

        // ---------------------------------------------------------------- managed subclass
        var container = new QWidget();
        var managed = new ManagedWidget();
        managed.SetParent(container);
        context.Check(managed.GetType() == typeof(ManagedWidget) && managed.IsAlive,
            "the managed subclass wrapper is still its own type after being parented");
        context.Check(ReferenceEquals(QObject.FromHandle(managed.Handle), managed),
            "FromHandle keeps a live managed subclass wrapper (cache-first rule)");
        context.Check(container.Children().Any(child => ReferenceEquals(child, managed)),
            "Children() hands the managed wrapper itself back, not a QWidget view");
        context.Check(ReferenceEquals(managed.Parent(), container),
            "Parent() of the managed wrapper is the container");

        // ------------------------------------------------------------- unpublished internal type
        // QComboBox's view is a private QComboBoxListView that the binding does not publish. The
        // walk has to skip it and stop at the nearest bound base.
        var combo = new QComboBox();
        var view = combo.View();
        context.Check(view is QListView,
            $"an unpublished internal child resolves to the nearest bound base (got {view?.GetType().Name})");
        context.Check(view != null && ReferenceEquals(view, combo.View()),
            "and that resolution is identity-cached too");

        // ------------------------------------------------------------------ namespaced Qt3D class
        // Qt3DCore's classes register under Qt's qualified name; the parent's wrapper is detached
        // (its native object is owned by the root) so Parent() has to consult the registry.
        var rootEntity = new QEntity();
        var parentEntity = new QEntity(rootEntity);
        var childEntity = new QEntity(parentEntity);
        IntPtr entityHandle = parentEntity.Handle;
        parentEntity.Dispose();     // owned by rootEntity: the native node stays alive
        var resolvedEntity = QObject.FromHandle(entityHandle);
        context.Check(resolvedEntity is QEntity,
            $"a namespaced Qt3DCore::QEntity resolves through its registered name (got {resolvedEntity?.GetType().Name})");
        context.Check(ReferenceEquals(resolvedEntity, childEntity.Parent()),
            "Parent() of the child entity returns the same resolved QEntity wrapper");
        rootEntity.Dispose();       // Qt deletes the entity tree

        // ------------------------------------------------------------------- secondary bases
        var paintButton = new QPushButton("paint");
        context.Check(paintButton.AsQPaintDevice() != null,
            "the AsQPaintDevice() secondary-base view is unaffected by actual-type wrapping");
        paintButton.Dispose();

        // ---------------------------------------------------------------- findChild/findChildren
        // The lookup is Qt's tree walk on the actual wrapper types; no separate type registry or
        // qobject_cast helper is involved.
        var lookupRoot = new QWidget();
        var directButton = new QPushButton("direct", lookupRoot) { ObjectName = "target" };
        var nestedParent = new QWidget(lookupRoot);
        var nestedButton = new QPushButton("nested", nestedParent) { ObjectName = "nested-target" };
        var label = new QLabel("label", lookupRoot) { ObjectName = "target-label" };

        context.Check(ReferenceEquals(lookupRoot.FindChild<QPushButton>(), directButton),
            "FindChild<T> returns the first matching direct child");
        context.Check(ReferenceEquals(lookupRoot.FindChild<QPushButton>("nested-target"), nestedButton),
            "FindChild<T>(name) descends when the direct children do not match");
        context.Check(lookupRoot.FindChild<QPushButton>("nested-target", Qt.FindChildOptions.FindDirectChildrenOnly) == null,
            "FindDirectChildrenOnly does not descend");
        context.Check(ReferenceEquals(lookupRoot.FindChild<QPushButton>(), directButton),
            "FindChild<T> checks every direct child before descending");

        var foundButtons = lookupRoot.FindChildren<QPushButton>();
        context.Check(foundButtons.Length == 2
              && ReferenceEquals(foundButtons[0], directButton)
              && ReferenceEquals(foundButtons[1], nestedButton),
            "FindChildren<T> is a pre-order walk (direct child before its subtree)");
        context.Check(lookupRoot.FindChildren<QPushButton>("target").Length == 1,
            "the name filter is applied to every candidate");
        context.Check(lookupRoot.FindChildren<QPushButton>().Length == 2,
            "FindChildren<T> sees the nested child");

        using (var pattern = new QRegularExpression("^target"))
            context.Check(lookupRoot.FindChildren<QPushButton>(pattern).Length == 1,
                "the regular-expression overload matches objectName");
        using (var pattern = new QRegularExpression("target"))
            context.Check(lookupRoot.FindChildren<QPushButton>(pattern).Length == 2,
                "an unanchored regular expression matches both objectNames");

        context.Check(ReferenceEquals(lookupRoot.FindChild(typeof(QPushButton), "nested-target"), nestedButton),
            "FindChild(Type, name) uses the runtime type spelling");
        context.Check(ReferenceEquals(
                lookupRoot.FindChild(new[] { typeof(QLabel), typeof(QPushButton) }, "nested-target"),
                nestedButton),
            "FindChild(Type[]) matches any of the requested types");
        context.Check(lookupRoot.FindChildren(typeof(QPushButton)).Length == 2
              && lookupRoot.FindChildren(new[] { typeof(QLabel), typeof(QPushButton) }).Length == 3,
            "FindChildren(Type/Type[]) count every matching child");

        // An unbound internal child resolves to its nearest bound base, so a findChild for that
        // base still sees it.
        var lookupCombo = new QComboBox();
        lookupCombo.View();
        context.Check(lookupCombo.FindChild<QListView>() != null,
            "FindChild<QListView> finds QComboBox's internal view as its bound base");
        lookupCombo.Dispose();

        // A managed subclass keeps its own type, and is still a QWidget for a base-typed lookup.
        var lookupManagedRoot = new QWidget();
        var lookupManaged = new ManagedWidget { ObjectName = "managed" };
        lookupManaged.SetParent(lookupManagedRoot);
        context.Check(ReferenceEquals(lookupManagedRoot.FindChild<ManagedWidget>("managed"), lookupManaged),
            "FindChild<T> returns the managed subclass wrapper itself");
        context.Check(ReferenceEquals(lookupManagedRoot.FindChild<QWidget>("managed"), lookupManaged),
            "and the same object is found through its Qt base type");
        lookupManagedRoot.Dispose();

        lookupRoot.Dispose();

        return context.ExitCode;
    }

    static string Describe(QObject[] objects)
        => objects == null ? "null" : string.Join(", ", objects.Select(o => o?.GetType().Name ?? "null"));

    // A QObject subclass declared in C# has no native class name in the registry; an existing
    // wrapper has to win or the type would be lost.
    sealed class ManagedWidget : QWidget
    {
    }
}
