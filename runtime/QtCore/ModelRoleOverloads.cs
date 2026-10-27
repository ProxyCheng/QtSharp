using System.Collections.Generic;

// Qt's item-model API takes the role as an int: roles are an open namespace (a custom role is
// Qt.UserRole + n), so the C++ header cannot use Qt::ItemDataRole. C# enums do not convert to int
// implicitly, so these overloads take the named enum and forward to the int spelling; a custom
// role keeps using the int overload.
namespace QtSharp.QtCore
{
    public partial struct QModelIndex
    {
        public object Data(Qt.ItemDataRole role) => Data((int)role);
    }

    public partial class QPersistentModelIndex
    {
        public object Data(Qt.ItemDataRole role) => Data((int)role);
    }

    public partial class QAbstractItemModel
    {
        public object Data(QModelIndex index, Qt.ItemDataRole role)
            => Data(index, (int)role);

        public bool SetData(QModelIndex index, object value, Qt.ItemDataRole role)
            => SetData(index, value, (int)role);

        public object HeaderData(int section, Qt.Orientation orientation, Qt.ItemDataRole role)
            => HeaderData(section, orientation, (int)role);

        public bool SetHeaderData(int section, Qt.Orientation orientation, object value, Qt.ItemDataRole role)
            => SetHeaderData(section, orientation, value, (int)role);

        public QModelIndex[] Match(QModelIndex start, Qt.ItemDataRole role, object value,
                                   int hits = 1,
                                   Qt.MatchFlags flags = Qt.MatchFlags.MatchStartsWith | Qt.MatchFlags.MatchWrap)
            => Match(start, (int)role, value, hits, flags);

        /// <summary>The dictionary form of <see cref="SetItemData(QModelIndex, Dictionary{int, object})"/>.</summary>
        public bool SetItemData(QModelIndex index, IReadOnlyDictionary<Qt.ItemDataRole, object> roles)
            => SetItemData(index, RoleMap.ToInts(roles));

        /// <summary>The roles a cell carries, keyed by the named enum.</summary>
        public Dictionary<Qt.ItemDataRole, object> ItemDataByRole(QModelIndex index)
            => RoleMap.FromInts(ItemData(index));
    }

    public partial class QSortFilterProxyModel
    {
        public void SetFilterRole(Qt.ItemDataRole role) => SetFilterRole((int)role);

        public void SetSortRole(Qt.ItemDataRole role) => SetSortRole((int)role);
    }

    internal static class RoleMap
    {
        internal static Dictionary<int, object> ToInts(IReadOnlyDictionary<Qt.ItemDataRole, object> roles)
        {
            if (roles == null)
                return null;
            var result = new Dictionary<int, object>(roles.Count);
            foreach (var pair in roles)
                result[(int)pair.Key] = pair.Value;
            return result;
        }

        internal static Dictionary<Qt.ItemDataRole, object> FromInts(Dictionary<int, object> roles)
        {
            var result = new Dictionary<Qt.ItemDataRole, object>();
            if (roles != null)
                foreach (var pair in roles)
                    result[(Qt.ItemDataRole)pair.Key] = pair.Value;
            return result;
        }
    }
}
