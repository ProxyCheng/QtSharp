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
            table.RevertAll();
            var reverted = table.Index(2, 2);
                context.Check(table.Data(reverted) as string == "Stockholm",
                    "RevertAll discards the staged value");

            return context.ExitCode;
        }
    }
}
