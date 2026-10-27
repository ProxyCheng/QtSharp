using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using QtSharp.QtCore;

namespace QtSharp.QtSql
{
    /// <summary>
    /// The C# counterpart of QtSql's <c>QSqlTableModel</c>: an editable SQL table behind a Qt model.
    /// Edits are staged in memory (<see cref="SetData"/> with Qt::EditRole), the view shows them
    /// immediately, and <see cref="SubmitAll"/> writes one UPDATE per edited cell inside a
    /// transaction; <see cref="RevertAll"/> discards them. This is the OnManualSubmit strategy;
    /// there is no live field-by-field write.
    /// </summary>
    public class QSqlTableModel : QSqlQueryModel
    {
        readonly Dictionary<(int Row, int Column), object> pending = new();
        string tableName;
        string primaryKey = "id";

        public QSqlTableModel()
        {
        }

        public string TableName => tableName;

        /// <summary>Loads a table; <paramref name="primaryKeyName"/> is the key SubmitAll uses.</summary>
        public void SetTable(string table, string primaryKeyName = "id")
        {
            tableName = table ?? throw new ArgumentNullException(nameof(table));
            primaryKey = primaryKeyName ?? "id";
            pending.Clear();
            SetQuery($"SELECT * FROM {Quote(tableName)}");
        }

        protected override object CellValue(int row, int column)
        {
            if (pending.TryGetValue((row, column), out object value))
                return Normalize(value);
            return base.CellValue(row, column);
        }

        public override bool SetData(QModelIndex index, object value, int role = default)
        {
            // Qt::EditRole is 2; other roles are not editable.
            if (role != 2 || !index.IsValid())
                return false;
            int row = index.Row(), column = index.Column();
            if (row < 0 || row >= RowCount() || column < 0 || column >= ColumnCount())
                return false;
            pending[(row, column)] = value;
            return true;
        }

        /// <summary>Writes every staged edit in one transaction, then reloads.</summary>
        public bool SubmitAll()
        {
            if (pending.Count == 0)
                return true;
            if (tableName == null)
                throw new InvalidOperationException("SetTable must be called before SubmitAll.");
            int keyColumn = ColumnIndex(primaryKey);
            if (keyColumn < 0)
                throw new InvalidOperationException(
                    $"The primary key '{primaryKey}' is not part of the result set.");

            using var transaction = Connection.BeginTransaction();
            try
            {
                // One UPDATE per edited row with every changed column in it, the way Qt's
                // QSqlTableModel submits a row - not one statement per cell.
                foreach (var rowGroup in pending.GroupBy(entry => entry.Key.Row))
                {
                    int row = rowGroup.Key;
                    var edits = rowGroup.ToList();
                    using var command = Connection.CreateCommand();
                    command.Transaction = transaction;
                    var assignments = new List<string>(edits.Count);
                    for (int i = 0; i < edits.Count; i++)
                    {
                        string parameterName = "@v" + i;
                        assignments.Add($"{Quote(Columns[edits[i].Key.Column])} = {parameterName}");
                        var parameter = command.CreateParameter();
                        parameter.ParameterName = parameterName;
                        parameter.Value = edits[i].Value ?? DBNull.Value;
                        command.Parameters.Add(parameter);
                    }
                    command.CommandText =
                        $"UPDATE {Quote(tableName)} SET {string.Join(", ", assignments)} " +
                        $"WHERE {Quote(primaryKey)} = @key";
                    var key = command.CreateParameter();
                    key.ParameterName = "@key";
                    key.Value = RawCell(row, keyColumn) ?? DBNull.Value;
                    command.Parameters.Add(key);
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            pending.Clear();
            Select();
            return true;
        }

        /// <summary>Discards every staged edit without touching the database.</summary>
        public void RevertAll() => pending.Clear();

        static string Quote(string identifier)
            => "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }
}
