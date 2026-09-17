using System;
using QtSharp.Api;

// The input events and the rich text types, on the new pipeline.
//
// Every event Qt delivers used to reach C# as an opaque pointer - a handler could tell that
// something happened, but not what. The whole input-event family is bound now, so a managed
// keyPressEvent can read the key code and a wheelEvent can read how far the wheel turned.
public sealed class EventProbeWidget : QWidget
{
    public int WheelCount;
    public int LastWheelDelta;
    public int LastWheelX, LastWheelY;
    public bool LastWheelShift;

    public int KeyCount;
    public int LastKey;
    public string LastKeyText;

    public int PressCount;
    public int LastPressX, LastPressY;

    public override void MousePressEvent(QMouseEvent e)
    {
        PressCount++;
        LastPressX = (int)e.Position().X();
        LastPressY = (int)e.Position().Y();
    }

    public override void WheelEvent(QWheelEvent e)
    {
        WheelCount++;
        // angleDelta() is in eighths of a degree, position() in widget coordinates.
        LastWheelDelta = e.angleDelta().Y();
        LastWheelX = (int)e.Position().X();
        LastWheelY = (int)e.Position().Y();
        LastWheelShift = e.Modifiers().HasFlag(Qt.KeyboardModifiers.ShiftModifier);
    }

    public override void KeyPressEvent(QKeyEvent e)
    {
        KeyCount++;
        LastKey = e.Key();
        LastKeyText = e.Text();
    }
}

public static class ApiEventsDemo
{
    public static int Run()
    {
        ApiRuntime.StartApplication();

        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        // ---------------------------------------------------------------- input events
        var probe = new EventProbeWidget();
        probe.Resize(200, 100);
        // Qt routes a wheel event through the widget tree, so the probe has to be a real (visible)
        // widget rather than a hidden one.
        probe.Show();

        // A wheel turn of 120 degrees arrives as 120 * 8 = 960 eighths, at the position it
        // happened, with the modifiers Qt recorded.
        bool turned = ApiRuntime.SendWheel(probe.Handle, 30, 40, 120, (int)Qt.KeyboardModifiers.ShiftModifier);
        Check(turned && probe.WheelCount == 1, $"wheelEvent reached C# ({probe.WheelCount} time(s))");
        Check(probe.LastWheelDelta == 960, $"QWheelEvent::angleDelta (got {probe.LastWheelDelta})");
        Check(probe.LastWheelX == 30 && probe.LastWheelY == 40,
            $"QWheelEvent::position (got {probe.LastWheelX},{probe.LastWheelY})");
        Check(probe.LastWheelShift, "QWheelEvent::modifiers carried the shift key");

        // The key code Qt reports is a Qt::Key value (the model publishes the table, since
        // key() itself hands back an int).
        bool typed = ApiRuntime.SendKeyPress(probe.Handle, (int)Qt.Key.Key_A);
        Check(typed && probe.KeyCount == 1, $"keyPressEvent reached C# ({probe.KeyCount} time(s))");
        Check(probe.LastKey == (int)Qt.Key.Key_A, $"QKeyEvent::key (got {probe.LastKey})");
        // The text Qt reports for a synthesized press of that key is the character it stands for.
        Check(probe.LastKeyText == "A", $"QKeyEvent::text (got '{probe.LastKeyText}')");

        // The event classes inherit their accessors from the bound base chain, so the pointer
        // side of QSinglePointEvent is visible on a mouse event too.
        ApiRuntime.SendMousePress(probe.Handle, 5, 6);
        Check(probe.PressCount == 1 && probe.LastPressX == 5 && probe.LastPressY == 6,
            $"the mouse press still reaches the managed override (at {probe.LastPressX},{probe.LastPressY})");

        // ---------------------------------------------------------------- rich text types
        // QTextFormat is the base of every text format, so a QTextCharFormat has to be usable
        // wherever a QTextFormat is (that is what the model's base list is for).
        var charFormat = new QTextCharFormat();
        charFormat.SetFontWeight(700);
        QTextFormat asBase = charFormat;
        Check(asBase.Type() == (int)QTextFormat.FormatType.CharFormat && charFormat.FontWeight() == 700,
            $"QTextCharFormat through its QTextFormat base (type={asBase.Type()})");

        // QTextLength is a value type Qt passes by const reference: a frame format stores one.
        var frameFormat = new QTextFrameFormat();
        using (var length = new QTextLength(QTextLength.Type.FixedLength, 120))
        {
            frameFormat.SetWidth(length);
            Check(frameFormat.Width().RawValue() == 120,
                $"QTextLength round-trip through QTextFrameFormat (got {frameFormat.Width().RawValue()})");
        }
        frameFormat.SetBorder(2);
        Check(frameFormat.Border() == 2, "QTextFrameFormat::border round-trip");

        // A laid-out document is what hands out QTextLine: a line knows where it is, how wide it
        // is and what it covers - the primitive a text editor's cursor math is built on. A
        // block's layout is only filled in once the document has been laid out, which asking for
        // a bounding rect does.
        var document = new QTextDocument();
        document.SetTextWidth(400);
        document.SetPlainText("first line\nsecond line");
        var documentLayout = document.DocumentLayout();
        var block = document.FirstBlock();
        var blockRect = documentLayout.BlockBoundingRect(block);
        var layout = block.Layout();
        Check(layout.LineCount() >= 1 && blockRect.Height() > 0,
            $"QTextLayout::lineCount (got {layout.LineCount()}, block height {blockRect.Height():F1})");
        var line = layout.LineCount() > 0 ? layout.LineAt(0) : new QTextLine();
        Check(line.IsValid() && line.NaturalTextWidth() > 0,
            $"QTextLine::naturalTextWidth (got {line.NaturalTextWidth():F1})");
        Check(line.TextStart() == 0, $"QTextLine::textStart (got {line.TextStart()})");

        // A table is a frame with a grid of cells. A cell's format is a QTextCharFormat, and a
        // QTextTableCellFormat is one of those - so the cell padding it adds is set through the
        // subclass and read back through a QTextCharFormat.
        var cursor = new QTextCursor(document);
        var table = cursor.InsertTable(2, 2);
        using (var cellFormat = new QTextTableCellFormat())
        {
            cellFormat.SetTopPadding(4);
            Check(cellFormat.TopPadding() == 4, $"QTextTableCellFormat::topPadding (got {cellFormat.TopPadding()})");
            cellFormat.SetFontWeight(700);
            table.CellAt(0, 0).SetFormat(cellFormat);
            Check(table.CellAt(0, 0).Format().FontWeight() == 700,
                $"QTextTableCellFormat round-trip through QTextTableCell (got {table.CellAt(0, 0).Format().FontWeight()})");
        }
        Check(table.Rows() == 2 && table.Columns() == 2, "QTextTable dimensions");

        // A document fragment is a piece of another document, which is how rich text is pasted.
        var fragment = QTextDocumentFragment.FromHtml("<b>bold</b> text");
        Check(fragment.ToPlainText() == "bold text", $"QTextDocumentFragment (got '{fragment.ToPlainText()}')");

        // A regular-expression validator, next to the int/double ones the model already had.
        var edit = new QLineEdit();
        edit.SetValidator(new QRegularExpressionValidator(new QRegularExpression("\\d{3}-\\d{4}")));
        Check(edit.HasAcceptableInput() == false, "an empty input is not acceptable yet");
        edit.SetText("555-1234");
        Check(edit.HasAcceptableInput(), "QRegularExpressionValidator accepted the pattern");
        edit.SetText("555-123");
        Check(!edit.HasAcceptableInput(), "QRegularExpressionValidator rejected a short one");

        // QRawFont is the font data behind a QFont, and QStaticText is text measured once and
        // drawn many times - both are QtGui value types.
        using (var font = new QFont("Arial", 12))
        {
            var rawFont = QRawFont.FromFont(font);
            Check(rawFont.IsValid() && rawFont.FamilyName().Length > 0,
                $"QRawFont::fromFont (got '{rawFont.FamilyName()}')");
        }

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
