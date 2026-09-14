using System;
using System.Collections.Generic;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtWidgets;
using Qt = QtSharp.QtCore.Qt;

// Port of the official Qt "Shortcut Editor" example
// (examples/widgets/widgets/shortcuteditor). The example is a read-write
// hierarchical model displayed in a QTreeView where the shortcut column is edited
// with a QKeySequenceEdit through a QStyledItemDelegate.

// The official example keeps the action registry inside a QApplication subclass.
// A process-wide manager is the C# equivalent.
public sealed class ActionManager
{
    public static readonly ActionManager Instance = new ActionManager();

    readonly List<QAction> actions = new();
    readonly Dictionary<QAction, string> contexts = new();
    readonly Dictionary<QAction, string> categories = new();

    ActionManager() { }

    public IReadOnlyList<QAction> RegisteredActions => actions;

    public string ContextForAction(QAction action) => contexts[action];

    public string CategoryForAction(QAction action) => categories[action];

    public QAction RegisterAction(string name, string shortcut, string context, string category)
    {
        var action = new QAction(name);
        action.SetShortcutString(shortcut);
        contexts[action] = context;
        categories[action] = category;
        actions.Add(action);
        return action;
    }
}

public class ShortcutEditorModel : QAbstractItemModel
{
    sealed class Item
    {
        public readonly int Id;
        public readonly string Name;
        public readonly QAction Action;
        public readonly Item ParentItem;
        public readonly List<Item> Children = new();

        public Item(int id, string name, QAction action, Item parent)
        {
            Id = id;
            Name = name;
            Action = action;
            ParentItem = parent;
            parent?.Children.Add(this);
        }

        public int Row => ParentItem == null ? 0 : ParentItem.Children.IndexOf(this);
    }

    const int DisplayRole = (int)Qt.ItemDataRole.DisplayRole;
    const int EditRole = (int)Qt.ItemDataRole.EditRole;

    readonly List<Item> items = new();
    Item root;

    public ShortcutEditorModel(QObject parent = null) : base(parent) { }

    Item ItemAt(QModelIndex index)
    {
        if (index == null || !index.IsValid)
            return null;
        long id = (long)index.InternalId;
        return id >= 0 && id < items.Count ? items[(int)id] : null;
    }

    Item NewItem(string name, QAction action, Item parent)
    {
        var item = new Item(items.Count, name, action, parent);
        items.Add(item);
        return item;
    }

    public void SetActions()
    {
        BeginResetModel();
        items.Clear();
        root = NewItem(null, null, null);

        var byContext = new SortedDictionary<string, SortedDictionary<string, List<QAction>>>(StringComparer.Ordinal);
        foreach (var action in ActionManager.Instance.RegisteredActions)
        {
            string context = ActionManager.Instance.ContextForAction(action);
            string category = ActionManager.Instance.CategoryForAction(action);
            if (!byContext.TryGetValue(context, out var categories))
                byContext[context] = categories = new SortedDictionary<string, List<QAction>>(StringComparer.Ordinal);
            if (!categories.TryGetValue(category, out var list))
                categories[category] = list = new List<QAction>();
            list.Add(action);
        }

        foreach (var context in byContext)
        {
            var contextItem = NewItem(context.Key, null, root);
            foreach (var category in context.Value)
            {
                var categoryItem = NewItem(category.Key, null, contextItem);
                foreach (var action in category.Value)
                {
                    string name = action.Text;
                    if (string.IsNullOrEmpty(name))
                        continue;
                    NewItem(name, action, categoryItem);
                }
            }
        }

        EndResetModel();
    }

    public override QModelIndex Index(int row, int column, QModelIndex parent)
    {
        if (!HasIndex(row, column, parent))
            return new QModelIndex();

        var parentItem = !parent.IsValid ? root : ItemAt(parent);
        if (parentItem == null || row < 0 || row >= parentItem.Children.Count)
            return new QModelIndex();

        var child = parentItem.Children[row];
        return CreateIndex(row, column, (ulong)child.Id);
    }

    public override QModelIndex Index(int row, int column) => Index(row, column, new QModelIndex());

    public override QModelIndex parent(QModelIndex child)
    {
        if (!child.IsValid)
            return new QModelIndex();

        var item = ItemAt(child);
        var parentItem = item?.ParentItem;
        if (parentItem == null || parentItem == root)
            return new QModelIndex();

        return CreateIndex(parentItem.Row, 0, (ulong)parentItem.Id);
    }

    public override int RowCount(QModelIndex parent)
    {
        if (parent.IsValid && parent.Column > 0)
            return 0;

        var item = !parent.IsValid ? root : ItemAt(parent);
        return item?.Children.Count ?? 0;
    }

    public override int RowCount() => RowCount(new QModelIndex());

    public override int ColumnCount(QModelIndex parent) => 2;

    public override int ColumnCount() => 2;

    public override QVariant Data(QModelIndex index, int role = DisplayRole)
    {
        if (!index.IsValid || (role != DisplayRole && role != EditRole))
            return new QVariant();

        var item = ItemAt(index);
        if (item == null)
            return new QVariant();

        if (index.Column == 0)
            return new QVariant(item.Name);
        if (index.Column == 1 && item.Action != null)
            return new QVariant(item.Action.ShortcutString());
        return new QVariant();
    }

    public override QVariant HeaderData(int section, Qt.Orientation orientation, int role = DisplayRole)
    {
        if (orientation == Qt.Orientation.Horizontal && role == DisplayRole)
            return new QVariant(section == 0 ? "Name" : "Shortcut");
        return new QVariant();
    }

    public override int Flags(QModelIndex index)
    {
        if (!index.IsValid)
            return (int)Qt.ItemFlag.NoItemFlags;

        int flags = (int)Qt.ItemFlag.ItemIsEnabled | (int)Qt.ItemFlag.ItemIsSelectable;
        if (index.Column == 1)
            flags |= (int)Qt.ItemFlag.ItemIsEditable;
        return flags;
    }

    public override bool SetData(QModelIndex index, QVariant value, int role = EditRole)
    {
        if (role == EditRole && index.Column == 1)
        {
            string keySequence = value.ToString();
            var action = ItemAt(index)?.Action;
            if (action != null)
            {
                if (keySequence == action.ShortcutString())
                    return true;
                action.SetShortcutString(keySequence);
            }

            this.EmitDataChanged(index, index);

            if (string.IsNullOrEmpty(keySequence))
                return true;
        }

        return false;
    }
}

public class ShortcutEditorDelegate : QStyledItemDelegate
{
    readonly List<QWidget> editors = new();

    public ShortcutEditorDelegate(QObject parent = null) : base(parent) { }

    public override QWidget CreateEditor(QWidget parent, QStyleOptionViewItem option, QModelIndex index)
    {
        var editor = new QKeySequenceEdit(parent);
        editors.Add(editor);
        editor.EditingFinishedSignal.Connect(() =>
        {
            CommitData(editor);
            CloseEditor(editor);
        });
        return editor;
    }

    public override void SetEditorData(QWidget editor, QModelIndex index)
    {
        if (editor == null || index == null || !index.IsValid)
            return;

        string value = index.Model.Data(index, (int)Qt.ItemDataRole.EditRole).ToString();
        ((QKeySequenceEdit)editor).SetKeySequenceString(value);
    }

    public override void SetModelData(QWidget editor, QAbstractItemModel model, QModelIndex index)
    {
        if (editor == null || model == null || index == null || !index.IsValid)
            return;

        string keySequence = ((QKeySequenceEdit)editor).KeySequenceString();
        model.SetData(index, new QVariant(keySequence), (int)Qt.ItemDataRole.EditRole);
    }

    public override void UpdateEditorGeometry(QWidget editor, QStyleOptionViewItem option, QModelIndex index)
    {
        if (editor != null && option != null)
            editor.Geometry = option.Rect;
    }

    public override void DestroyEditor(QWidget editor, QModelIndex index)
    {
        editors.Remove(editor);
    }
}

public class ShortcutEditorWidget : QWidget
{
    readonly ShortcutEditorModel model;
    readonly ShortcutEditorDelegate editorDelegate;
    readonly QTreeView view;

    public ShortcutEditorWidget(QWidget parent = null) : base(parent)
    {
        model = new ShortcutEditorModel(this);
        editorDelegate = new ShortcutEditorDelegate(this);
        view = new QTreeView(this);
        view.Model = model;
        view.SetItemDelegateForColumn(1, editorDelegate);
        view.sizeAdjustPolicy = QAbstractScrollArea.SizeAdjustPolicy.AdjustToContents;
        view.AllColumnsShowFocus = true;
        view.Header.ResizeSection(0, 250);

        var layout = new QVBoxLayout();
        layout.SetContentsMargins(0, 0, 0, 0);
        layout.AddWidget(view);
        Layout = layout;

        model.SetActions();
    }
}

public class ShortcutWindow : QMainWindow
{
    readonly List<object> keepAlive = new();

    public ShortcutWindow() : base(null, 0)
    {
        var topPushButton = keep(new QPushButton("Left"));
        var bottomPushButton = keep(new QPushButton("Right"));

        foreach (var nameShortcut in new[]
        {
            new[] { "red", "r", "shift+r" },
            new[] { "green", "g", "shift+g" },
            new[] { "blue", "b", "shift+b" },
        })
        {
            var topAction = ActionManager.Instance.RegisterAction(nameShortcut[0], nameShortcut[1], "top", "color");
            topPushButton.AddAction(topAction);
            topAction.TriggeredSignal.Connect(_ => topPushButton.Text = nameShortcut[0]);

            var bottomAction = ActionManager.Instance.RegisterAction(nameShortcut[0], nameShortcut[2], "bottom", "color");
            bottomPushButton.AddAction(bottomAction);
            bottomAction.TriggeredSignal.Connect(_ => bottomPushButton.Text = nameShortcut[0]);
        }

        var vBoxLayout = new QVBoxLayout();
        vBoxLayout.AddWidget(topPushButton);
        vBoxLayout.AddWidget(bottomPushButton);

        var hBoxLayout = new QHBoxLayout();
        hBoxLayout.AddWidget(keep(new ShortcutEditorWidget()));
        hBoxLayout.AddLayout(vBoxLayout);

        var centralWidget = keep(new QWidget());
        centralWidget.Layout = hBoxLayout;
        CentralWidget = centralWidget;

        WindowTitle = "Shortcut Editor";
        Resize(720, 420);
    }

    T keep<T>(T obj) where T : class
    {
        keepAlive.Add(obj);
        return obj;
    }
}
