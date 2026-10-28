using System.Collections.Generic;

namespace QtSharp.Charts.Examples
{
    /// <summary>
    /// The editable table model behind the two model-mapper examples: a grid of numbers with a header
    /// row and column, which is all a QVBarModelMapper or QVXYModelMapper needs to drive a series.
    ///
    /// Both Qt examples write this model in C++ (BarModelMapperModel, ModelDataModel); it is written
    /// in C# here, which is the point of a model mapper - the model is yours and the series follows
    /// it.
    ///
    /// The examples also paint each mapped column through Qt::BackgroundRole, returning a QColor in a
    /// QVariant. That path now works - QColor has its Qt metatype, so the model returns a real QColor
    /// variant. The mapping itself is unaffected.
    /// </summary>
    internal sealed class MapperTableModel : QAbstractTableModel
    {
        readonly List<double[]> _rows = new();
        readonly string[] _headers;

        public MapperTableModel(int rowCount, int columnCount, string[] headers = null)
        {
            _headers = headers;
            var random = QRandomGenerator.Global();
            for (int i = 0; i < rowCount; i++)
            {
                var row = new double[columnCount];
                for (int k = 0; k < columnCount; k++)
                    row[k] = k % 2 == 0 ? i * 50 + random.Bounded(20) : random.Bounded(100);
                _rows.Add(row);
            }
        }

        public int ColumnCountValue { get; set; }

        public override int RowCount(QModelIndex parent = default) => _rows.Count;

        public override int ColumnCount(QModelIndex parent = default)
            => ColumnCountValue > 0 ? ColumnCountValue : (_rows.Count > 0 ? _rows[0].Length : 0);

        public override object HeaderData(int section, Qt.Orientation orientation, int role = default)
        {
            if (role != (int)Qt.ItemDataRole.DisplayRole)
                return null;
            if (orientation == Qt.Orientation.Horizontal)
                return _headers != null && section < _headers.Length ? _headers[section] : $"201{section}";
            return (section + 1).ToString();
        }

        public override object Data(QModelIndex index, int role = default)
        {
            if (index.Row() < 0 || index.Row() >= _rows.Count
                || index.Column() < 0 || index.Column() >= _rows[index.Row()].Length)
                return null;
            if (role == (int)Qt.ItemDataRole.DisplayRole || role == (int)Qt.ItemDataRole.EditRole)
                return _rows[index.Row()][index.Column()];
            // The Qt examples colour the mapped columns through this role. The value crosses as a
            // real QColor variant, which Qt's delegate uses directly.
            if (role == (int)Qt.ItemDataRole.BackgroundRole)
                return index.Column() is 1 or 2
                    ? new QColor(192, 80, 77)
                    : new QColor(79, 129, 189);
            return null;
        }

        public override bool SetData(QModelIndex index, object value, int role = default)
        {
            if (!index.IsValid() || role != (int)Qt.ItemDataRole.EditRole)
                return false;
            _rows[index.Row()][index.Column()] = System.Convert.ToDouble(value);
            // dataChanged is QAbstractItemModel's own emit entry point (its signal carries a
            // QPrivateSignal, so there is no QSignal property for it).
            DataChanged(index, index);
            return true;
        }

        public override Qt.ItemFlags Flags(QModelIndex index)
            => base.Flags(index) | Qt.ItemFlags.ItemIsEditable;

        /// <summary>What the table holds, so a test can check a cell without going through Qt.</summary>
        public double Cell(int row, int column) => _rows[row][column];
    }
}
