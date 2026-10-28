using System;

// Dynamic invocation: a method reached through the meta-object instead of a C# call site.
//
// Qt's own public methods are the targets here (QLabel::setText, QSpinBox::setValue,
// QAbstractItemModel::rowCount and setData). They are exactly what a script or a plugin sees: a
// name in the meta-object, an argument list, and a result. The failure cases stay failures - an
// unknown name or a wrong argument type returns false with a reason, and no C++ frame is unwound.
[Test("test-metainvoke")]
public static class MetaInvokeApi
{
    public static int Run(TestContext context)
    {
        // ---------------------------------------------------------------- by name, a string
        var label = new QLabel();
        bool called = QMetaObject.InvokeMethod(label, "setText", Qt.ConnectionType.DirectConnection,
                                               new object[] { "set from the meta-object" }, out object textResult);
        context.Check(called && textResult == null && label.Text == "set from the meta-object",
            $"invokeMethod calls a QString slot by name (called={called}, text='{label.Text}')");

        // ---------------------------------------------------------------- by name, an int
        var spin = new QSpinBox();
        bool set = QMetaObject.InvokeMethod(spin, "setValue", Qt.ConnectionType.DirectConnection,
                                            new object[] { 42 }, out object intResult);
        context.Check(set && intResult == null && spin.Value == 42,
            $"invokeMethod calls an int slot by name (set={set}, value={spin.Value})");

        // ---------------------------------------------------------------- a return value
        // rowCount is Q_INVOKABLE and carries a defaulted QModelIndex parameter; invoke does not
        // apply C++ defaults, so the invalid index is passed explicitly. It crosses as a QVariant
        // and the shim converts it to the declared QModelIndex.
        var model = new QStringListModel(new[] { "alpha", "beta" });
        int rowCountIndex = model.MetaObject().IndexOfMethod("rowCount(QModelIndex)");
        context.Check(rowCountIndex >= 0, $"rowCount(QModelIndex) is in the meta-object (index {rowCountIndex})");
        var rowCount = model.MetaObject().Method(rowCountIndex);
        bool counted = rowCount.Invoke(model, new object[] { new QModelIndex() }, out object count);
        context.Check(counted && count is int rows && rows == 2,
            $"QMetaMethod.Invoke reads the return value back (counted={counted}, result={count})");

        bool named = QMetaObject.InvokeMethod(model, "rowCount", Qt.ConnectionType.DirectConnection,
                                              new object[] { new QModelIndex() }, out object namedCount);
        context.Check(named && namedCount is int namedRows && namedRows == 2,
            $"invokeMethod by name reads the same return value (named={named}, result={namedCount})");

        // ---------------------------------------------------------------- a QVariant parameter
        // setData takes (QModelIndex, QVariant, int): the model index and the string cross as
        // QVariants and are converted to the declared types, and the int role stays an int. The
        // call is virtual, so QStringListModel's override is the one that runs.
        QModelIndex first = model.Index(0, 0);
        bool changed = QMetaObject.InvokeMethod(model, "setData", Qt.ConnectionType.DirectConnection,
                                                new object[] { first, "alpha changed", (int)Qt.ItemDataRole.EditRole },
                                                out object changedResult);
        context.Check(changed && changedResult is bool wrote && wrote
                      && model.Index(0, 0).Data() as string == "alpha changed",
            $"a QVariant parameter receives the converted value (changed={changed}, data='{model.Index(0, 0).Data()}')");

        // ---------------------------------------------------------------- a missing member
        bool missingCalled = QMetaObject.InvokeMethod(label, "noSuchSlot", Qt.ConnectionType.DirectConnection,
                                                      Array.Empty<object>(), out object missingResult, out string missingError);
        context.Check(!missingCalled && missingResult == null && !string.IsNullOrEmpty(missingError),
            $"an unknown member returns false with a reason ('{missingError}')");

        // ---------------------------------------------------------------- a wrong argument type
        // "not an int" is a QString; Qt's resolution requires the declared int. The call fails
        // cleanly instead of handing Qt a pointer of the wrong type.
        bool wrongCalled = QMetaObject.InvokeMethod(spin, "setValue", Qt.ConnectionType.DirectConnection,
                                                    new object[] { "not an int" }, out object wrongResult, out string wrongError);
        context.Check(!wrongCalled && wrongResult == null && !string.IsNullOrEmpty(wrongError),
            $"a wrong argument type returns false with a reason ('{wrongError}')");
        context.Check(spin.Value == 42, "a refused call leaves the target untouched");

        bool methodWrong = rowCount.Invoke(model, new object[] { "not a QModelIndex" }, out object methodWrongResult);
        context.Check(!methodWrong && methodWrongResult == null,
            "QMetaMethod.Invoke refuses a wrong argument type the same way");

        // ---------------------------------------------------------------- queued
        // A void method can be queued; the call is posted, and no result storage is handed to Qt.
        // A queued call that asked for a return value is refused by Qt (documented on the API).
        var queued = new QLabel();
        bool posted = QMetaObject.InvokeMethod(queued, "setText", Qt.ConnectionType.QueuedConnection,
                                               new object[] { "delivered later" }, out object queuedResult);
        context.Check(posted && queuedResult == null,
            $"a queued void call is posted and carries no result (posted={posted})");
        context.Check(queued.Text == "", "the queued call has not run yet");
        QCoreApplication.ProcessEvents();
        context.Check(queued.Text == "delivered later",
            $"the queued call runs on the next event pass (text='{queued.Text}')");

        bool queuedReturn = QMetaObject.InvokeMethod(model, "rowCount", Qt.ConnectionType.QueuedConnection,
                                                      new object[] { new QModelIndex() }, out object queuedCount);
        context.Check(!queuedReturn && queuedCount == null,
            "a queued call that asks for a return value is refused");

        // The discard overload asks for no result, so the same returning method can be queued: Qt
        // runs it and drops the value, exactly like a queued connection to a helper that ignores it.
        bool queuedDiscard = QMetaObject.InvokeMethod(model, "rowCount", Qt.ConnectionType.QueuedConnection,
                                                      new QModelIndex());
        context.Check(queuedDiscard, "a queued call that discards the result is accepted");
        QCoreApplication.ProcessEvents();

        return context.ExitCode;
    }
}
