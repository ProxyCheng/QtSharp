using System.Collections.Generic;

namespace QtSharp.Charts.Tests
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
    /// QVariant. That is left out: QVariant's colour constructor is its *template* one, which the
    /// binding cannot materialise (the same gap that keeps QComboBox::addItem(QString, QVariant) down
    /// to the types QVariant has real constructors for). The mapping itself is unaffected.
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

        public override int RowCount(QModelIndex parent = null) => _rows.Count;

        public override int ColumnCount(QModelIndex parent = null)
            => ColumnCountValue > 0 ? ColumnCountValue : (_rows.Count > 0 ? _rows[0].Length : 0);

        public override QVariant HeaderData(int section, Qt.Orientation orientation, int role = default)
        {
            if (role != (int)Qt.ItemDataRole.DisplayRole)
                return new QVariant();
            if (orientation == Qt.Orientation.Horizontal)
                return new QVariant(_headers != null && section < _headers.Length ? _headers[section] : $"201{section}");
            return new QVariant((section + 1).ToString());
        }

        public override QVariant Data(QModelIndex index, int role = default)
        {
            if (index.Row() < 0 || index.Row() >= _rows.Count
                || index.Column() < 0 || index.Column() >= _rows[index.Row()].Length)
                return new QVariant();
            if (role == (int)Qt.ItemDataRole.DisplayRole || role == (int)Qt.ItemDataRole.EditRole)
                return new QVariant(_rows[index.Row()][index.Column()]);
            return new QVariant();
        }

        public override bool SetData(QModelIndex index, QVariant value, int role = default)
        {
            if (!index.IsValid() || role != (int)Qt.ItemDataRole.EditRole)
                return false;
            _rows[index.Row()][index.Column()] = value.ToDouble();
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
