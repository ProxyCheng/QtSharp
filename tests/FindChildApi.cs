using System;
using System.Linq;
using QtSharp.QtCore;
using QtSharp.QtWidgets;

// QObject::findChild/findChildren are C++ templates, so the binding supplies them as C# generics
// over the wrapper tree. Because every wrapper is its actual bound type, `is T` is the same test
// Qt's qobject_cast<T*> makes - and it also matches a managed subclass, which a static
// meta-object cast cannot. The order has to stay Qt's: a whole level is tested before the walk
// descends.
[Test("test-findchild")]
public static class FindChildApi
{
    sealed class ManagedWidget : QWidget
    {
    }

    public static int Run(TestContext context)
    {
        using var root = new QWidget();
        root.ObjectName = "root";

        var button = new QPushButton("ok", root) { ObjectName = "ok" };
        var edit = new QLineEdit(root) { ObjectName = "edit" };
        var box = new QWidget(root) { ObjectName = "box" };
        var custom = new ManagedWidget { ObjectName = "custom" };
        custom.SetParent(root);
        var label = new QLabel("deep", box) { ObjectName = "deep" };

        // Type only: the default name matches any object, and the first matching direct child wins.
        context.Check(ReferenceEquals(root.FindChild<QPushButton>(), button),
            "FindChild<T>() without a name returns the first child of that type");

        // Name plus a base type: the name narrows an otherwise ambiguous search.
        context.Check(ReferenceEquals(root.FindChild<QWidget>("edit"), edit),
            "FindChild<T>(name) matches the object name and returns the actual-typed wrapper");

        // The default is recursive, so a grandchild is found.
        context.Check(ReferenceEquals(root.FindChild<QLabel>("deep"), label),
            "the default search descends into grandchildren");

        // ... and FindDirectChildrenOnly stops at the first level.
        context.Check(root.FindChild<QLabel>("deep", Qt.FindChildOptions.FindDirectChildrenOnly) == null,
            "FindDirectChildrenOnly does not see a grandchild");

        // A name that no child has, and a type that does not match the named child.
        context.Check(root.FindChild<QLabel>("missing") == null,
            "a name with no match returns null");
        context.Check(root.FindChild<QPushButton>("edit") == null,
            "a matching name under the wrong type returns null");

        // FindChildren order: direct children in child order, then each subtree (Qt's helper tests
        // a whole level before descending). box's label comes after custom, the last direct child.
        QWidget[] all = root.FindChildren<QWidget>();
        context.Check(all.Length == 5, $"FindChildren<QWidget>() returns the whole subtree ({all.Length})");
        context.Check(all.SequenceEqual(new QObject[] { button, edit, box, custom, label }),
            "the order is Qt's: direct children first, then each child's subtree");

        context.Check(root.FindChildren<QWidget>(null, Qt.FindChildOptions.FindDirectChildrenOnly).Length == 4,
            "FindDirectChildrenOnly returns only the four direct children");
        context.Check(root.FindChildren<QLabel>().Length == 1,
            "a type that occurs once returns one element");
        context.Check(root.FindChildren<QWidget>("").Length == 5,
            "an empty name matches every object, like Qt's null QString");
        context.Check(root.FindChildren<QPushButton>("nothing").Length == 0,
            "no match returns an empty array, not null");

        // A managed subclass is its own type and also a QWidget; both searches see the same wrapper.
        context.Check(ReferenceEquals(root.FindChild<ManagedWidget>(), custom),
            "a managed subclass is found by its own type");
        context.Check(root.FindChild<QWidget>("custom") is ManagedWidget,
            "a search by the base type keeps the managed subclass instance");

        return context.ExitCode;
    }
}
