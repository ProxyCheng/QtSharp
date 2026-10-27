using System;
using System.Collections.Generic;

// The extension points a real application needs: an item model and an item delegate written
// entirely in C#. Nothing here is driven by the test - the assertions read back what *Qt* did, so
// they only pass if the native view really reached the managed overrides:
//
//   view.SetModel(managed model)   -> Qt calls rowCount()/data()/headerData() on it
//   view.SetItemDelegate(managed)  -> Qt calls paint()/sizeHint() while rendering
//
// This is what the whole virtual-dispatch axis was for.
[Demo("test-custommodel")]
public static class ApiCustomModelDemo
{
    sealed class TableModel : QAbstractTableModel
    {
        readonly List<string[]> rows = new()
        {
            new[] { "alpha", "1" },
            new[] { "beta", "2" },
            new[] { "gamma", "3" },
        };

        public int RowCountCalls;
        public int DataCalls;
        public int HeaderDataCalls;
        public int SetDataCalls;
        public string LastRole;

        public override int RowCount(QModelIndex parent = default)
        {
            RowCountCalls++;
            return rows.Count;
        }

        public override int ColumnCount(QModelIndex parent = default) => 2;

        public override object Data(QModelIndex index, int role = default)
        {
            DataCalls++;
            // 0 is Qt::DisplayRole; anything else has no data here.
            if (role != 0 || !index.IsValid())
                return null;
            return rows[index.Row()][index.Column()];
        }

        public override object HeaderData(int section, Qt.Orientation orientation, int role = default)
        {
            HeaderDataCalls++;
            return role == 0 ? $"col {section}" : null;
        }

        public override Qt.ItemFlags Flags(QModelIndex index)
            => Qt.ItemFlags.ItemIsEnabled | Qt.ItemFlags.ItemIsSelectable | Qt.ItemFlags.ItemIsEditable;

        public override bool SetData(QModelIndex index, object value, int role = default)
        {
            SetDataCalls++;
            LastRole = value?.ToString();
            if (!index.IsValid() || role != 2)      // 2 is Qt::EditRole
                return false;
            rows[index.Row()][index.Column()] = LastRole;
            return true;
        }

        // A container return: the managed string[] has to become a native QStringList in the slot
        // Qt provided.
        public override string[] MimeTypes() => new[] { "text/plain" };

        // A container parameter (the model index list) and a pointer return.
        public QModelIndex[] LastMimeIndexes;
        public override QMimeData MimeData(QModelIndex[] indexes)
        {
            LastMimeIndexes = indexes;
            var data = new QMimeData();
            data.SetText("from-managed-model");
            return data;
        }

        // A QHash return, written back element by element.
        public override Dictionary<int, QByteArray> RoleNames()
            => new()
            {
                [0] = new QByteArray("display"),
                [2] = new QByteArray("edit"),
            };
    }

    sealed class CountingDelegate : QStyledItemDelegate
    {
        public int PaintCalls;
        public int SizeHintCalls;

        public override void Paint(QPainter painter, QStyleOptionViewItem option, QModelIndex index)
        {
            PaintCalls++;
            painter.FillRect(option.Rect, new QBrush(new QColor(210, 225, 255)));
            painter.DrawText(option.Rect, 0, index.Data()?.ToString() ?? "");
        }

        public override QSize SizeHint(QStyleOptionViewItem option, QModelIndex index)
        {
            SizeHintCalls++;
            return new QSize(140, 24);
        }
    }

    public static int Run(QApplication app)
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        var model = new TableModel();
        var view = new QTableView();
        view.SetModel(model);
        var itemDelegate = new CountingDelegate();
        view.SetItemDelegate(itemDelegate);
        view.Resize(320, 200);

        // Reaching the model *through Qt*: the view holds a QAbstractItemModel*, so these calls
        // only work if the managed overrides are installed in the native vtable.
        var throughQt = view.Model();
        Check(throughQt != null && throughQt.RowCount() == 3,
            $"the view sees the managed model's row count (got {throughQt?.RowCount()})");
        Check(throughQt.ColumnCount() == 2, "column count through the view");
        Check(throughQt.Data(throughQt.Index(1, 0))?.ToString() == "beta",
            $"the managed model supplied the data (got '{throughQt.Data(throughQt.Index(1, 0))}')");
        Check(throughQt.HeaderData(1, Qt.Orientation.Horizontal)?.ToString() == "col 1",
            $"the managed model supplied the header (got '{throughQt.HeaderData(1, Qt.Orientation.Horizontal)}')");
        Check(model.RowCountCalls >= 2 && model.DataCalls > 0,
            $"Qt called the managed overrides ({model.RowCountCalls} rowCount, {model.DataCalls} data)");

        // A boxed return (QVariant) and an enum-flag return (Qt::ItemFlags) cross the C ABI.
        var flags = throughQt.Flags(throughQt.Index(0, 0));
        Check((flags & Qt.ItemFlags.ItemIsEditable) != 0 && (flags & Qt.ItemFlags.ItemIsEnabled) != 0,
            $"the managed flags came back (got {flags})");

        // A container return: the managed string[] is copied into Qt's QStringList.
        var mimeTypes = throughQt.MimeTypes();
        Check(mimeTypes.Length == 1 && mimeTypes[0] == "text/plain",
            $"the managed mimeTypes() returned (got {(mimeTypes.Length > 0 ? mimeTypes[0] : "<none>")})");

        // A container parameter: Qt hands the override a list, which must be a *copy* (the wrapper
        // owns it, and freeing it would corrupt Qt's own container).
        var indexes = new[] { throughQt.Index(0, 0), throughQt.Index(1, 0) };
        var mimeData = model.MimeData(indexes);
        Check(mimeData != null && mimeData.Text() == "from-managed-model",
            $"the managed mimeData() returned (got '{mimeData?.Text()}')");
        Check(model.LastMimeIndexes != null && model.LastMimeIndexes.Length == 2
              && model.LastMimeIndexes[1].Row() == 1,
            $"the container parameter arrived intact (got {model.LastMimeIndexes?.Length} index(es))");

        // A QHash<int, QByteArray> return.
        var roleNames = throughQt.RoleNames();
        Check(roleNames.Count == 2 && roleNames[0].ConstData() == "display",
            $"the managed roleNames() returned (got {roleNames.Count} role(s))");

        // Editing goes through the model: Qt asks flags(), then calls setData().
        Check(throughQt.SetData(throughQt.Index(0, 0), "renamed", 2),
            "setData reached the managed model");
        Check(model.SetDataCalls == 1 && model.LastRole == "renamed",
            $"the managed model recorded the edit (got '{model.LastRole}')");
        Check(throughQt.Data(throughQt.Index(0, 0))?.ToString() == "renamed",
            "and the model really changed");

        // The real proof for the delegate: render the view and count the managed paint() calls.
        var shot = view.Grab();
        Check(itemDelegate.PaintCalls > 0,
            $"Qt asked the managed delegate to paint ({itemDelegate.PaintCalls} time(s))");
        Check(DemoCatalog.GrabCovers(shot, 320, 200),
            $"the view rendered off-screen (got {shot.Width()}x{shot.Height()})");

        // Asking the view to size its rows is what makes it consult the delegate's sizeHint().
        view.ResizeRowsToContents();
        Check(itemDelegate.SizeHintCalls > 0,
            $"Qt asked the managed delegate for a size hint ({itemDelegate.SizeHintCalls} time(s))");

        // A boxed parameter is a *copy*: the delegate's size hint is its own, and the caller's
        // option object is untouched.
        var option = new QStyleOptionViewItem();
        var hint = itemDelegate.SizeHint(option, throughQt.Index(0, 0));
        Check(hint.Width() == 140 && hint.Height() == 24,
            $"the managed sizeHint reached the caller's slot (got {hint.Width()}x{hint.Height()})");

        // Everything must survive a collection: the wrappers Qt handed the overrides were clones,
        // and the model/delegate are owned by the managed side.
        for (int round = 0; round < 3; round++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Check(view.Model().RowCount() == 3 && throughQt.Data(throughQt.Index(2, 0))?.ToString() == "gamma",
            "the managed model still works after a full GC");
        using (var again = view.Grab())
            Check(DemoCatalog.GrabCovers(again, 320, 200), "and the view still renders");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
