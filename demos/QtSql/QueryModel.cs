using System;

// Qt's SQL "Query Model" example (examples/sql/querymodel): a QSqlQueryModel over a SQLite table,
// shown in a QTableView. The upstream example creates the small "person" table it queries; this
// port keeps the same schema and rows, then also drives QSqlTableModel and the raw QSqlQuery /
// QSqlRecord path that the other SQL examples use, so the binding is checked on both sides.
[Demo("qtsql-querymodel")]
public static class QueryModel
{
    static readonly (int Id, string First, string Last, string City)[] People =
    {
        (1, "Danny", "Young", "Beach"),
        (2, "Christine", "Holand", "French"),
        (3, "Lars", "Gordon", "Stockholm"),
        (4, "Roberto", "Ricardo", "Paris"),
    };

    public static int Run(QApplication app)
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        Check(QSqlDatabase.IsDriverAvailable("QSQLITE"), "QSQLITE is one of the deployed drivers");

        var db = QSqlDatabase.AddDatabase("QSQLITE");
        db.SetDatabaseName(":memory:");
        Check(db.IsValid(), "QSqlDatabase.addDatabase(\"QSQLITE\") returns a valid database");
        Check(db.Open(), "the in-memory database opens");

        try
        {
            using (var query = new QSqlQuery(db))
            {
                Check(query.Exec(
                    "create table person (id integer primary key, firstname varchar(20), " +
                    "lastname varchar(20), city varchar(20))"),
                    "QSqlQuery.exec creates the person table");
                Check(query.Prepare("insert into person (id, firstname, lastname, city) values (?, ?, ?, ?)"),
                    "QSqlQuery.prepare accepts the INSERT");
                foreach (var person in People)
                {
                    query.BindValue(0, QVariant.From(person.Id));
                    query.BindValue(1, QVariant.From(person.First));
                    query.BindValue(2, QVariant.From(person.Last));
                    query.BindValue(3, QVariant.From(person.City));
                    Check(query.Exec(), $"the INSERT for {person.First} {person.Last} runs");
                }
                query.Finish();
            }

            // The example's own model: SELECT * FROM person, displayed by a QTableView.
            using var model = new QSqlQueryModel();
            model.SetQuery("select * from person order by id", db);
            Check(model.LastError().IsValid() == false, "QSqlQueryModel.setQuery reports no error");
            Check(model.RowCount() == People.Length, $"the model has {People.Length} rows");
            Check(model.ColumnCount() == 4, "the model has four columns");

            bool everyRowRead = true;
            for (int row = 0; row < People.Length; row++)
            {
                var record = model.Record(row);
                var person = People[row];
                // SQLite hands INTEGER columns back as QVariant::LongLong, not Int.
                everyRowRead &= Convert.ToInt64(record.Value("id").ToObject()) == person.Id
                                && record.Value("firstname").ToObject() as string == person.First
                                && record.Value("lastname").ToObject() as string == person.Last
                                && record.Value("city").ToObject() as string == person.City;
            }
            Check(everyRowRead, "QSqlRecord.value reads every inserted row back");

            using (var index = model.Index(0, 1))
                Check(model.Data(index).ToObject() as string == "Danny",
                    "QSqlQueryModel.data returns the first name through QVariant");

            using (var header = model.HeaderData(1, Qt.Orientation.Horizontal))
                Check(header.ToObject() as string == "firstname",
                    "QSqlQueryModel.headerData names the column");

            // QSqlTableModel is the editable view of the same table; the official "cachedtable" and
            // "relationaltablemodel" examples build on it.
            using var table = new QSqlTableModel(null, db);
            table.SetTable("person");
            table.SetEditStrategy(QSqlTableModel.EditStrategy.OnManualSubmit);
            Check(table.Select(), "QSqlTableModel.select loads the table");
            Check(table.RowCount() == People.Length, "QSqlTableModel reports the same row count");

            using (var cell = table.Index(1, 3))
                Check(table.SetData(cell, QVariant.From("Lyon"), 2 /* Qt::EditRole */),
                    "QSqlTableModel.setData stages an edit");
            Check(table.SubmitAll(), "QSqlTableModel.submitAll writes the edit");
            using (var after = new QSqlQuery("select city from person where id = 2", db))
                Check(after.Next() && after.Value(0).ToObject() as string == "Lyon",
                    "the edit is visible through a plain SELECT");

            using (var view = new QTableView())
            {
                view.SetModel(model);
                view.WindowTitle = "Query Model";
                view.Resize(420, 220);
                view.Show();
                QCoreApplication.ProcessEvents();
                using var pixmap = view.Grab();
                Check(DemoCatalog.GrabCovers(pixmap, view.Width, view.Height),
                    $"the QTableView over the model renders ({pixmap.Width()}x{pixmap.Height()})");
            }
        }
        finally
        {
            string connection = db.ConnectionName();
            db.Close();
            QSqlDatabase.RemoveDatabase(connection);
            db.Dispose();
        }

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[FAIL] {failures} check(s) failed");
        return failures == 0 ? 0 : 1;
    }
}
