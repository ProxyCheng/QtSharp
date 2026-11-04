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
  <PackageReference Include="QtSharp.Api.win-x64" Version="0.26.0" />
  <PackageReference Include="QtSharp.Sql.win-x64" Version="0.26.0" />
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
inside a transaction); `RevertAll()` discards them and emits `dataChanged` for each row that had
an edit, so the view repaints the restored values. The primary key defaults to `id` and can be
changed with `SetTable(table, primaryKey)`. The model reports editable flags for every valid cell
and emits `dataChanged` when an edit is staged, so a `QTableView` edits cells in place. `Data`
answers both `DisplayRole` and `EditRole`, so the view's editor opens on the current value;
`Record(row)` and `Value(row, column)` read through the same path, staged edits included.

Rows are staged the same way Qt stages them: `InsertRows`/`InsertRow` add empty rows (marked `*`
in the vertical header), `RemoveRows`/`RemoveRow` mark base rows deleted (marked `!`, still
visible, not editable), and `SubmitAll` writes the UPDATEs, INSERTs and DELETEs in one transaction.
`RevertAll` drops inserted rows and restores deleted ones. Marking a row deleted and restoring
it each emit `headerDataChanged` for that row, so the vertical header repaints its `!` marker (or
the row number) right away. `SetHeaderData` stores a horizontal
header and emits `headerDataChanged`; a `DisplayRole` lookup falls back to the stored `EditRole`
value, as in Qt. Columns the provider reports as read-only are not editable.

Provider failures follow Qt rather than C#: `SetQuery`/`Select` and `SubmitAll` return `false` and
leave the driver's exception in `LastError()` instead of throwing through the view's signal
handlers. A failed `SetQuery` resets the model the way Qt's `setQuery` does. A failed `SubmitAll`
rolls the transaction back and keeps every staged edit, inserted row and deletion, so the caller
can fix the offending row and submit again - Qt only clears the cache after a successful submit.

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
