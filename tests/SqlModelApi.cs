using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace QtSharp.Tests
{
    /// <summary>
    /// The SQL substitute package: Qt's model classes implemented over ADO.NET. The test does not
    /// re-test SQLite or ADO.NET - it checks the seam: a .NET result set shown by a Qt view, and
    /// Qt-model edits written back through SubmitAll.
    /// </summary>
    [Demo("test-sqlmodel")]
    public static class SqlModelTests
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
            Check(query.SetQuery("SELECT id, firstname, city FROM person ORDER BY id"),
                "SetQuery materializes the result through ADO.NET");
            Check(query.RowCount() == 3 && query.ColumnCount() == 3,
                $"the model reports {query.RowCount()} rows and {query.ColumnCount()} columns");

            using (var index = query.Index(1, 1))
                Check(query.Data(index).ToObject() as string == "Christine",
                    "Data(index) reads a cell through QVariant");
            using (var header = query.HeaderData(1, Qt.Orientation.Horizontal))
                Check(header.ToObject() as string == "firstname",
                    "HeaderData names the column");
            Check(query.Record(0)["city"] as string == "Beach",
                "Record(row) is the row as a dictionary");
            Check(query.Value(2, "firstname") as string == "Lars",
                "Value(row, column) reads by name");

            // The Qt view half: the model is a QAbstractTableModel, so QTableView renders it.
            using (var view = new QTableView())
            {
                view.SetModel(query);
                view.Resize(420, 220);
                view.Show();
                QCoreApplication.ProcessEvents();
                using var pixmap = view.Grab();
                Check(DemoCatalog.GrabCovers(pixmap, view.Width, view.Height),
                    $"QTableView renders the SQL model ({pixmap.Width()}x{pixmap.Height()})");
            }

            // ---- an editable table behind a Qt model ------------------------------------
            using var table = new QSqlTableModel();
            table.SetConnection(connection);
            table.SetTable("person");
            Check(table.RowCount() == 3, "SetTable loads the table");

            using (var cell = table.Index(1, 2))
                Check(table.SetData(cell, QVariant.From("Lyon"), 2 /* Qt::EditRole */),
                    "SetData stages an edit");
            using (var staged = table.Index(1, 2))
                Check(table.Data(staged).ToObject() as string == "Lyon",
                    "the model shows the staged value before it is written");

            Check(table.SubmitAll(), "SubmitAll writes the edit");
            using (var verify = new QSqlQueryModel())
            {
                verify.SetConnection(connection);
                verify.SetQuery("SELECT city FROM person WHERE id = 2");
                using var index = verify.Index(0, 0);
                Check(verify.Data(index).ToObject() as string == "Lyon",
                    "the edit reached SQLite");
            }

            using (var cell = table.Index(2, 2))
                table.SetData(cell, QVariant.From("nowhere"), 2);
            table.RevertAll();
            using (var reverted = table.Index(2, 2))
                Check(table.Data(reverted).ToObject() as string == "Stockholm",
                    "RevertAll discards the staged value");

            Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[FAIL] {failures} check(s) failed");
            return failures == 0 ? 0 : 1;
        }
    }
}
