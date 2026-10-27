using System.Collections.Generic;
using QtSharp.QtCore;

// The role overloads for the QtGui model classes; see ModelRoleOverloads.cs in QtCore for why the
// int stays the primary spelling (custom roles are Qt.UserRole + n).
namespace QtSharp.QtGui
{
    public partial class QStandardItem
    {
        public object Data(Qt.ItemDataRole role) => Data((int)role);

        public void SetData(object value, Qt.ItemDataRole role) => SetData(value, (int)role);
    }

    public partial class QStandardItemModel
    {
        public void SetSortRole(Qt.ItemDataRole role) => SetSortRole((int)role);

        public void SetItemRoleNames(IReadOnlyDictionary<Qt.ItemDataRole, QByteArray> roleNames)
        {
            if (roleNames == null)
            {
                SetItemRoleNames((Dictionary<int, QByteArray>)null);
                return;
            }
            var roles = new Dictionary<int, QByteArray>(roleNames.Count);
            foreach (var pair in roleNames)
                roles[(int)pair.Key] = pair.Value;
            SetItemRoleNames(roles);
        }
    }

    public partial class QFileSystemModel
    {
        public object MyComputer(Qt.ItemDataRole role) => MyComputer((int)role);
    }

    public partial class QTextCharFormat
    {
        public void SetFontWeight(QFont.Weight weight) => SetFontWeight((int)weight);
    }
}
