using System;
using System.Collections.Generic;

namespace QtSharp.Tests
{
    /// <summary>
    /// QVariant's object-shaped API, the PyQt/PySide contract: a Qt value type crosses as a real
    /// Qt value, a QObject as QObject*, and anything else as an opaque managed value that survives
    /// the copies Qt makes. The model-role checks are the case that did not work before: a C#
    /// QAbstractItemModel could not put a QColor in a BackgroundRole.
    /// </summary>
    [Test("test-variantobject")]
    public static class VariantObjectTests
    {
        sealed class Payload
        {
            public int Value;
        }

        sealed class RoleModel : QAbstractTableModel
        {
            public override int RowCount(QModelIndex parent = default) => 1;
            public override int ColumnCount(QModelIndex parent = default) => 1;

            public override object Data(QModelIndex index, int role = default)
            {
                // Returning the Qt value directly is the point: no QVariant constructor has to be
                // named at the call site, and Qt receives a real QColor/QIcon variant.
                if (role == (int)Qt.ItemDataRole.BackgroundRole)
                    return new QColor(210, 225, 255);
                if (role == (int)Qt.ItemDataRole.DecorationRole)
                    return new QIcon();
                return null;
            }
        }

        public static int Run(TestContext context)
        {

            // ---------------------------------------------------------------- Qt value types
            var color = new QColor(10, 20, 30, 40);
            {
                QVariant variant = QVariant.From(color);   // object -> the real Qt QColor variant
                var metaType = variant.MetaType();
                context.Check(metaType.Name() == "QColor",
                    $"a QColor becomes a real QColor variant (metatype '{metaType.Name()}')");

                var back = variant.ToObject();
                context.Check(back is QColor b && b.Red() == 10 && b.Green() == 20
                      && b.Blue() == 30 && b.Alpha() == 40,
                    "ToObject gives the QColor back");

                var generic = variant.To<QColor>();
                context.Check(generic.IsValid() && generic.Blue() == 30, "To<QColor>() gives the QColor back");

                using (var explicitVariant = new QVariant(color))
                    context.Check(explicitVariant.ToObject() is QColor, "new QVariant(color) uses the same path");
            }

            using (var brush = new QBrush(new QColor(1, 2, 3)))
            {
                QVariant variant = QVariant.From(brush);
                context.Check(variant.ToObject() is QBrush, "a QBrush round-trips as a QBrush variant");
            }

            using (var icon = new QIcon())
            {
                QVariant variant = QVariant.From(icon);
                context.Check(variant.ToObject() is QIcon, "a QIcon round-trips as a QIcon variant");
            }

            // ---------------------------------------------------------------- opaque managed values
            var payload = new Payload { Value = 42 };
            using (var opaque = new QVariant(payload))
            {
                context.Check(ReferenceEquals(opaque.ToObject(), payload),
                    "an unmapped managed object comes back by reference");
                context.Check(ReferenceEquals(opaque.To<Payload>(), payload), "To<Payload>() unwraps it");
            }

            // Qt copies the QVariant into its own storage (QComboBox item data). Disposing the
            // original must not release the object while that copy still exists.
            using (var combo = new QComboBox())
            using (var opaque = new QVariant(payload))
            {
                combo.AddItem("payload", opaque);
                opaque.Dispose();
                context.Check(ReferenceEquals(combo.ItemData(0, (int)Qt.ItemDataRole.UserRole), payload),
                    "a Qt-owned QVariant copy keeps the managed object alive");
            }

            // ---------------------------------------------------------------- model roles
            using (var view = new QTableView())
            {
                view.SetModel(new RoleModel());
                var model = view.Model();
                var backgroundColor = model.Data(model.Index(0, 0),
                           (int)Qt.ItemDataRole.BackgroundRole);
                {
                    context.Check(backgroundColor is QColor bg && bg.Red() == 210
                          && bg.Green() == 225 && bg.Blue() == 255,
                        "a managed model returns a QColor through BackgroundRole");
                }

                var decoration = model.Data(model.Index(0, 0),
                           (int)Qt.ItemDataRole.DecorationRole) as QIcon;
                    context.Check(decoration != null,
                        "a managed model returns a QIcon through DecorationRole");
            }

            // ---------------------------------------------------------------- generic BCL spelling
            using (var number = QVariant.From(42))
                context.Check(number.To<int>() == 42, "To<int>() unwraps a primitive");

            // ---------------------------------------------------------------- qvariant.h's own types
            // These are the types QVariant declares a constructor for. They used to fall through
            // to the opaque managed payload, which still round-tripped through From()/ToObject() -
            // only the metatype check catches that, which is why the regression hid.
            using (var point = QVariant.From(new QPoint(3, 4)))
            {
                var metaType = point.MetaType();
                context.Check(metaType.Name() == "QPoint",
                    $"a QPoint becomes a real QPoint variant (metatype '{metaType.Name()}')");
                // ToObject() still prefers the BCL geometry for the four types that have one, the
                // way it does for QDate -> DateOnly; To<T>() reaches the Qt type.
                context.Check(point.ToObject() is System.Drawing.Point fromObject
                      && fromObject.X == 3 && fromObject.Y == 4,
                    "ToObject gives the BCL Point");
                context.Check(point.To<QPoint>().X() == 3, "To<QPoint>() gives the Qt type back");
            }

            using (var line = QVariant.From(new QLine(0, 1, 2, 3)))
            {
                var metaType = line.MetaType();
                context.Check(metaType.Name() == "QLine",
                    $"a QLine becomes a real QLine variant (metatype '{metaType.Name()}')");
            }

            using (var date = QVariant.From(new QDate(2026, 10, 27)))
            {
                var metaType = date.MetaType();
                context.Check(metaType.Name() == "QDate",
                    $"a QDate becomes a real QDate variant (metatype '{metaType.Name()}')");
                context.Check(date.ToObject() is DateOnly day && day == new DateOnly(2026, 10, 27),
                    "ToObject still prefers the BCL DateOnly");
            }

            using (var url = QVariant.From(new QUrl("https://example.test/x")))
            {
                var metaType = url.MetaType();
                context.Check(metaType.Name() == "QUrl",
                    $"a QUrl becomes a real QUrl variant (metatype '{metaType.Name()}')");
                context.Check(url.ToObject() is Uri uri && uri.AbsoluteUri.StartsWith("https://example.test/x"),
                    "ToObject gives the BCL Uri");
                using (var back = url.To<QUrl>())
                    context.Check(back != null && back.Scheme() == "https",
                        "To<QUrl>() reaches the Qt type itself");
            }

            using (var index = QVariant.From(new QModelIndex()))
            {
                var metaType = index.MetaType();
                context.Check(metaType.Name() == "QModelIndex",
                    $"a QModelIndex becomes a real QModelIndex variant (metatype '{metaType.Name()}')");
            }

            // QVector3D is trivially copyable and qvariant.h has no constructor for it - only the
            // conversion operator - so the generated glue carries both directions.
            using (var vector = QVariant.From(new QVector3D(1, 2, 3)))
            {
                var metaType = vector.MetaType();
                context.Check(metaType.Name() == "QVector3D",
                    $"a QVector3D becomes a real QVector3D variant (metatype '{metaType.Name()}')");
                context.Check(vector.ToObject() is QVector3D back && back.Y() == 2 && back.Z() == 3,
                    "and comes back as the struct");
            }

            // ---------------------------------------------------------------- small values
            using (var character = QVariant.From('A'))
            {
                var metaType = character.MetaType();
                context.Check(metaType.Name() == "QChar",
                    $"a char becomes a QChar variant (metatype '{metaType.Name()}')");
                context.Check(character.ToObject() is char letter && letter == 'A', "and comes back as the char");
            }

            using (var role = QVariant.From(Qt.ItemDataRole.DisplayRole))
                context.Check(role.type() == (int)QVariant.Type.Int && role.To<int>() == 0,
                    "an enum becomes an int variant, as in PySide");

            using (var small = QVariant.From((byte)42))
                context.Check(small.To<int>() == 42, "a byte widens to int");

            // ---------------------------------------------------------------- containers
            using (var list = QVariant.From(new object[] { 1, "two", new QLine(1, 2, 3, 4) }))
            {
                var metaType = list.MetaType();
                context.Check(metaType.Name() == "QVariantList",
                    $"an object[] becomes a QVariantList (metatype '{metaType.Name()}')");
                var back = list.ToObject() as object[];
                context.Check(back != null && back.Length == 3
                      && back[0] is int number && number == 1
                      && back[1] is string text && text == "two"
                      && back[2] is QLine line && line.X1() == 1,
                    "and unwraps recursively");
            }

            using (var numbers = QVariant.From(new[] { 1, 2, 3 }))
            {
                var back = numbers.ToObject() as object[];
                context.Check(back != null && back.Length == 3
                      && back[0] is int first && first == 1,
                    "an int[] becomes a QVariantList too, not an opaque payload");
            }

            using (var counts = QVariant.From(new Dictionary<string, int> { ["a"] = 1 }))
            {
                var metaType = counts.MetaType();
                context.Check(metaType.Name() == "QVariantMap",
                    $"a strongly typed Dictionary<string, int> becomes a QVariantMap (metatype '{metaType.Name()}')");
            }

            using (var map = QVariant.From(new Dictionary<string, object>
                   {
                       ["count"] = 2,
                       ["line"] = new QLine(5, 6, 7, 8),
                   }))
            {
                var metaType = map.MetaType();
                context.Check(metaType.Name() == "QVariantMap",
                    $"a dictionary becomes a QVariantMap (metatype '{metaType.Name()}')");
                var back = map.ToObject() as Dictionary<string, object>;
                context.Check(back != null
                      && back["count"] is int count && count == 2
                      && back["line"] is QLine line && line.Y2() == 8,
                    "and unwraps recursively");
            }

            return context.ExitCode;
        }
    }
}
