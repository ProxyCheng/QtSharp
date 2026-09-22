using System;
using System.Collections.Generic;
using QtSharp.Api;

// Port of the official "Shortcut Editor" example
// (examples/widgets/widgets/shortcuteditor) onto the new pipeline.
//
// This is the demo the *legacy* port kept crashing on, and the reason is structural: the tree is
// driven by a model written in C#, and a *hierarchical* model has to build its own indexes. Qt
// declares QAbstractItemModel::createIndex (and beginInsertRows/endResetModel/...) protected, so
// the binding publishes them through the generated native subclass - without that a managed tree
// model cannot exist at all, and the view walks off the end of an index it built itself.

// The official example keeps the action registry inside a QApplication subclass; a process-wide
// singleton is the C# equivalent.
public sealed class ShortcutRegistry
{
    public static readonly ShortcutRegistry Instance = new();

    readonly List<QAction> actions = new();
    readonly Dictionary<QAction, string> contexts = new();
    readonly Dictionary<QAction, string> categories = new();

    ShortcutRegistry() { }

    public IReadOnlyList<QAction> Actions => actions;

    public string ContextOf(QAction action) => contexts[action];

    public string CategoryOf(QAction action) => categories[action];

    public QAction Register(string name, string shortcut, string context, string category)
    {
        var action = new QAction(name);
        using (var sequence = new QKeySequence(shortcut))
            action.SetShortcut(sequence);
        contexts[action] = context;
        categories[action] = category;
        actions.Add(action);
        return action;
    }
}

public sealed class ShortcutTreeModel : QAbstractItemModel
{
    sealed class Node
    {
        public readonly int Id;
        public readonly string Name;
        public readonly QAction Action;
        public readonly Node ParentNode;
        public readonly List<Node> Children = new();

        public Node(int id, string name, QAction action, Node parent)
        {
            Id = id;
            Name = name;
            Action = action;
            ParentNode = parent;
            parent?.Children.Add(this);
        }

        public int Row => ParentNode == null ? 0 : ParentNode.Children.IndexOf(this);
    }

    const int DisplayRole = (int)Qt.ItemDataRole.DisplayRole;
    const int EditRole = (int)Qt.ItemDataRole.EditRole;

    readonly List<Node> nodes = new();
    Node root;

    public ShortcutTreeModel() : base() { }

    Node NodeAt(QModelIndex index)
    {
        if (index == null || !index.IsValid())
            return null;
        ulong id = index.InternalId();
        return id < (ulong)nodes.Count ? nodes[(int)id] : null;
    }

    Node NewNode(string name, QAction action, Node parent)
    {
        var node = new Node(nodes.Count, name, action, parent);
        nodes.Add(node);
        return node;
    }

    /// <summary>Rebuilds the tree from the registry (the official example's setActions()).</summary>
    public void SetActions()
    {
        BeginResetModel();
        Populate();
        EndResetModel();
    }

    void Populate()
    {
        nodes.Clear();
        root = NewNode(null, null, null);

        var byContext = new SortedDictionary<string, SortedDictionary<string, List<QAction>>>(StringComparer.Ordinal);
        foreach (var action in ShortcutRegistry.Instance.Actions)
        {
            string context = ShortcutRegistry.Instance.ContextOf(action);
            string category = ShortcutRegistry.Instance.CategoryOf(action);
            if (!byContext.TryGetValue(context, out var categories))
                byContext[context] = categories = new SortedDictionary<string, List<QAction>>(StringComparer.Ordinal);
            if (!categories.TryGetValue(category, out var list))
                categories[category] = list = new List<QAction>();
            list.Add(action);
        }

        foreach (var context in byContext)
        {
            var contextNode = NewNode(context.Key, null, root);
            foreach (var category in context.Value)
            {
                var categoryNode = NewNode(category.Key, null, contextNode);
                foreach (var action in category.Value)
                {
                    string name = action.Text;
                    if (string.IsNullOrEmpty(name))
                        continue;
                    NewNode(name, action, categoryNode);
                }
            }
        }
    }

    /// <summary>
    /// Adds an action to an existing category, announcing the change the way a model has to:
    /// beginInsertRows/endInsertRows around the mutation. Those are protected in C++ too, and the
    /// view only learns about the new row through them.
    /// </summary>
    public QModelIndex AddAction(string context, string category, string name, string shortcut)
    {
        var action = ShortcutRegistry.Instance.Register(name, shortcut, context, category);

        var contextNode = root.Children.Find(n => n.Name == context);
        if (contextNode == null)
            return null;
        var categoryNode = contextNode.Children.Find(n => n.Name == category);
        if (categoryNode == null)
            return null;

        var parentIndex = CreateIndex(categoryNode.Row, 0, (ulong)categoryNode.Id);
        int row = categoryNode.Children.Count;
        BeginInsertRows(parentIndex, row, row);
        NewNode(name, action, categoryNode);
        EndInsertRows();

        return CreateIndex(row, 1, (ulong)categoryNode.Children[row].Id);
    }

    /// <summary>The action a row stands for (null for a context or category row).</summary>
    public QAction ActionAt(QModelIndex index) => NodeAt(index)?.Action;

    public override QModelIndex Index(int row, int column, QModelIndex parent = null)
    {
        if (row < 0 || column < 0 || column >= ColumnCount(parent))
            return new QModelIndex();

        var parentNode = parent == null || !parent.IsValid() ? root : NodeAt(parent);
        if (parentNode == null || row < 0 || row >= parentNode.Children.Count)
            return new QModelIndex();

        return CreateIndex(row, column, (ulong)parentNode.Children[row].Id);
    }

    public override QModelIndex Parent(QModelIndex child)
    {
        if (child == null || !child.IsValid())
            return new QModelIndex();

        var parentNode = NodeAt(child)?.ParentNode;
        if (parentNode == null || parentNode == root)
            return new QModelIndex();

        return CreateIndex(parentNode.Row, 0, (ulong)parentNode.Id);
    }

    public override int RowCount(QModelIndex parent = null)
    {
        if (parent != null && parent.IsValid() && parent.Column() > 0)
            return 0;
        var node = parent == null || !parent.IsValid() ? root : NodeAt(parent);
        return node?.Children.Count ?? 0;
    }

    public override int ColumnCount(QModelIndex parent = null) => 2;

    public override QVariant Data(QModelIndex index, int role = DisplayRole)
    {
        if (index == null || !index.IsValid() || (role != DisplayRole && role != EditRole))
            return new QVariant();

        var node = NodeAt(index);
        if (node == null)
            return new QVariant();

        if (index.Column() == 0)
            return new QVariant(node.Name);
        if (index.Column() == 1 && node.Action != null)
            return new QVariant(node.Action.Shortcut.ToString());
        return new QVariant();
    }

    public override QVariant HeaderData(int section, Qt.Orientation orientation, int role = DisplayRole)
    {
        if (orientation == Qt.Orientation.Horizontal && role == DisplayRole)
            return new QVariant(section == 0 ? "Name" : "Shortcut");
        return new QVariant();
    }

    public override Qt.ItemFlags Flags(QModelIndex index)
    {
        if (index == null || !index.IsValid())
            return Qt.ItemFlags.NoItemFlags;

        var flags = Qt.ItemFlags.ItemIsEnabled | Qt.ItemFlags.ItemIsSelectable;
        if (index.Column() == 1)
            flags |= Qt.ItemFlags.ItemIsEditable;
        return flags;
    }

    public override bool SetData(QModelIndex index, QVariant value, int role = EditRole)
    {
        if (index == null || !index.IsValid() || role != EditRole || index.Column() != 1)
            return false;

        var action = NodeAt(index)?.Action;
        if (action == null)
            return false;

        string text = value.ToString();
        if (text == action.Shortcut.ToString())
            return true;

        using (var sequence = new QKeySequence(text))
            action.SetShortcut(sequence);
        DataChanged(index, index);
        return true;
    }
}

public sealed class ShortcutItemDelegate : QStyledItemDelegate
{
    /// <summary>The editor a sheet is currently being edited with, kept alive for the view.</summary>
    public readonly List<QWidget> Editors = new();

    /// <summary>How often the view asked for an editor's content, and what it handed over.</summary>
    public int SetEditorDataCalls;
    public QWidget LastEditor;

    public ShortcutItemDelegate(QObject parent = null) : base(parent) { }

    public override QWidget CreateEditor(QWidget parent, QStyleOptionViewItem option, QModelIndex index)
    {
        var editor = new QKeySequenceEdit(parent);
        Editors.Add(editor);
        // The official example commits on editingFinished. A binding needs an *emit* path for
        // that (commitData/closeEditor are signals of the delegate); until that exists the view's
        // own focus-out handling commits through setModelData below, which is the same contract.
        return editor;
    }

    public override void SetEditorData(QWidget editor, QModelIndex index)
    {
        SetEditorDataCalls++;
        LastEditor = editor;
        if (editor == null || index == null || !index.IsValid())
            return;
        string value = index.Model().Data(index, (int)Qt.ItemDataRole.EditRole).ToString();
        using (var sequence = new QKeySequence(value))
            ((QKeySequenceEdit)editor).SetKeySequence(sequence);
    }

    public override void SetModelData(QWidget editor, QAbstractItemModel model, QModelIndex index)
    {
        if (editor == null || model == null || index == null || !index.IsValid())
            return;
        model.SetData(index, new QVariant(((QKeySequenceEdit)editor).KeySequence.ToString()),
                      (int)Qt.ItemDataRole.EditRole);
    }

    public override void UpdateEditorGeometry(QWidget editor, QStyleOptionViewItem option, QModelIndex index)
    {
        if (editor != null && option != null)
            editor.Geometry = option.Rect;
    }
}

public sealed class ShortcutEditorWidgetApi : QWidget
{
    public readonly ShortcutTreeModel Model;
    public readonly ShortcutItemDelegate Delegate;
    public readonly QTreeView View;

    public ShortcutEditorWidgetApi(QWidget parent = null) : base(parent)
    {
        Model = new ShortcutTreeModel();
        Delegate = new ShortcutItemDelegate(this);
        View = new QTreeView(this);
        View.SetModel(Model);
        View.SetItemDelegateForColumn(1, Delegate);
        View.SetSizeAdjustPolicy(QAbstractScrollArea.SizeAdjustPolicy.AdjustToContents);
        View.AllColumnsShowFocus = true;
        View.Header().ResizeSection(0, 250);

        var layout = new QVBoxLayout();
        layout.SetContentsMargins(0, 0, 0, 0);
        layout.AddWidget(View);
        SetLayout(layout);

        Model.SetActions();
    }
}

public sealed class ShortcutWindowApi : QMainWindow
{
    public readonly QPushButton TopButton = new QPushButton("Left");
    public readonly QPushButton BottomButton = new QPushButton("Right");
    public readonly ShortcutEditorWidgetApi Editor;

    public ShortcutWindowApi() : base()
    {
        // The registry is filled *before* the editor builds its tree from it. The official example
        // registers every action inside its QApplication subclass, which is constructed before any
        // window exists, so its model always sees a complete registry.
        foreach (var entry in new[]
        {
            new[] { "red", "r", "shift+r" },
            new[] { "green", "g", "shift+g" },
            new[] { "blue", "b", "shift+b" },
        })
        {
            string name = entry[0];
            var topAction = ShortcutRegistry.Instance.Register(name, entry[1], "top", "color");
            TopButton.AddAction(topAction);
            topAction.TriggeredSignal.Connect(_ => TopButton.SetText(name));

            var bottomAction = ShortcutRegistry.Instance.Register(name, entry[2], "bottom", "color");
            BottomButton.AddAction(bottomAction);
            bottomAction.TriggeredSignal.Connect(_ => BottomButton.SetText(name));
        }

        Editor = new ShortcutEditorWidgetApi();

        var buttons = new QVBoxLayout();
        buttons.AddWidget(TopButton);
        buttons.AddWidget(BottomButton);

        var row = new QHBoxLayout();
        row.AddWidget(Editor);
        row.AddLayout(buttons);

        var central = new QWidget();
        central.SetLayout(row);
        SetCentralWidget(central);

        SetWindowTitle("Shortcut Editor");
        Resize(720, 420);
    }
}

public static class ApiShortcutDemo
{
    public static int Run(QApplication app)
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        var window = new ShortcutWindowApi();
        window.Show();

        var model = window.Editor.Model;
        var view = window.Editor.View;
        var registry = ShortcutRegistry.Instance;

        Check(registry.Actions.Count == 6, $"the registry holds six actions (got {registry.Actions.Count})");

        // The tree shape: two contexts, one category each, three actions per category. Every one of
        // those counts comes out of a QModelIndex the *managed* model built with createIndex.
        var topIndex = model.Index(0, 0);
        Check(model.RowCount() == 2 && topIndex.IsValid(),
            $"the root has two contexts (got {model.RowCount()})");

        string firstContext = model.Data(topIndex, 0).ToString();
        var categoryIndex = model.Index(0, 0, topIndex);
        Check((firstContext == "bottom" || firstContext == "top") && model.Data(categoryIndex, 0).ToString() == "color",
            $"context row -> category row (got '{firstContext}' -> '{model.Data(categoryIndex, 0)}')");
        Check(model.RowCount(categoryIndex) == 3, $"the category holds three actions (got {model.RowCount(categoryIndex)})");

        var nameIndex = model.Index(0, 0, categoryIndex);
        var actionIndex = model.Index(0, 1, categoryIndex);
        // Data(index, role): the second argument is the role, so the two columns come from two
        // indexes rather than two roles.
        string actionName = model.Data(nameIndex, (int)Qt.ItemDataRole.DisplayRole).ToString();
        string actionShortcut = model.Data(actionIndex, (int)Qt.ItemDataRole.DisplayRole).ToString();
        Check(actionName.Length > 0 && actionShortcut.Length > 0,
            $"an action row carries a name and a shortcut ('{actionName}' / '{actionShortcut}')");

        // The index really is the one the model handed out: it round-trips to the same node and
        // walks back to its parent (this is the part that used to crash).
        var parentOfAction = model.Parent(actionIndex);
        Check(parentOfAction.IsValid() && parentOfAction.Row() == categoryIndex.Row()
              && parentOfAction.InternalId() == categoryIndex.InternalId(),
            "an index walks back to its parent through the id the model stored in it");
        Check(actionIndex.InternalId() >= 0 && actionIndex.Row() == 0,
            $"the index carries the model's own id (id={actionIndex.InternalId()}, row={actionIndex.Row()})");

        // The crash the user hit: expanding the "color" node and letting Qt paint it. Expanding
        // makes the view ask the model for the child indexes and the delegate for a size hint, and
        // painting goes through the delegate as well.
        view.ExpandAll();
        view.SetCurrentIndex(actionIndex);
        int painted = view.SizeHintForColumn(0);
        using (var rendered = view.Grab())
        {
            Check(!rendered.IsNull() && rendered.Width() > 0,
                $"the tree renders with a node expanded (got {rendered.Width()}x{rendered.Height()}, column hint {painted})");
        }
        Check(model.RowCount(categoryIndex) == 3
              && model.Data(actionIndex, (int)Qt.ItemDataRole.DisplayRole).ToString().Length > 0,
            "the model still answers after the expansion");

        // The delegate's contract, driven directly: it has to hand the model's value to the editor
        // and write the editor's value back.
        var delegateInstance = window.Editor.Delegate;
        using (var option = new QStyleOptionViewItem())
        {
            var editor = delegateInstance.CreateEditor(view, option, actionIndex);
            Check(editor is QKeySequenceEdit, $"the delegate built its editor (got {editor.GetType().Name})");

            delegateInstance.UpdateEditorGeometry(editor, option, actionIndex);
            delegateInstance.SetEditorData(editor, actionIndex);
            string before = ((QKeySequenceEdit)editor).KeySequence.ToString();
            Check(before.Length > 0, $"the editor was seeded from the model (got '{before}')");

            using (var replacement = new QKeySequence("Ctrl+Shift+R"))
                ((QKeySequenceEdit)editor).SetKeySequence(replacement);
            delegateInstance.SetModelData(editor, model, actionIndex);

            string after = model.Data(actionIndex, (int)Qt.ItemDataRole.DisplayRole).ToString();
            Check(after == "Ctrl+Shift+R", $"the editor's value reached the model (got '{after}')");
            var editedAction = model.ActionAt(actionIndex);
            Check(editedAction != null && editedAction.Shortcut.ToString() == "Ctrl+Shift+R",
                $"and the model wrote it through to the action itself (got '{editedAction?.Shortcut.ToString()}')");
        }

        // The path a double-click takes: Qt opens the editor itself, so setEditorData() is called
        // by the view with a QWidget* the delegate never saw. That pointer has to come back as the
        // very QKeySequenceEdit the delegate created (the identity cache) - otherwise the cast
        // inside setEditorData throws, which is exactly what a real double-click in this window
        // exposed before the wrapper rule was fixed.
        delegateInstance.LastEditor = null;
        view.ExpandAll();
        view.Edit(actionIndex);
        Check(delegateInstance.LastEditor is QKeySequenceEdit,
            $"a view-driven edit hands the delegate back its own QKeySequenceEdit "
            + $"(got {delegateInstance.LastEditor?.GetType().Name ?? "nothing"})");

        // A structural change announced the way a model has to announce one - the protected
        // beginInsertRows/endInsertRows the binding now publishes.
        int rowsBefore = model.RowCount(categoryIndex);
        var added = model.AddAction(firstContext, "color", "yellow", "y");
        Check(added != null && model.RowCount(categoryIndex) == rowsBefore + 1,
            $"inserting a row through beginInsertRows/endInsertRows (got {model.RowCount(categoryIndex)} rows)");
        var addedName = model.Index(rowsBefore, 0, categoryIndex);
        // Qt normalises a single-letter sequence to upper case ("y" comes back as "Y"), which is
        // what the view shows.
        Check(model.Data(addedName, (int)Qt.ItemDataRole.DisplayRole).ToString() == "yellow"
              && model.Data(added, (int)Qt.ItemDataRole.DisplayRole).ToString() == "Y",
            $"the inserted row is addressable by its new index ('{model.Data(addedName, 0).ToString()}'"
            + $" / '{model.Data(added, 0).ToString()}')");

        view.ExpandAll();
        view.SetCurrentIndex(added);
        using (var rendered = view.Grab())
            Check(!rendered.IsNull(), "the view renders after the insertion");

        // The failure mode the legacy demo had: a full collection while the view holds the model
        // and the delegate. Everything the managed side owns is reachable through the widget, so
        // nothing here may be freed behind Qt's back.
        for (int round = 0; round < 3; round++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        view.ExpandAll();
        Check(model.RowCount(categoryIndex) == rowsBefore + 1
              && model.Data(added, (int)Qt.ItemDataRole.DisplayRole).ToString() == "Y"
              && view.Model().RowCount(categoryIndex) == rowsBefore + 1,
            "the model, the delegate and the view survive repeated full GCs");
        using (var rendered = view.Grab())
            Check(!rendered.IsNull(), "and the tree still renders afterwards");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");

        // Then leave the window up so it can be *used*: expand the nodes, edit a shortcut. The
        // default is short enough for the gate; QTSHARP_DEMO_MS=60000 to play with it properly
        // (the official example's own interactions: open a context, then a category, then edit).
        var stop = new QTimer();
        stop.SetSingleShot(true);
        stop.TimeoutSignal.Connect(() => app.Quit());
        int runFor = int.TryParse(Environment.GetEnvironmentVariable("QTSHARP_DEMO_MS"), out var ms) ? ms : 1500;
        stop.Start(runFor);
        app.Exec();

        return failures == 0 ? 0 : 1;
    }
}
