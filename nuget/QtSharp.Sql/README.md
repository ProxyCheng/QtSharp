# QtSharp.Sql

> The package id carries the platform: the snippets below use the `win-x64` variant. On
> Linux, use the matching `.linux-x64` package (`QtSharp.Api.linux-x64`, ...).

The two Qt model classes the removed QtSql binding used to provide, implemented over ADO.NET
(`System.Data.Common`) instead: `QSqlQueryModel` (read-only, a `QAbstractTableModel` over a query)
and `QSqlTableModel` (editable, staged edits written by `SubmitAll`). They live in the
`QtSharp.QtSql` namespace, so code written against the old binding keeps its class names.

This is a *substitute*, not a binding: there is no Qt SQL module behind it. Any ADO.NET provider
works - pass its `DbConnection`:

```xml
<ItemGroup>
  <PackageReference Include="QtSharp.Api.win-x64" Version="0.24.0" />
  <PackageReference Include="QtSharp.Sql.win-x64" Version="0.24.0" />
  <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.14" />
</ItemGroup>
```

```csharp
using Microsoft.Data.Sqlite;
using QtSharp.QtSql;
using QtSharp.QtWidgets;

using var connection = new SqliteConnection("Data Source=app.db");
using var model = new QSqlQueryModel();
model.SetConnection(connection);
model.SetQuery("SELECT id, firstname, city FROM person ORDER BY id");

var view = new QTableView();
view.SetModel(model);
view.Show();
```

The editable half stages edits and writes them on `SubmitAll()` (one UPDATE per edited cell,
inside a transaction); `RevertAll()` discards them. The primary key defaults to `id` and can be
changed with `SetTable(table, primaryKey)`.

Because the models are plain C# classes, there is no native glue, no Qt runtime dependency beyond
`QtSharp.Api`, and no SQL driver plugin: the provider is whatever ADO.NET provider you reference.

## Performance notes

* The data access is ADO.NET against the provider's own engine (Microsoft.Data.Sqlite wraps
  sqlite3 directly), so query execution is on par with - usually faster than - Qt's SQL drivers.
* `SetQuery`/`SetTable` **materialize the whole result**, like Qt's `QSqlQueryModel`. Qt's
  `QSqlTableModel` fetches in 256-row batches; for very large tables page the query yourself
  (`WHERE`/`LIMIT`/`OFFSET`) or keep the model to the rows a view will show.
* Every `Data()`/`HeaderData()` call crosses the managed/native boundary because the model is a
  managed subclass of `QAbstractTableModel`. That is a constant per visible cell, not an
  asymptotic cost; a repaint of a few dozen rows is unaffected.
* `SubmitAll()` issues one UPDATE per edited row with all changed columns, inside one transaction -
  the same shape as Qt's manual-submit path.
