using QtSharp.QtCore;

// The role overloads for the QtWidgets classes; see ModelRoleOverloads.cs in QtCore for why the
// int stays the primary spelling (custom roles are Qt.UserRole + n).
namespace QtSharp.QtWidgets
{
    public partial class QComboBox
    {
        public object CurrentData(Qt.ItemDataRole role) => CurrentData((int)role);

        public int FindData(object data, Qt.ItemDataRole role,
                            Qt.MatchFlags flags = Qt.MatchFlags.MatchExactly | Qt.MatchFlags.MatchCaseSensitive)
            => FindData(data, (int)role, flags);

        public object ItemData(int index, Qt.ItemDataRole role) => ItemData(index, (int)role);

        public void SetItemData(int index, object value, Qt.ItemDataRole role)
            => SetItemData(index, value, (int)role);
    }

    public partial class QCompleter
    {
        public void SetCompletionRole(Qt.ItemDataRole role) => SetCompletionRole((int)role);
    }

    public partial class QListWidgetItem
    {
        public object Data(Qt.ItemDataRole role) => Data((int)role);

        public void SetData(Qt.ItemDataRole role, object value) => SetData((int)role, value);
    }

    public partial class QTableWidgetItem
    {
        public object Data(Qt.ItemDataRole role) => Data((int)role);

        public void SetData(Qt.ItemDataRole role, object value) => SetData((int)role, value);
    }

    public partial class QTreeWidgetItem
    {
        public object Data(int column, Qt.ItemDataRole role) => Data(column, (int)role);

        public void SetData(int column, Qt.ItemDataRole role, object value)
            => SetData(column, (int)role, value);
    }

    public partial class QItemDelegate
    {
        public QRect Rect(QStyleOptionViewItem option, QModelIndex index, Qt.ItemDataRole role)
            => Rect(option, index, (int)role);
    }
}
