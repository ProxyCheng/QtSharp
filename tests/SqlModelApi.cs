﻿using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace QtSharp.Tests
{
    /// <summary>
    /// The SQL substitute package: Qt's model classes implemented over ADO.NET. The test does not
    /// re-test SQLite or ADO.NET - it checks the seam: a .NET result set shown by a Qt view, and
    /// Qt-model edits written back through SubmitAll.
    /// </summary>
    [Test("test-sqlmodel")]
    public static class SqlModelTests
    {
        public static int Run(TestContext context)
        {

            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            using (var create = connection.CreateCommand())
            {
                create.CommandText =
                    "CREATE TABLE person (id INTEGER PRIMARY KEY, firstname TEXT, city TEXT);" +
                    "INSERT INTO person VALUES (1, 'Danny', 'Beach')," +
                    "                            (2, 'Christine', 'French')," +
                    "                            (3, 'Lars', 'Stockholm');";
                create.ExecuteNonQuery();
            }

            // ---- a read-only query behind a Qt model ------------------------------------
            using var query = new QSqlQueryModel();
            query.SetConnection(connection);
            context.Check(query.SetQuery("SELECT id, firstname, city FROM person ORDER BY id"),
                "SetQuery materializes the result through ADO.NET");
            context.Check(query.RowCount() == 3 && query.ColumnCount() == 3,
                $"the model reports {query.RowCount()} rows and {query.ColumnCount()} columns");

            var index = query.Index(1, 1);
                context.Check(query.Data(index) as string == "Christine",
                    "Data(index) reads a cell through QVariant");
                context.Check(query.Data(index, (int)Qt.ItemDataRole.EditRole) as string == "Christine",
                    "Data(index, EditRole) seeds a view's editor with the same value");
                context.Check(query.Data(index, (int)Qt.ItemDataRole.ToolTipRole) == null,
                    "Data still ignores roles the model does not serve");

            // The editor path a QTableView takes: QStyledItemDelegate seeds its QLineEdit from
            // EditRole, so this is the assertion that fails when Data answers DisplayRole only.
            using (var itemDelegate = new QStyledItemDelegate())
            using (var option = new QStyleOptionViewItem())
            using (var editor = itemDelegate.CreateEditor(null, option, index))
            {
                itemDelegate.SetEditorData(editor, index);
                string text = (editor as QLineEdit)?.Text;
                context.Check(text == "Christine",
                    $"a view's editor opens on the cell value (got '{text}')");
            }
            context.Check(query.HeaderData(1, Qt.Orientation.Horizontal) as string == "firstname",
                "HeaderData names the column");
            context.Check(query.Record(0)["city"] as string == "Beach",
                "Record(row) is the row as a dictionary");
            context.Check(query.Value(2, "firstname") as string == "Lars",
                "Value(row, column) reads by name");

            // The Qt view half: the model is a QAbstractTableModel, so QTableView renders it.
            using (var view = new QTableView())
            {
                view.SetModel(query);
                view.Resize(420, 220);
                view.Show();
                QCoreApplication.ProcessEvents();
                using var pixmap = view.Grab();
                context.Check(ExamplePixels.GrabCovers(pixmap, view.Width, view.Height),
                    $"QTableView renders the SQL model ({pixmap.Width()}x{pixmap.Height()})");
            }

            // ---- an editable table behind a Qt model ------------------------------------
            using var table = new QSqlTableModel();
            table.SetConnection(connection);
            table.SetTable("person");
            context.Check(table.RowCount() == 3, "SetTable loads the table");

            var cell = table.Index(1, 2);
            context.Check(table.Flags(cell).HasFlag(Qt.ItemFlags.ItemIsEditable),
                "Flags marks a valid cell editable");
            context.Check(!table.Flags(default(QModelIndex)).HasFlag(Qt.ItemFlags.ItemIsEditable),
                "Flags leaves an invalid index non-editable");

            int changes = 0;
            using var connectionHandle = ApiRuntime.Connect(
                table.Handle, "dataChanged(QModelIndex,QModelIndex,QList<int>)",
                (argv, _) => changes++);
            context.Check(connectionHandle.IsConnected,
                "a native connection to dataChanged(QModelIndex,QModelIndex,QList<int>) is established");

            context.Check(table.SetData(cell, "Lyon", (int)Qt.ItemDataRole.EditRole),
                "SetData stages an edit");
            context.Check(changes == 1,
                $"a staged edit emits one dataChanged (got {changes})");

            int rejectedChanges = changes;
            context.Check(!table.SetData(cell, "Paris", (int)Qt.ItemDataRole.DisplayRole),
                "SetData rejects a non-edit role");
            context.Check(!table.SetData(default(QModelIndex), "Paris", (int)Qt.ItemDataRole.EditRole),
                "SetData rejects an invalid index");
            context.Check(changes == rejectedChanges,
                $"rejected edits emit no dataChanged (got {changes - rejectedChanges})");
            var staged = table.Index(1, 2);
                context.Check(table.Data(staged) as string == "Lyon",
                    "the model shows the staged value before it is written");
                context.Check(table.Data(staged, (int)Qt.ItemDataRole.EditRole) as string == "Lyon",
                    "an editor reopens on the staged value");
                context.Check(table.Record(1)["city"] as string == "Lyon",
                    "Record(row) reads through the model, staged edit included");
                context.Check(table.Value(1, "city") as string == "Lyon",
                    "Value(row, column) reads through the model, staged edit included");

            context.Check(table.SubmitAll(), "SubmitAll writes the edit");
            using (var verify = new QSqlQueryModel())
            {
                verify.SetConnection(connection);
                verify.SetQuery("SELECT city FROM person WHERE id = 2");
                var verifyIndex = verify.Index(0, 0);
                context.Check(verify.Data(verifyIndex) as string == "Lyon",
                    "the edit reached SQLite");
            }

            var cell2 = table.Index(2, 2);
                table.SetData(cell2, "nowhere", 2);
            int stagedChanges = changes;
            table.RevertAll();
            context.Check(changes == stagedChanges + 1,
                $"RevertAll emits dataChanged for the restored row (got {changes - stagedChanges})");
            var reverted = table.Index(2, 2);
                context.Check(table.Data(reverted) as string == "Stockholm",
                    "RevertAll discards the staged value");
            int afterRevert = changes;
            table.RevertAll();
            context.Check(changes == afterRevert,
                "RevertAll with nothing staged emits no dataChanged");

            // ---- headers, inserts and deletes ------------------------------------------------
            using (var extra = connection.CreateCommand())
            {
                extra.CommandText =
                    "CREATE TABLE notes (id INTEGER PRIMARY KEY, text TEXT);" +
                    "INSERT INTO notes VALUES (1, 'first'), (2, 'second'), (3, 'third');";
                extra.ExecuteNonQuery();
            }

            int headerChanges = 0;
            using (var headerConnection = ApiRuntime.Connect(table.Handle,
                "headerDataChanged(Qt::Orientation,int,int)", (argv, _) => headerChanges++))
            {
                context.Check(table.SetHeaderData(1, Qt.Orientation.Horizontal, "First name", 2),
                    "SetHeaderData stores a horizontal header");
                context.Check(table.HeaderData(1, Qt.Orientation.Horizontal, 0) as string == "First name",
                    "a DisplayRole header falls back to the stored EditRole value");
                context.Check(headerChanges == 1,
                    $"SetHeaderData emits one headerDataChanged (got {headerChanges})");
                context.Check(!table.SetHeaderData(1, Qt.Orientation.Vertical, "x", 2),
                    "SetHeaderData refuses a vertical section");
                context.Check(table.SetHeaderData(0, Qt.Orientation.Horizontal, null, 2),
                    "SetHeaderData removes an override with a null value");
                context.Check(table.HeaderData(0, Qt.Orientation.Horizontal, 0) as string == "id",
                    "the column name is back after the override is removed");
            }

            var sameCell = table.Index(0, 1);
            int beforeSame = changes;
            context.Check(table.SetData(sameCell, "Danny", 2),
                "SetData with the value already there succeeds");
            context.Check(changes == beforeSame,
                "SetData with the value already there emits no dataChanged");

            using (var editing = new QSqlTableModel())
            {
                editing.SetConnection(connection);
                editing.SetTable("notes");
                context.Check(editing.RowCount() == 3, "the notes table has three rows");

                context.Check(editing.InsertRow(3), "InsertRow appends a row");
                context.Check(editing.RowCount() == 4, "the inserted row is part of the model");
                context.Check(editing.HeaderData(3, Qt.Orientation.Vertical, 0) as string == "*",
                    "an inserted row is marked * in the vertical header");
                context.Check(editing.SetData(editing.Index(3, 1), "fourth", 2),
                    "an inserted row can be filled in");
                context.Check(editing.SubmitAll(), "SubmitAll writes the inserted row");
                context.Check(editing.RowCount() == 4
                    && editing.Data(editing.Index(3, 1)) as string == "fourth",
                    "the inserted row is read back after the reload");

                context.Check(editing.RemoveRow(0), "RemoveRow marks a row for deletion");
                context.Check(editing.RowCount() == 4,
                    "the marked row stays visible until submit");
                context.Check(editing.HeaderData(0, Qt.Orientation.Vertical, 0) as string == "!",
                    "a deleted row is marked ! in the vertical header");
                context.Check(!editing.Flags(editing.Index(0, 1)).HasFlag(Qt.ItemFlags.ItemIsEditable),
                    "a deleted row is not editable");
                context.Check(!editing.SetData(editing.Index(0, 1), "changed", 2),
                    "SetData refuses a deleted row");
                context.Check(editing.SubmitAll(), "SubmitAll writes the deletion");
                context.Check(editing.RowCount() == 3
                    && editing.Data(editing.Index(0, 1)) as string == "second",
                    "the deleted row is gone after the reload");
            }

            using (var reverting = new QSqlTableModel())
            {
                reverting.SetConnection(connection);
                reverting.SetTable("notes");

                reverting.InsertRow(3);
                reverting.SetData(reverting.Index(3, 1), "temp", 2);
                reverting.RevertAll();
                context.Check(reverting.RowCount() == 3,
                    "RevertAll drops an inserted row");

                reverting.RemoveRow(0);
                reverting.RevertAll();
                context.Check(reverting.Flags(reverting.Index(0, 1)).HasFlag(Qt.ItemFlags.ItemIsEditable),
                    "RevertAll restores a deleted row");
                context.Check(reverting.Data(reverting.Index(0, 1)) as string == "second",
                    "the restored row shows its value again");
            }

            using (var marked = new QSqlTableModel())
            {
                marked.SetConnection(connection);
                marked.SetTable("notes");

                var headerSignals = new List<(Qt.Orientation Orientation, int First, int Last)>();
                marked.HeaderDataChanged += (orientation, first, last) =>
                    headerSignals.Add((orientation, first, last));

                marked.RemoveRow(1);
                context.Check(headerSignals.Count == 1
                      && headerSignals[0] == (Qt.Orientation.Vertical, 1, 1),
                    $"marking a row deleted emits headerDataChanged for its section (got {headerSignals.Count})");

                headerSignals.Clear();
                marked.RevertAll();
                context.Check(headerSignals.Count == 1
                      && headerSignals[0] == (Qt.Orientation.Vertical, 1, 1),
                    $"reverting a deletion repaints the header too (got {headerSignals.Count})");
                context.Check(marked.HeaderData(1, Qt.Orientation.Vertical, 0) as int? == 2,
                    "the vertical header shows the row number again");
            }

            using (var shifting = new QSqlTableModel())
            {
                shifting.SetConnection(connection);
                shifting.SetTable("notes");

                shifting.SetData(shifting.Index(1, 1), "edited", 2);
                shifting.InsertRow(0);
                context.Check(shifting.Data(shifting.Index(2, 1)) as string == "edited",
                    "an insert above an edited row keeps the edit with its row");
                shifting.RevertAll();
                context.Check(shifting.RowCount() == 3
                    && shifting.Data(shifting.Index(1, 1)) as string == "third",
                    "reverting drops the insert and the edit together");
            }

            // ---- provider errors come back through the Qt contract --------------------------
            using (var broken = new QSqlQueryModel())
            {
                broken.SetConnection(connection);
                context.Check(!broken.SetQuery("SELECT * FROM no_such_table"),
                    "SetQuery returns false when the provider rejects the query");
                context.Check(broken.LastError() != null,
                    "LastError() carries the provider's exception");
                context.Check(broken.RowCount() == 0,
                    "a failed SetQuery resets the model, as Qt's setQuery does");
                context.Check(broken.SetQuery("SELECT id FROM person ORDER BY id"),
                    "a later SetQuery succeeds");
                context.Check(broken.LastError() == null,
                    "a successful SetQuery clears LastError()");
            }

            using (var extra = connection.CreateCommand())
            {
                extra.CommandText =
                    "CREATE TABLE strict (id INTEGER PRIMARY KEY, name TEXT NOT NULL);" +
                    "INSERT INTO strict VALUES (1, 'one');";
                extra.ExecuteNonQuery();
            }

            using (var strict = new QSqlTableModel())
            {
                strict.SetConnection(connection);
                strict.SetTable("strict");
                context.Check(strict.RowCount() == 1, "the strict table has one row");

                strict.SetData(strict.Index(0, 1), "edited", 2);
                strict.InsertRow(1);   // the NOT NULL name stays NULL
                context.Check(!strict.SubmitAll(),
                    "SubmitAll returns false when the provider rejects a row");
                context.Check(strict.LastError() != null,
                    "a failed SubmitAll records the exception in LastError()");
                context.Check(strict.RowCount() == 2
                    && strict.HeaderData(1, Qt.Orientation.Vertical, 0) as string == "*",
                    "a failed SubmitAll keeps the staged row");
                context.Check(strict.Data(strict.Index(0, 1)) as string == "edited",
                    "a failed SubmitAll keeps the staged edit");
                using (var verify = connection.CreateCommand())
                {
                    verify.CommandText = "SELECT name FROM strict WHERE id = 1";
                    context.Check(verify.ExecuteScalar() as string == "one",
                        "a failed SubmitAll rolls the whole transaction back");
                }

                context.Check(strict.SetData(strict.Index(1, 1), "two", 2),
                    "the inserted row can be completed after the failure");
                context.Check(strict.SubmitAll(),
                    "SubmitAll succeeds once the row matches the constraint");
                context.Check(strict.LastError() == null,
                    "a successful SubmitAll clears LastError()");
                context.Check(strict.RowCount() == 2
                    && strict.Data(strict.Index(0, 1)) as string == "edited",
                    "the edit that failed with the insert is written with it");
            }

            return context.ExitCode;
        }
    }
}
