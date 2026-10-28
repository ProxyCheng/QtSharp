using System;
using QtSharp.Qt3DCore;

// The one canonical C# -> Qt type-name map. MetaType.QtName is what a [QtSignal]/[QtProperty]
// turns a C# type into, and QMetaType.FromType<T> asks the same mapper before QMetaType.FromName,
// so a declared member and a metatype can never disagree about a type's name.
//
// The three bugs this pins down:
//   * a value class is not a pointer: QFont, not QFont*;
//   * a bound class keeps its C++ namespace: Qt3DCore::QEntity*, not QEntity*;
//   * an unsupported type fails loudly instead of being spelled from t.Name.
public class TypeNameObject : QObject
{
    [QtSignal] public event Action<QFont> FontChanged;
    [QtSignal] public event Action<QByteArray> DataChanged;

    // The compiler only counts a field-like event as used when its own class invokes it; the test
    // reads the two signatures out of the meta-object and never emits.
    public void Raise(QFont font, QByteArray data)
    {
        FontChanged?.Invoke(font);
        DataChanged?.Invoke(data);
    }
}

[Demo("test-metatype")]
public static class MetaTypeApi
{
    // A QObject subclass the binding did not generate. Its meta-object is named after the C#
    // type, and like any QObject it crosses as a pointer.
    sealed class ManagedObject : QObject
    {
    }

    sealed class NotAQtType
    {
    }

    public static int Run(QApplication app)
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        // ---------------------------------------------------------------- the intrinsic table
        Check(MetaType.QtName(typeof(string)) == "QString", "string is QString");
        Check(MetaType.QtName(typeof(int)) == "int", "int is int");
        Check(MetaType.QtName(typeof(bool)) == "bool", "bool is bool");
        Check(MetaType.QtName(typeof(long)) == "qint64", "long is qint64");
        Check(MetaType.QtName(typeof(byte)) == "uchar", "byte is uchar");

        // The BCL spellings the rest of the binding already crosses as a Qt value.
        Check(MetaType.QtName(typeof(char)) == "QChar", "char is QChar");
        Check(MetaType.QtName(typeof(DateTime)) == "QDateTime", "DateTime is QDateTime");
        Check(MetaType.QtName(typeof(DateOnly)) == "QDate", "DateOnly is QDate");
        Check(MetaType.QtName(typeof(TimeOnly)) == "QTime", "TimeOnly is QTime");
        Check(MetaType.QtName(typeof(Guid)) == "QUuid", "Guid is QUuid");
        Check(MetaType.QtName(typeof(Uri)) == "QUrl", "Uri is QUrl");
        Check(MetaType.QtName(typeof(byte[])) == "QByteArray", "byte[] is QByteArray");

        // ---------------------------------------------------------------- bound value classes
        // The generated table knows these are value classes, not object pointers, so the name
        // carries no '*' (the old IQtHandle fallback said QFont*, QByteArray*).
        Check(MetaType.QtName(typeof(QFont)) == "QFont", $"QFont is QFont (got '{MetaType.QtName(typeof(QFont))}')");
        Check(MetaType.QtName(typeof(QByteArray)) == "QByteArray",
            $"QByteArray is QByteArray (got '{MetaType.QtName(typeof(QByteArray))}')");
        Check(MetaType.QtName(typeof(QIcon)) == "QIcon", $"QIcon is QIcon (got '{MetaType.QtName(typeof(QIcon))}')");
        Check(MetaType.QtName(typeof(QColorSpace)) == "QColorSpace",
            $"QColorSpace is QColorSpace (got '{MetaType.QtName(typeof(QColorSpace))}')");

        // A runtime struct is a value with no handle at all.
        Check(MetaType.QtName(typeof(QColor)) == "QColor", $"QColor is QColor (got '{MetaType.QtName(typeof(QColor))}')");

        // ---------------------------------------------------------------- QObject pointers
        Check(MetaType.QtName(typeof(QObject)) == "QObject*", "QObject is a pointer metatype");
        Check(MetaType.QtName(typeof(QWidget)) == "QWidget*", "a bound QObject subclass is a pointer metatype");
        Check(MetaType.QtName(typeof(ManagedObject)) == "ManagedObject*",
            $"a QObject subclass declared in C# is named after its meta-object (got '{MetaType.QtName(typeof(ManagedObject))}')");

        // The generated table's exact cppName keeps the namespace Qt3D declares.
        Check(MetaType.QtName(typeof(QEntity)) == "Qt3DCore::QEntity*",
            $"a namespaced class keeps its namespace (got '{MetaType.QtName(typeof(QEntity))}')");

        // A bound enum keeps the name QMetaType registered it under.
        Check(MetaType.QtName(typeof(Qt.Alignment)) == "Qt::Alignment",
            $"an enum keeps its Qt spelling (got '{MetaType.QtName(typeof(Qt.Alignment))}')");

        // ---------------------------------------------------------------- unsupported types fail
        Check(Throws(() => MetaType.QtName(typeof(NotAQtType))),
            "an unsupported type throws instead of being spelled from t.Name");
        Check(Throws(() => QMetaType.FromType<NotAQtType>()),
            "QMetaType.FromType<T> fails the same way for an unsupported type");

        // ---------------------------------------------------------------- the declared members agree
        var declared = new TypeNameObject();
        var meta = declared.MetaObject();
        Check(meta.IndexOfSignal("fontChanged(QFont)") >= 0,
            "a [QtSignal] carrying a value class spells it without a pointer");
        Check(meta.IndexOfSignal("dataChanged(QByteArray)") >= 0,
            "and the same for another value class");

        // ---------------------------------------------------------------- QMetaType.FromType<T>
        Check(Matches<string>("QString"), "FromType<string>() is QString");
        Check(Matches<int>("int"), "FromType<int>() is int");
        Check(Matches<char>("QChar"), "FromType<char>() is QChar");
        Check(Matches<DateTime>("QDateTime"), "FromType<DateTime>() is QDateTime");
        Check(Matches<QColor>("QColor"), "FromType<QColor>() is QColor");
        Check(Matches<QFont>("QFont"), "FromType<QFont>() is QFont");
        Check(Matches<QByteArray>("QByteArray"), "FromType<QByteArray>() is QByteArray");
        Check(Matches<QObject>("QObject*"), "FromType<QObject>() is QObject*");

        // Qt resolves a name only once its metatype is registered; Qt3DCore does not register
        // QEntity*, so this is an invalid QMetaType. What the call does prove is that FromType
        // reaches the generated table for the class at all (an unknown class throws, as above),
        // and the spelling it feeds FromName is pinned by the mapper check above.
        int entityId = QMetaType.FromType<QEntity>().Id();
        Check(entityId == QMetaType.Type("Qt3DCore::QEntity*"),
            $"FromType<QEntity>() looks the metatype up by Qt3DCore::QEntity* (got {entityId})");

        return failures;
    }

    // Qt has to know the metatype under exactly the name the mapper gave it. A zero id would mean
    // neither side found anything, which would compare equal and prove nothing.
    static bool Matches<T>(string qtName)
    {
        int id = QMetaType.FromType<T>().Id();
        return id != 0 && id == QMetaType.Type(qtName);
    }

    static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (NotSupportedException)
        {
            return true;
        }
    }
}
