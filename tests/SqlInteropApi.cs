using System;
using System.Linq;

namespace QtSharp.Tests
{
    /// <summary>
    /// The BCL-facing half of QtSql: rows as dictionaries, LINQ to Objects over a consumed query
    /// and over a model, and typed reads that go through QVariant.ToObject(). There is no LINQ
    /// provider here - these are ordinary sequences, so the query runs in Qt and the operators run
    /// in C#.
    /// </summary>
    [Demo("test-qtsql")]
    public static class SqlInteropTests
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

            var db = QSqlDatabase.AddDatabase("QSQLITE");
            db.SetDatabaseName(":memory:");
            Check(db.Open(), "the in-memory SQLite database opens");
            try
            {
                using (var setup = new QSqlQuery(db))
                {
                    setup.Exec("create table person (id integer primary key, firstname text, " +
                               "lastname text, city text)");
                    setup.Prepare("insert into person (id, firstname, lastname, city) values (?, ?, ?, ?)");
                    var people = new[]
                    {
                        (Id: 1, First: "Danny", Last: "Young", City: "Beach"),
                        (Id: 2, First: "Christine", Last: "Holand", City: "French"),
                        (Id: 3, First: "Lars", Last: "Gordon", City: "Stockholm"),
                        (Id: 4, First: "Roberto", Last: "Ricardo", City: "Paris"),
                    };
                    foreach (var person in people)
                    {
                        setup.BindValue(0, QVariant.From(person.Id));
                        setup.BindValue(1, QVariant.From(person.First));
                        setup.BindValue(2, QVariant.From(person.Last));
                        setup.BindValue(3, QVariant.From(person.City));
                        setup.Exec();
                    }
                }

                // Rows() -> dictionaries -> LINQ to Objects.
                using (var query = new QSqlQuery(
                    "select id, firstname, lastname, city from person order by id", db))
                {
                    var rows = query.Rows().ToList();
                    Check(rows.Count == 4, $"Rows() yields one dictionary per row ({rows.Count})");
                    Check(rows[0]["firstname"] as string == "Danny",
                        "a dictionary value is the unwrapped BCL value");
                    Check(rows.Count(r => (string)r["city"] == "Stockholm") == 1,
                        "Where() filters the rows in C#");
                    Check(rows.Select(r => (string)r["firstname"])
                            .SequenceEqual(new[] { "Danny", "Christine", "Lars", "Roberto" }),
                        "Select() projects the columns in row order");
                }

                // SelectRows() keeps the Qt record API; Get<T> converts SQLite's LongLong to int.
                using (var query = new QSqlQuery(
                    "select id, firstname, city from person order by id", db))
                {
                    var people = query.SelectRows(r => (
                        Id: r.Get<int>("id"),
                        First: r.Get<string>("firstname"),
                        City: r.Get<string>("city"))).ToList();
                    Check(people.Count == 4 && people[1].First == "Christine",
                        "SelectRows() projects typed records");
                    Check(people[3].Id == 4 && people[3].City == "Paris",
                        "Get<int> converts the LongLong SQLite returns");
                }

                // The raw record accessors still work next to the BCL ones.
                using (var query = new QSqlQuery("select * from person where id = 2", db))
                {
                    using var record = query.Records().First();
                    Check(record.Get<int>("id") == 2, "Records() exposes the disposable Qt record");
                    Check(record.Get<string>("lastname") == "Holand", "Get<string> reads a text column");
                    Check(record.ToDictionary()["lastname"] as string == "Holand",
                        "ToDictionary() names the columns");
                    Check(record.Get<string>("no_such_column", "n/a") == "n/a",
                        "Get<T> falls back for a missing column");
                }

                // The model side: no cursor, the rows are already there.
                using (var model = new QSqlQueryModel())
                {
                    model.SetQuery("select id, firstname, lastname from person order by id", db);
                    Check(model.Rows().Count() == 4, "QSqlQueryModel.Rows() enumerates its rows");
                    Check((string)model.Rows().First()["lastname"] == "Young",
                        "the model's first row is the first person");
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
}
