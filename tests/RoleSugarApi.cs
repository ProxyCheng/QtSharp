using System;
using System.Collections.Generic;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtWidgets;

namespace QtSharp.Tests
{
    /// <summary>
    /// The role-taking API spelled with the named enum. Qt declares the role as an <c>int</c>
    /// (roles are an open namespace: a custom role is Qt.UserRole + n), so the primary overload
    /// keeps that spelling and these forward from <see cref="Qt.ItemDataRole"/>. The point is that
    /// a call site no longer needs <c>(int)</c> for the standard roles.
    /// </summary>
    [Demo("test-rolesugar")]
    public static class RoleSugarTests
    {
        public static int Run(QApplication app)
        {
            int failures = 0;
            void Check(bool ok, string what)
            {
                Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
                if (!ok)
                    failures++;
            }

            // ---------------------------------------------------------------- model + item
            using (var model = new QStandardItemModel())
            using (var item = new QStandardItem("cell"))
            {
                model.SetItem(0, 0, item);
                var cellIndex = model.Index(0, 0);
                Check(model.Data(cellIndex, Qt.ItemDataRole.DisplayRole) as string == "cell",
                    "QStandardItemModel.Data takes Qt.ItemDataRole");
                Check(cellIndex.Data(Qt.ItemDataRole.DisplayRole) as string == "cell",
                    "QModelIndex.Data takes Qt.ItemDataRole");
                Check(model.SetData(cellIndex, "edited", Qt.ItemDataRole.EditRole),
                    "SetData takes Qt.ItemDataRole");
                Check(cellIndex.Data(Qt.ItemDataRole.EditRole) as string == "edited",
                    "and the edit reached the cell");

                Check(model.SetHeaderData(0, Qt.Orientation.Horizontal, "H", Qt.ItemDataRole.DisplayRole),
                    "SetHeaderData takes Qt.ItemDataRole");
                Check(model.HeaderData(0, Qt.Orientation.Horizontal, Qt.ItemDataRole.DisplayRole) as string == "H",
                    "HeaderData takes Qt.ItemDataRole");

                var rolesIndex = model.Index(0, 0);
                Check(model.SetItemData(rolesIndex, new Dictionary<Qt.ItemDataRole, object>
                {
                    [Qt.ItemDataRole.UserRole] = 42,
                    [Qt.ItemDataRole.ToolTipRole] = "tip",
                }), "SetItemData takes an enum-keyed dictionary");
                var roles = model.ItemDataByRole(rolesIndex);
                Check(roles.TryGetValue(Qt.ItemDataRole.UserRole, out var number) && number is int n && n == 42,
                    $"ItemDataByRole reads by the named role (got {number})");
                Check(roles.TryGetValue(Qt.ItemDataRole.ToolTipRole, out var tip) && tip as string == "tip",
                    "and carries the other role too");

                item.SetData(7, Qt.ItemDataRole.UserRole);
                Check(item.Data(Qt.ItemDataRole.UserRole) is int itemValue && itemValue == 7,
                    "QStandardItem.Data/SetData take Qt.ItemDataRole");

                // A custom role keeps the int spelling - that is why it stays the primary overload.
                var customIndex = model.Index(0, 0);
                int customRole = (int)Qt.ItemDataRole.UserRole + 1;
                Check(model.SetData(customIndex, "custom", customRole), "a custom role still uses int");
                Check(model.Data(customIndex, customRole) as string == "custom",
                    "and reads back through the int overload");
            }

            // ---------------------------------------------------------------- combo box
            using (var combo = new QComboBox())
            {
                combo.AddItem("one", 11);
                combo.AddItem("two", 22);
                Check(combo.ItemData(0, Qt.ItemDataRole.UserRole) is int first && first == 11,
                    "QComboBox.ItemData takes Qt.ItemDataRole");
                Check(combo.CurrentData(Qt.ItemDataRole.UserRole) is int current && current == 11,
                    "CurrentData takes Qt.ItemDataRole");
                combo.SetItemData(0, 99, Qt.ItemDataRole.UserRole);
                Check(combo.ItemData(0, Qt.ItemDataRole.UserRole) is int edited && edited == 99,
                    "SetItemData takes Qt.ItemDataRole");
                Check(combo.FindData(99, Qt.ItemDataRole.UserRole) == 0,
                    "FindData takes Qt.ItemDataRole");
            }

            // ---------------------------------------------------------------- widget items
            using (var list = new QListWidget())
            {
                var listItem = new QListWidgetItem("row", list);
                listItem.SetData(Qt.ItemDataRole.UserRole, 5);
                Check(listItem.Data(Qt.ItemDataRole.UserRole) is int value && value == 5,
                    "QListWidgetItem.Data/SetData take Qt.ItemDataRole");
            }

            using (var table = new QTableWidget(1, 1))
            {
                var tableItem = new QTableWidgetItem("cell");
                table.SetItem(0, 0, tableItem);
                tableItem.SetData(Qt.ItemDataRole.UserRole, 6);
                Check(tableItem.Data(Qt.ItemDataRole.UserRole) is int value && value == 6,
                    "QTableWidgetItem.Data/SetData take Qt.ItemDataRole");
            }

            using (var tree = new QTreeWidget())
            {
                var treeItem = new QTreeWidgetItem(new[] { "column" });
                tree.AddTopLevelItem(treeItem);
                treeItem.SetData(0, Qt.ItemDataRole.UserRole, 8);
                Check(treeItem.Data(0, Qt.ItemDataRole.UserRole) is int value && value == 8,
                    "QTreeWidgetItem.Data/SetData take Qt.ItemDataRole");
            }

            // ---------------------------------------------------------------- roles stored as properties
            using (var proxy = new QSortFilterProxyModel())
            {
                proxy.SetSortRole(Qt.ItemDataRole.DisplayRole);
                Check(proxy.SortRole == (int)Qt.ItemDataRole.DisplayRole, "SetSortRole takes Qt.ItemDataRole");
                proxy.SetFilterRole(Qt.ItemDataRole.EditRole);
                Check(proxy.FilterRole == (int)Qt.ItemDataRole.EditRole, "SetFilterRole takes Qt.ItemDataRole");
            }

            using (var completer = new QCompleter())
            {
                completer.SetCompletionRole(Qt.ItemDataRole.DisplayRole);
                Check(completer.CompletionRole == (int)Qt.ItemDataRole.DisplayRole,
                    "QCompleter.SetCompletionRole takes Qt.ItemDataRole");
            }

            // ---------------------------------------------------------------- QFont::Weight
            using (var format = new QTextCharFormat())
            {
                format.SetFontWeight(QFont.Weight.Bold);
                Check(format.FontWeight() == (int)QFont.Weight.Bold,
                    "QTextCharFormat.SetFontWeight takes QFont.Weight");
            }

            Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[FAIL] {failures} check(s) failed");
            return failures == 0 ? 0 : 1;
        }
    }
}
