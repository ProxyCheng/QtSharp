using System;
using System.Collections.Generic;

// Writing Qt subclasses in C#, and letting Qt drive them. Nothing here calls an override on
// purpose: each override is invoked by Qt's own machinery, so the assertions only pass if the
// generated native subclass installed the vtable hook and the dispatch found the managed object.
//
//   abstract class, concrete override   QAbstractButton is abstract; paintEvent is pure. Qt calls
//                                       the managed PaintEvent while the widget renders.
//   layout extension point              QLayout::addItem/itemAt/takeAt/count/sizeHint are the
//                                       extension points every custom layout implements. Qt calls
//                                       them while it activates and measures the layout.
//   protected nested type               QAbstractItemView::moveCursor takes CursorAction, which Qt
//                                       declares protected. The managed override (and the base
//                                       call it chains to) both have to cross that type.
//   by-reference virtual parameters     QValidator::validate rewrites its QString and int through
//                                       references. The managed override writes them back into
//                                       Qt's variables; QLineEdit then reports the verdict.
[Test("test-subclass")]
public static class ApiSubclassDemo
{
    sealed class ManagedButton : QAbstractButton
    {
        public int PaintCalls;
        public int SizeHintCalls;

        public ManagedButton() => SetText("managed");

        public override void PaintEvent(QPaintEvent e)
        {
            PaintCalls++;
            var colour = new QColor(40, 120, 200);
            using var painter = new QPainter(AsQPaintDevice());
            painter.FillRect(new QRect(0, 0, width(), height()), colour);
        }

        public override QSize sizeHint()
        {
            SizeHintCalls++;
            return new QSize(120, 40);
        }
    }

    sealed class SingleColumnLayout : QLayout
    {
        readonly List<QLayoutItem> items = new();
        public int AddItemCalls;
        public int ItemAtCalls;
        public int TakeAtCalls;
        public int CountCalls;
        public int SizeHintCalls;
        public int SetGeometryCalls;
        public QRect LastGeometry;

        public override void AddItem(QLayoutItem item)
        {
            AddItemCalls++;
            items.Add(item);
        }

        public override int Count()
        {
            CountCalls++;
            return items.Count;
        }

        public override QLayoutItem ItemAt(int index)
        {
            ItemAtCalls++;
            return index >= 0 && index < items.Count ? items[index] : null;
        }

        public override QLayoutItem TakeAt(int index)
        {
            TakeAtCalls++;
            if (index < 0 || index >= items.Count)
                return null;
            var item = items[index];
            items.RemoveAt(index);
            return item;
        }

        public override QSize SizeHint()
        {
            SizeHintCalls++;
            return new QSize(80, 30);
        }

        public override void SetGeometry(QRect rect)
        {
            SetGeometryCalls++;
            LastGeometry = rect;
            for (int i = 0; i < items.Count; i++)
                items[i].SetGeometry(new QRect(rect.X(), rect.Y() + i * 40, rect.Width(), 40));
        }
    }

    sealed class DownOnlyView : QListView
    {
        public int MoveCursorCalls;
        public QAbstractItemView.CursorAction LastAction;

        // CursorAction is *protected* in Qt. The override crosses it, and so does the base call:
        // the generated forwarder converts the int slot inside a member of the native subclass,
        // which is the only place C++ lets the protected type be named.
        public override QModelIndex MoveCursor(QAbstractItemView.CursorAction cursorAction,
                                               Qt.KeyboardModifiers modifiers)
        {
            MoveCursorCalls++;
            LastAction = cursorAction;
            return base.MoveCursor(cursorAction, modifiers);
        }
    }

    sealed class DigitsOnlyValidator : QValidator
    {
        public int ValidateCalls;

        public override QValidator.State Validate(ref string input, ref int pos)
        {
            ValidateCalls++;
            // The reference parameters are the override's outputs: clamp the position and answer
            // for the text that came in.
            if (pos < 0 || pos > input.Length)
                pos = input.Length;
            foreach (char c in input)
                if (!char.IsAsciiDigit(c))
                    return QValidator.State.Invalid;
            return QValidator.State.Acceptable;
        }
    }

    public static int Run(TestContext context)
    {

        // ---- an abstract Qt class implemented in C# --------------------------------------
        var host = new QWidget();
        host.Resize(300, 160);
        var layout = new SingleColumnLayout();
        host.SetLayout(layout);
        var button = new ManagedButton();
        layout.AddWidget(button);

        // Qt's own path reaches the managed addItem (QLayout::addWidget calls the virtual).
        context.Check(layout.AddItemCalls == 1,
            $"Qt reached the managed layout's addItem ({layout.AddItemCalls} call(s))");
        context.Check(layout.Count() >= 1, $"the managed count() sees the item ({layout.Count()})");

        host.Show();
        layout.Activate();
        context.Check(layout.SetGeometryCalls > 0,
            $"Qt asked the managed layout to set its geometry ({layout.SetGeometryCalls} call(s))");
        context.Check(layout.LastGeometry.Width() == 300 && layout.LastGeometry.Height() == 160,
            $"the layout got the host's rectangle ({layout.LastGeometry.Width()}x{layout.LastGeometry.Height()})");
        context.Check(button.width() > 0 && button.height() > 0,
            $"the managed layout placed the child ({button.width()}x{button.height()})");

        // QLayout::totalSizeHint() measures by calling the virtual sizeHint().
        var total = layout.TotalSizeHint();
        context.Check(layout.SizeHintCalls > 0 && total.Width() >= 80 && total.Height() >= 30,
            $"the managed layout's sizeHint was consulted ({total.Width()}x{total.Height()}, {layout.SizeHintCalls} call(s))");

        // QLayout::indexOf walks count()/itemAt() through the vtable; the item it hands back is
        // Qt's own QWidgetItem, whose sizeHint() calls the widget's virtual - the managed button.
        context.Check(layout.IndexOf(button) == 0 && layout.ItemAtCalls > 0,
            $"the managed itemAt() answered Qt's indexOf ({layout.ItemAtCalls} call(s))");
        var item = layout.ItemAt(0);
        var childHint = item.SizeHint();
        context.Check(button.SizeHintCalls > 0 && childHint.Width() == 120 && childHint.Height() == 40,
            $"Qt consulted the managed button's sizeHint ({childHint.Width()}x{childHint.Height()}, {button.SizeHintCalls} call(s))");

        // Rendering calls the managed paintEvent; the grab proves the override really painted.
        var shot = button.Grab();
        context.Check(button.PaintCalls > 0,
            $"Qt asked the managed button to paint ({button.PaintCalls} call(s))");
        context.Check(DemoPixels.GrabCovers(shot, button.width(), button.height()),
            $"the managed paintEvent produced the widget content ({shot.Width()}x{shot.Height()})");

        // QLayout::removeWidget goes through takeAt().
        layout.RemoveWidget(button);
        context.Check(layout.TakeAtCalls > 0,
            $"Qt reached the managed layout's takeAt ({layout.TakeAtCalls} call(s))");
        context.Check(layout.Count() == 0, $"the managed layout released the item ({layout.Count()} left)");

        // ---- a protected nested type in an override signature ---------------------------
        var view = new DownOnlyView();
        var model = new QStandardItemModel();
        model.AppendRow(new QStandardItem("first"));
        model.AppendRow(new QStandardItem("second"));
        view.SetModel(model);
        view.SetCurrentIndex(model.Index(0, 0));

        var start = view.MoveCursor(QAbstractItemView.CursorAction.MoveDown,
                                    Qt.KeyboardModifiers.NoModifier);
        context.Check(view.MoveCursorCalls == 1 && view.LastAction == QAbstractItemView.CursorAction.MoveDown,
            $"the managed moveCursor override ran ({view.MoveCursorCalls} call(s), {view.LastAction})");
        context.Check(start.IsValid(),
            "the managed override chained to QListView::moveCursor (the protected enum crossed both ways)");

        // ---- by-reference parameters on an override ------------------------------------
        var editor = new QLineEdit();
        var validator = new DigitsOnlyValidator();
        editor.SetValidator(validator);
        editor.SetText("12a");
        context.Check(!editor.HasAcceptableInput(),
            "QLineEdit rejected the non-digit text through the managed validator");
        editor.SetText("42");
        context.Check(editor.HasAcceptableInput(),
            "QLineEdit accepted the digits through the managed validator");
        context.Check(validator.ValidateCalls >= 2,
            $"Qt called the managed validate(reference quintet) override ({validator.ValidateCalls} call(s))");

        editor.Close();
        view.Close();
        // The children go first: the host owns them natively, so once it is gone their wrappers
        // would point at freed objects.
        button.Dispose();
        layout.Dispose();
        editor.Dispose();
        view.Dispose();
        host.Dispose();

        return context.ExitCode;
    }
}
