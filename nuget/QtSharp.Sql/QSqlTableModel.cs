using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using QtSharp.QtCore;

namespace QtSharp.QtSql
{
    /// <summary>
    /// The C# counterpart of QtSql's <c>QSqlTableModel</c>: an editable SQL table behind a Qt model.
    /// Edits are staged in memory (<see cref="SetData"/> with Qt::EditRole), inserted and removed
    /// rows are staged the same way, the view shows them immediately with Qt's <c>*</c>/<c>!</c>
    /// vertical-header markers, and <see cref="SubmitAll"/> writes the updates, inserts and deletes
    /// in one transaction. <see cref="RevertAll"/> discards everything. This is the OnManualSubmit
    /// strategy; there is no live field-by-field write and no SetEditStrategy.
    /// </summary>
    public class QSqlTableModel : QSqlQueryModel
    {
        enum RowOperation
        {
            None,
            Insert,
            Delete
        }

        // One logical row of the model: a base row of the result set, or a row inserted with
        // InsertRows. A deleted base row keeps its value and stays visible (Qt keeps it too, marked
        // "!"), so a slot never points at a removed base row.
        sealed class RowSlot
        {
            public RowOperation Operation;
            public int BaseRow = -1;
            public Dictionary<int, object> Values;
        }

        readonly Dictionary<(int Row, int Column), object> pending = new();
        readonly List<RowSlot> slots = new();
        int syncedBaseCount = -1;
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
            ResetStaging();
            SetQuery($"SELECT * FROM {Quote(tableName)}");
        }

        // The base model materializes its rows; the slot list adds this model's inserted rows and
        // deletion marks on top. A count change means the base was reset (SetTable/Select), so the
        // slots - and any staged edit, as in Qt - go back to plain rows.
        void EnsureRowsSynced()
        {
            int baseCount = base.RowCount();
            if (baseCount == syncedBaseCount)
                return;

            slots.Clear();
            for (int i = 0; i < baseCount; i++)
                slots.Add(new RowSlot { Operation = RowOperation.None, BaseRow = i });
            pending.Clear();
            syncedBaseCount = baseCount;
        }

        void ResetStaging()
        {
            pending.Clear();
            slots.Clear();
            syncedBaseCount = -1;
        }

        protected override object CellValue(int row, int column)
        {
            EnsureRowsSynced();
            if (row < 0 || row >= slots.Count)
                return null;

            RowSlot slot = slots[row];
            switch (slot.Operation)
            {
                case RowOperation.Insert:
                    return slot.Values.TryGetValue(column, out object inserted)
                        ? Normalize(inserted)
                        : null;
                case RowOperation.Delete:
                    // Qt snapshots the record when the row is marked deleted; the staged value the
                    // row had before is not part of that snapshot.
                    return base.CellValue(slot.BaseRow, column);
                default:
                    return pending.TryGetValue((slot.BaseRow, column), out object value)
                        ? Normalize(value)
                        : base.CellValue(slot.BaseRow, column);
            }
        }

        public override int RowCount(QModelIndex parent = default)
        {
            if (parent.IsValid())
                return 0;
            EnsureRowsSynced();
            return slots.Count;
        }

        public override object HeaderData(int section, Qt.Orientation orientation, int role = default)
        {
            EnsureRowsSynced();
            if (orientation == Qt.Orientation.Vertical && role == 0
                && section >= 0 && section < slots.Count)
            {
                switch (slots[section].Operation)
                {
                    case RowOperation.Insert: return "*";
                    case RowOperation.Delete: return "!";
                }
                return section + 1;
            }
            return base.HeaderData(section, orientation, role);
        }

        public override Qt.ItemFlags Flags(QModelIndex index)
        {
            if (!index.IsValid())
                return base.Flags(index);

            EnsureRowsSynced();
            int row = index.Row(), column = index.Column();
            if (row < 0 || row >= slots.Count || column < 0 || column >= ColumnCount())
                return base.Flags(index);

            if (slots[row].Operation == RowOperation.Delete || IsColumnReadOnly(column))
                return base.Flags(index) & ~Qt.ItemFlags.ItemIsEditable;
            return base.Flags(index) | Qt.ItemFlags.ItemIsEditable;
        }

        public override bool SetData(QModelIndex index, object value, int role = default)
        {
            // Qt::EditRole is 2; other roles are not editable.
            if (role != 2 || !index.IsValid())
                return false;

            EnsureRowsSynced();
            int row = index.Row(), column = index.Column();
            if (row < 0 || row >= slots.Count || column < 0 || column >= ColumnCount())
                return false;

            RowSlot slot = slots[row];
            if (slot.Operation == RowOperation.Delete || IsColumnReadOnly(column))
                return false;

            // Qt treats a write of the value already there as a success without a dataChanged.
            object normalized = Normalize(value);
            if (Equals(CellValue(row, column), normalized))
                return true;

            if (slot.Operation == RowOperation.Insert)
                slot.Values[column] = value;
            else
                pending[(slot.BaseRow, column)] = value;

            DataChanged(index, index, new List<int> { role, (int)Qt.ItemDataRole.DisplayRole });
            return true;
        }

        public override bool InsertRows(int row, int count, QModelIndex parent = default)
        {
            if (parent.IsValid() || row < 0 || count <= 0)
                return false;

            EnsureRowsSynced();
            if (row > slots.Count)
                return false;

            BeginInsertRows(parent, row, row + count - 1);
            for (int i = 0; i < count; i++)
                slots.Insert(row + i, new RowSlot
                {
                    Operation = RowOperation.Insert,
                    Values = new Dictionary<int, object>()
                });
            EndInsertRows();
            return true;
        }

        public bool InsertRow(int row) => InsertRows(row, 1);

        public override bool RemoveRows(int row, int count, QModelIndex parent = default)
        {
            if (parent.IsValid() || row < 0 || count <= 0)
                return false;

            EnsureRowsSynced();
            if (row + count > slots.Count)
                return false;

            // Backwards, the way Qt removes: an inserted row disappears at once, a base row is
            // only marked, so the view keeps showing it with "!" until SubmitAll or RevertAll.
            for (int i = row + count - 1; i >= row; i--)
            {
                RowSlot slot = slots[i];
                if (slot.Operation == RowOperation.Insert)
                {
                    BeginRemoveRows(parent, i, i);
                    slots.RemoveAt(i);
                    EndRemoveRows();
                    continue;
                }
                if (slot.Operation == RowOperation.Delete)
                    continue;

                slot.Operation = RowOperation.Delete;
                DataChanged(Index(i, 0), Index(i, ColumnCount() - 1));
                // The vertical header went from the row number to "!". Qt's removeRows emits this
                // in the manual-submit strategy; without it the header keeps painting the number
                // until something else forces a repaint.
                HeaderDataChangedSignal.Emit(Qt.Orientation.Vertical, i, i);
            }
            return true;
        }

        public bool RemoveRow(int row) => RemoveRows(row, 1);

        /// <summary>
        /// Writes every staged edit, insert and delete in one transaction, then reloads. A provider
        /// error rolls the transaction back, keeps everything staged and returns false, with the
        /// exception in <see cref="QSqlQueryModel.LastError"/> - the caller fixes the row and
        /// submits again, as with Qt's manual-submit strategy.
        /// </summary>
        public bool SubmitAll()
        {
            EnsureRowsSynced();
            bool hasRows = slots.Any(slot => slot.Operation != RowOperation.None);
            if (pending.Count == 0 && !hasRows)
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
                // QSqlTableModel submits a row - not one statement per cell. The rows are written
                // in model order, inserts and deletes included, as Qt walks its cache.
                var updates = pending
                    .GroupBy(entry => entry.Key.Row)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .OrderBy(entry => entry.Key.Column)
                            .Select(entry => (Column: entry.Key.Column, Value: entry.Value))
                            .ToList());

                for (int row = 0; row < slots.Count; row++)
                {
                    RowSlot slot = slots[row];
                    if (slot.Operation == RowOperation.Insert)
                    {
                        InsertRowIntoTable(slot, transaction);
                        continue;
                    }
                    if (slot.Operation == RowOperation.Delete)
                    {
                        DeleteRowFromTable(slot.BaseRow, keyColumn, transaction);
                        continue;
                    }
                    if (updates.TryGetValue(slot.BaseRow, out var edits))
                        UpdateRowInTable(slot.BaseRow, edits, keyColumn, transaction);
                }

                transaction.Commit();
            }
            catch (DbException exception)
            {
                // Qt keeps the staged rows when submitAll() fails - only the select() after a
                // successful submit clears the cache - so the caller can fix the row and submit
                // again. The rollback additionally leaves the database at the last submitted state.
                RollBack(transaction);
                SetLastError(exception);
                return false;
            }
            catch
            {
                RollBack(transaction);
                throw;
            }

            ResetStaging();
            // Qt's manual-submit path ends in select(), and its result is submitAll()'s result.
            return Select();
        }

        static void RollBack(DbTransaction transaction)
        {
            // A failed statement can leave the transaction unusable; the rollback error must not
            // replace the one that caused it.
            try { transaction.Rollback(); } catch { }
        }

        void InsertRowIntoTable(RowSlot slot, DbTransaction transaction)
        {
            using var command = Connection.CreateCommand();
            command.Transaction = transaction;

            var names = new List<string>();
            var values = new List<string>();
            for (int column = 0; column < Columns.Count; column++)
            {
                if (IsColumnAutoGenerated(column))
                    continue;

                string parameterName = "@v" + column;
                names.Add(Quote(Columns[column]));
                values.Add(parameterName);
                var parameter = command.CreateParameter();
                parameter.ParameterName = parameterName;
                parameter.Value = slot.Values.TryGetValue(column, out object value) && value != null
                    ? value
                    : DBNull.Value;
                command.Parameters.Add(parameter);
            }

            command.CommandText = names.Count == 0
                ? $"INSERT INTO {Quote(tableName)} DEFAULT VALUES"
                : $"INSERT INTO {Quote(tableName)} ({string.Join(", ", names)}) " +
                  $"VALUES ({string.Join(", ", values)})";
            command.ExecuteNonQuery();
        }

        void DeleteRowFromTable(int baseRow, int keyColumn, DbTransaction transaction)
        {
            using var command = Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"DELETE FROM {Quote(tableName)} WHERE {Quote(primaryKey)} = @key";
            var key = command.CreateParameter();
            key.ParameterName = "@key";
            key.Value = RawCell(baseRow, keyColumn) ?? DBNull.Value;
            command.Parameters.Add(key);
            command.ExecuteNonQuery();
        }

        void UpdateRowInTable(int baseRow, List<(int Column, object Value)> edits,
            int keyColumn, DbTransaction transaction)
        {
            using var command = Connection.CreateCommand();
            command.Transaction = transaction;
            var assignments = new List<string>(edits.Count);
            for (int i = 0; i < edits.Count; i++)
            {
                string parameterName = "@v" + i;
                assignments.Add($"{Quote(Columns[edits[i].Column])} = {parameterName}");
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
            key.Value = RawCell(baseRow, keyColumn) ?? DBNull.Value;
            command.Parameters.Add(key);
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Discards every staged edit, insert and delete without touching the database. An inserted
        /// row is removed from the model (with begin/endRemoveRows, as Qt reverts one), a deleted row
        /// is restored, and every affected row gets a <c>dataChanged</c>.
        /// </summary>
        public void RevertAll()
        {
            EnsureRowsSynced();
            bool hasRows = slots.Any(slot => slot.Operation != RowOperation.None);
            if (pending.Count == 0 && !hasRows)
                return;

            for (int row = slots.Count - 1; row >= 0; row--)
            {
                RowSlot slot = slots[row];
                if (slot.Operation == RowOperation.Insert)
                {
                    BeginRemoveRows(default, row, row);
                    slots.RemoveAt(row);
                    EndRemoveRows();
                }
                else if (slot.Operation == RowOperation.Delete)
                {
                    slot.Operation = RowOperation.None;
                    DataChanged(Index(row, 0), Index(row, ColumnCount() - 1));
                    // The marker goes back to the row number. Qt's revertCachedRow leaves this to
                    // the next repaint; emitting it makes the revert visible immediately.
                    HeaderDataChangedSignal.Emit(Qt.Orientation.Vertical, row, row);
                }
            }

            var editedBaseRows = pending.Keys
                .Select(entry => entry.Row).Distinct().OrderBy(row => row).ToList();
            pending.Clear();

            int lastColumn = ColumnCount() - 1;
            if (lastColumn < 0)
                return;
            foreach (int baseRow in editedBaseRows)
            {
                int logicalRow = slots.FindIndex(slot =>
                    slot.BaseRow == baseRow && slot.Operation == RowOperation.None);
                if (logicalRow >= 0)
                    DataChanged(Index(logicalRow, 0), Index(logicalRow, lastColumn));
            }
        }

        static string Quote(string identifier)
            => "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }
}
