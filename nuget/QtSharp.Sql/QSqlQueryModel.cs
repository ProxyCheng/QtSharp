using System;
using System.Collections.Generic;
using System.Data.Common;
using QtSharp.QtCore;

namespace QtSharp.QtSql
{
    /// <summary>
    /// The C# counterpart of QtSql's <c>QSqlQueryModel</c>: a read-only
    /// <see cref="QAbstractTableModel"/> over a SQL result set, materialized through ADO.NET so the
    /// data survives the reader. Set a <see cref="DbConnection"/>, run a query, and pass the model
    /// to any Qt view - no Qt SQL module is involved.
    ///
    /// Values cross into Qt through <see cref="QVariant"/>; a NULL is an invalid QVariant, and the
    /// few ADO.NET types QVariant has no direct mapping for (decimal, DateTimeOffset, TimeSpan)
    /// are converted.
    /// </summary>
    public class QSqlQueryModel : QAbstractTableModel
    {
        readonly List<string> columns = new();
        readonly List<object[]> rows = new();
        DbConnection connection;
        string lastQuery;
        (string Name, object Value)[] lastParameters = Array.Empty<(string, object)>();

        public QSqlQueryModel()
        {
        }

        /// <summary>The connection the model reads from.</summary>
        protected DbConnection Connection => connection;

        /// <summary>The result set's column names, in SELECT order.</summary>
        public IReadOnlyList<string> Columns => columns;

        /// <summary>The index of a column name, or -1.</summary>
        protected int ColumnIndex(string name)
        {
            for (int i = 0; i < columns.Count; i++)
                if (string.Equals(columns[i], name, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        /// <summary>Sets the connection; call it before <see cref="SetQuery"/>.</summary>
        public void SetConnection(DbConnection connection)
            => this.connection = connection ?? throw new ArgumentNullException(nameof(connection));

        /// <summary>
        /// Runs the query and replaces the model's rows. Parameters are ADO.NET named parameters
        /// (<c>@name</c>); a null value becomes DBNull.
        /// </summary>
        public bool SetQuery(string query, params (string Name, object Value)[] parameters)
        {
            if (connection == null)
                throw new InvalidOperationException("SetConnection must be called before SetQuery.");
            lastQuery = query ?? throw new ArgumentNullException(nameof(query));
            lastParameters = parameters ?? Array.Empty<(string, object)>();
            return Execute();
        }

        /// <summary>Runs the last query again.</summary>
        public bool Select() => lastQuery != null && Execute();

        /// <summary>Reloads the model (the counterpart of QSqlQueryModel::refresh).</summary>
        public void Refresh() => Select();

        bool Execute()
        {
            if (connection.State != System.Data.ConnectionState.Open)
                connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = lastQuery;
            foreach (var (name, value) in lastParameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }

            using var reader = command.ExecuteReader();
            var newColumns = new List<string>(reader.FieldCount);
            for (int i = 0; i < reader.FieldCount; i++)
                newColumns.Add(reader.GetName(i));
            var newRows = new List<object[]>();
            while (reader.Read())
            {
                var row = new object[reader.FieldCount];
                reader.GetValues(row);
                newRows.Add(row);
            }

            BeginResetModel();
            columns.Clear();
            columns.AddRange(newColumns);
            rows.Clear();
            rows.AddRange(newRows);
            EndResetModel();
            return true;
        }

        // Qt calls rowCount()/columnCount() with a default-constructed (invalid) QModelIndex for
        // the root, not with null; a valid parent means "child of a cell", which this flat model
        // does not have.
        public override int RowCount(QModelIndex parent = null)
            => parent == null || !parent.IsValid() ? rows.Count : 0;

        public override int ColumnCount(QModelIndex parent = null)
            => parent == null || !parent.IsValid() ? columns.Count : 0;

        public override QVariant Data(QModelIndex index, int role = default)
        {
            // Qt::DisplayRole is 0; every other role has no data here.
            if (role != 0 || index == null)
                return new QVariant();
            return CellValue(index.Row(), index.Column());
        }

        /// <summary>The cell as Qt sees it; a subclass may overlay pending edits.</summary>
        protected virtual QVariant CellValue(int row, int column)
            => ToVariant(RawCell(row, column));

        /// <summary>The cell's value as ADO.NET handed it over.</summary>
        protected object RawCell(int row, int column)
        {
            if (row < 0 || row >= rows.Count || column < 0 || column >= columns.Count)
                return null;
            return rows[row][column];
        }

        public override QVariant HeaderData(int section, Qt.Orientation orientation, int role = default)
        {
            if (role != 0)
                return new QVariant();
            if (orientation == Qt.Orientation.Horizontal && section >= 0 && section < columns.Count)
                return QVariant.From(columns[section]);
            if (orientation == Qt.Orientation.Vertical && section >= 0 && section < rows.Count)
                return QVariant.From(section + 1);
            return new QVariant();
        }

        /// <summary>The value of one cell, by column name.</summary>
        public object Value(int row, string columnName)
        {
            int column = ColumnIndex(columnName);
            return column < 0 ? null : Unwrap(RawCell(row, column));
        }

        /// <summary>A row as a plain dictionary (the counterpart of QSqlRecord).</summary>
        public IReadOnlyDictionary<string, object> Record(int row)
        {
            var result = new Dictionary<string, object>(columns.Count, StringComparer.Ordinal);
            for (int i = 0; i < columns.Count; i++)
                result[columns[i]] = Unwrap(RawCell(row, i));
            return result;
        }

        static object Unwrap(object value) => value == DBNull.Value ? null : value;

        internal static QVariant ToVariant(object value)
        {
            if (value == null || value == DBNull.Value)
                return new QVariant();
            switch (value)
            {
                // QVariant has no decimal/DateTimeOffset/TimeSpan mapping; the closest Qt types
                // are double, DateTime and a string.
                case decimal decimalValue: return QVariant.From((double)decimalValue);
                case DateTimeOffset offset: return QVariant.From(offset.DateTime);
                case TimeSpan time: return QVariant.From(time.ToString());
                default: return QVariant.From(value);
            }
        }
    }
}
