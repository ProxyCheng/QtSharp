﻿using System;
using System.Collections.Generic;

// Port of examples/widgets/itemviews/stardelegate on the new pipeline.
//
// The example is the *editing* half of the delegate story: a managed QStyledItemDelegate paints a
// star rating into a QTableWidget cell, and when the cell is edited it puts a managed QWidget (the
// StarEditor) in the cell, hands it the current value, and writes what comes back into the model.
// test-custommodel already covered paint()/sizeHint(); this one covers createEditor()/setEditorData()/
// setModelData()/updateEditorGeometry() and the commitData/closeEditor signals.
//
// The one adaptation is the value that travels through the model: the official stores a C++
// StarRating value in a QVariant (`QVariant::fromValue<StarRating>`), which a binding cannot
// express, so the rating is an int in the item's display data and the delegate parses it back.
public sealed class ApiStarRating
{
    public const int PaintingScaleFactor = 20;

    public int StarCount { get; private set; }
    public int MaxStarCount { get; }

    static readonly QPolygonF StarPolygon = BuildStar();

    static readonly QPolygonF DiamondPolygon = BuildDiamond();

    public ApiStarRating(int starCount, int maxStarCount)
    {
        StarCount = starCount;
        MaxStarCount = maxStarCount;
    }

    static QPolygonF BuildStar()
    {
        var points = new List<QPointF> { new QPointF(1.0, 0.5) };
        for (int i = 1; i < 5; ++i)
            points.Add(new QPointF(0.5 + 0.5 * Math.Cos(0.8 * i * 3.14),
                                   0.5 + 0.5 * Math.Sin(0.8 * i * 3.14)));
        return new QPolygonF(points.ToArray());
    }

    static QPolygonF BuildDiamond()
    {
        return new QPolygonF(new[]
        {
            new QPointF(0.4, 0.5), new QPointF(0.5, 0.4), new QPointF(0.6, 0.5),
            new QPointF(0.5, 0.6), new QPointF(0.4, 0.5),
        });
    }

    public void SetStarCount(int count) => StarCount = count;

    public QSize SizeHint() => new QSize(PaintingScaleFactor * MaxStarCount, PaintingScaleFactor);

    public void Paint(QPainter painter, QRect rect, QPalette palette, bool editable)
    {
        painter.Save();
        painter.SetRenderHint(QPainter.RenderHint.Antialiasing, true);
        painter.SetPen(Qt.PenStyle.NoPen);

        using (var brush = editable ? palette.Highlight() : palette.WindowText())
            painter.SetBrush(brush);

        int yOffset = (rect.Height() - PaintingScaleFactor) / 2;
        painter.Translate(rect.X(), rect.Y() + yOffset);
        painter.Scale(PaintingScaleFactor, PaintingScaleFactor);

        for (int i = 0; i < MaxStarCount; ++i)
        {
            if (i < StarCount)
                painter.DrawPolygon(StarPolygon, Qt.FillRule.WindingFill);
            else if (editable)
                painter.DrawPolygon(DiamondPolygon, Qt.FillRule.WindingFill);
            painter.Translate(1.0, 0.0);
        }

        painter.Restore();
    }
}

// The official StarEditor: a widget that paints the rating and lets the user drag over the stars.
public sealed class ApiStarEditor : QWidget
{
    public Action EditingFinished;

    ApiStarRating rating = new ApiStarRating(0, 5);

    public ApiStarEditor(QWidget parent = null) : base(parent)
    {
        SetMouseTracking(true);
        SetAutoFillBackground(true);
        Resize(100, 20);
    }

    public void SetStarRating(ApiStarRating value) { rating = value; Update(); }

    public ApiStarRating StarRating => rating;

    public ApiStarRating CurrentRating => rating;

    public int MouseMoveCount;

    public override QSize sizeHint() => rating.SizeHint();

    public override void PaintEvent(QPaintEvent e)
    {
        using var painter = new QPainter(AsQPaintDevice());
        var rect = new QRect(0, 0, width(), height());
        using var palette = Palette;
        rating.Paint(painter, rect, palette, editable: true);
        painter.End();
    }

    public override void MouseMoveEvent(QMouseEvent e)
    {
        MouseMoveCount++;
        var position = e.Position();
        int star = StarAtPosition((int)Math.Round(position.X()));
        if (star != rating.StarCount && star != -1)
        {
            rating.SetStarCount(star);
            Update();
        }
        base.MouseMoveEvent(e);
    }

    public override void MouseReleaseEvent(QMouseEvent e)
    {
        ReleaseEditing();
        base.MouseReleaseEvent(e);
    }

    /// <summary>
    /// The body of mouseReleaseEvent. The runtime has a synthetic *press* probe but no synthetic
    /// release, so a headless test drives the same code through this seam (as ShapedClockApi does
    /// for the drag).
    /// </summary>
    public void ReleaseEditing() => EditingFinished?.Invoke();

    // `(x / (sizeHint().width() / maxStarCount())) + 1`, clamped by the same -1 as the original.
    public int StarAtPosition(int x)
    {
        var hint = rating.SizeHint();
        int star = (x / (hint.Width() / rating.MaxStarCount)) + 1;
        if (star <= 0 || star > rating.MaxStarCount)
            return -1;
        return star;
    }
}

public sealed class ApiStarDelegate : QStyledItemDelegate
{
    public int PaintCalls;
    public int SizeHintCalls;
    public int CreateEditorCalls;
    public int SetEditorDataCalls;
    public int SetModelDataCalls;
    public int UpdateEditorGeometryCalls;

    public const int MaxStarCount = 5;

    /// <summary>The editor for a view's widget, so SetEditorData/SetModelData can talk to it.</summary>
    readonly Dictionary<IntPtr, ApiStarEditor> editors = new();

    public ApiStarDelegate(QObject parent = null) : base(parent) { }

    static int RatingOf(QModelIndex index)
    {
        string text = index.Data()?.ToString();
        return int.TryParse(text, out int rating) ? rating : 0;
    }

    static bool IsRatingColumn(QModelIndex index) => index.Column() == 3;

    public override void Paint(QPainter painter, QStyleOptionViewItem option, QModelIndex index)
    {
        PaintCalls++;
        if (!IsRatingColumn(index))
        {
            base.Paint(painter, option, index);
            return;
        }

        var rating = new ApiStarRating(RatingOf(index), MaxStarCount);
        var rect = option.Rect;
        using var palette = option.Palette;
        if ((option.State & QStyle.State.State_Selected) != 0)
            painter.FillRect(rect, palette.Highlight());

        rating.Paint(painter, rect, palette, editable: false);
    }

    public override QSize SizeHint(QStyleOptionViewItem option, QModelIndex index)
    {
        SizeHintCalls++;
        if (!IsRatingColumn(index))
            return base.SizeHint(option, index);

        var rating = new ApiStarRating(0, MaxStarCount);
        return rating.SizeHint();
    }

    public override QWidget CreateEditor(QWidget parent, QStyleOptionViewItem option, QModelIndex index)
    {
        CreateEditorCalls++;
        if (!IsRatingColumn(index))
            return base.CreateEditor(parent, option, index);

        var editor = new ApiStarEditor(parent);
        editor.EditingFinished = () => CommitAndCloseEditor(editor);
        editors[editor.Handle] = editor;
        return editor;
    }

    public override void SetEditorData(QWidget editor, QModelIndex index)
    {
        SetEditorDataCalls++;
        var starEditor = AsStarEditor(editor);
        if (starEditor == null)
        {
            base.SetEditorData(editor, index);
            return;
        }

        starEditor.SetStarRating(new ApiStarRating(RatingOf(index), MaxStarCount));
    }

    public override void SetModelData(QWidget editor, QAbstractItemModel model, QModelIndex index)
    {
        SetModelDataCalls++;
        var starEditor = AsStarEditor(editor);
        if (starEditor == null)
        {
            base.SetModelData(editor, model, index);
            return;
        }

        // `model->setData(index, QVariant::fromValue(editor->starRating()))`, with the rating as an
        // int instead of the C++ value type.
        model.SetData(index, starEditor.StarRating.StarCount);
    }

    public override void UpdateEditorGeometry(QWidget editor, QStyleOptionViewItem option, QModelIndex index)
    {
        UpdateEditorGeometryCalls++;
        var rect = option.Rect;
        editor.SetGeometry(rect);
    }

    /// <summary>
    /// The editor comes back from Qt as a QWidget; the runtime's handle registry hands us the same
    /// C# object we created, but a lookup by handle keeps this working even if it does not.
    /// </summary>
    ApiStarEditor AsStarEditor(QWidget widget)
    {
        if (widget is ApiStarEditor direct)
            return direct;
        return editors.TryGetValue(widget.Handle, out var found) ? found : null;
    }

    public void CommitAndCloseEditor(ApiStarEditor editor)
    {
        CommitDataSignal.Emit(editor);
        CloseEditorSignal.Emit(editor, QAbstractItemDelegate.EndEditHint.NoHint);
    }
}

// The official example exactly: a QTableWidget with the delegate on it. That is also the
// regression test for the "model of a view" gap: QTableWidget's own model is a private C++ class
// that is not part of the model, so `table.Model()` wraps it as a plain QAbstractItemModel - and
// index/data/setData/rowCount are pure virtuals there. The generated stubs call the real C++
// virtual for an object we did not subclass (see __managedSubclass in ApiCodeGen), which is what
// makes the index reachable and the editor's commit land in the table.
public sealed class ApiStarTable : QTableWidget
{
    public static readonly (string Title, string Genre, string Artist, int Rating)[] Songs =
    {
        ("Mass in B-Minor", "Baroque", "J.S. Bach", 5),
        ("Three More Foxes", "Jazz", "Maynard Ferguson", 4),
        ("Sex Bomb", "Pop", "Tom Jones", 3),
        ("Barbie Girl", "Pop", "Aqua", 5),
    };

    public readonly ApiStarDelegate StarDelegate;

    public ApiStarTable(QWidget parent = null) : base(Songs.Length, 4, parent)
    {
        for (int row = 0; row < Songs.Length; row++)
        {
            SetItem(row, 0, new QTableWidgetItem(Songs[row].Title));
            SetItem(row, 1, new QTableWidgetItem(Songs[row].Genre));
            SetItem(row, 2, new QTableWidgetItem(Songs[row].Artist));
            var rating = new QTableWidgetItem();
            rating.SetData(0, Songs[row].Rating);
            SetItem(row, 3, rating);
        }
        SetHorizontalHeaderLabels(new[] { "Title", "Genre", "Artist", "Rating" });

        StarDelegate = new ApiStarDelegate(this);
        SetItemDelegate(StarDelegate);
        SetEditTriggers(QAbstractItemView.EditTriggers.DoubleClicked
                        | QAbstractItemView.EditTriggers.SelectedClicked);
        SetSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows);

        ResizeColumnsToContents();
        Resize(500, 300);
    }

    public int RatingAt(int row)
    {
        var item = Item(row, 3);
        return int.TryParse(item.Data(0)?.ToString(), out int rating) ? rating : 0;
    }
}

[Example("qtwidgets-stardelegate")]
public static class StarDelegate
{
    public static int Run(ExampleContext example)
    {

        using var table = new ApiStarTable();
        #if QTSHARP_EXAMPLE_SMOKE
            table.Show();
        #else
            example.Show(table);
        #endif

#if QTSHARP_EXAMPLE_SMOKE

        example.Check(table.RowCount == 4 && table.ColumnCount == 4,
            $"the table has four songs and four columns ({table.RowCount}x{table.ColumnCount})");
#endif
        {
            var titleHeader = table.HorizontalHeaderItem(0);
            var ratingHeader = table.HorizontalHeaderItem(3);
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(titleHeader.Text() == "Title" && ratingHeader.Text() == "Rating",
                $"the header carries the official labels "
                + $"('{titleHeader.Text()}', '{ratingHeader.Text()}')");
            #endif
        }

        // The regression: QTableWidget's model is a private C++ class, so `table.Model()` hands back
        // a plain QAbstractItemModel wrapper. Its pure virtuals have to reach the real object.
        using (var model = table.Model())
        {
            var index = model.Index(2, 3);
            string value = model.Data(index)?.ToString();
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(index.IsValid() && model.RowCount() == 4 && value == "3",
                $"table.Model() reaches the real model "
                + $"(valid={index.IsValid()}, rows={model.RowCount()}, [2,3]='{value}')");
            #endif
        }

        // The lowercase accessors, because `table.EditTriggers` is ambiguous: QTableWidget inherits
        // the nested enum of that name from QAbstractItemView, and the type wins over the property.
        var triggers = table.editTriggers();
        #if QTSHARP_EXAMPLE_SMOKE
        example.Check(triggers.HasFlag(QAbstractItemView.EditTriggers.DoubleClicked)
              && triggers.HasFlag(QAbstractItemView.EditTriggers.SelectedClicked),
            $"the edit triggers are the official ones (got {triggers})");
        #endif
        #if QTSHARP_EXAMPLE_SMOKE
        example.Check(table.selectionBehavior() == QAbstractItemView.SelectionBehavior.SelectRows,
            $"whole rows are selected (got {table.selectionBehavior()})");
        #endif

        // The ratings travelled from C# into the table's items.
#if QTSHARP_EXAMPLE_SMOKE

        // The ratings travelled from C# into the table's items.
        example.Check(table.RatingAt(0) == 5 && table.RatingAt(1) == 4 && table.RatingAt(2) == 3
              && table.RatingAt(3) == 5,
            $"the four ratings are 5/4/3/5 (got {table.RatingAt(0)}/{table.RatingAt(1)}/"
            + $"{table.RatingAt(2)}/{table.RatingAt(3)})");
#endif

        // Rendering the table is what makes Qt ask the managed delegate to paint.
        #if QTSHARP_EXAMPLE_SMOKE
        using (var rendered = table.Grab())
            example.Check(rendered != null && !rendered.IsNull() && table.StarDelegate.PaintCalls > 0,
                $"Qt asked the managed delegate to paint ({table.StarDelegate.PaintCalls} times)");
        #endif

        // QStyledItemDelegate::sizeHint is reached for every cell.
#if QTSHARP_EXAMPLE_SMOKE

        // QStyledItemDelegate::sizeHint is reached for every cell.
        example.Check(table.StarDelegate.SizeHintCalls > 0,
            $"Qt asked the managed delegate for size hints ({table.StarDelegate.SizeHintCalls} times)");
#endif

        // The rating column's size hint is the official 20 * maxStarCount x 20.
        //
        // The index comes from QTableWidget::indexFromItem, not from model().index(): the table's
        // own model is a private C++ class that is not in the model, so the QAbstractItemModel
        // wrapper around it cannot reach it - `index`/`rowCount`/`data` are pure virtuals there and
        // the generated base methods answer `default`. The delegate
        // never has this problem: the indexes Qt hands it are the real ones.
        using (var option = new QStyleOptionViewItem())
        {
            var index = table.Model().Index(0, 3);
            var hint = table.StarDelegate.SizeHint(option, index);
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(index.IsValid() && hint.Width() == ApiStarRating.PaintingScaleFactor * ApiStarDelegate.MaxStarCount
                  && hint.Height() == ApiStarRating.PaintingScaleFactor,
                $"the rating cell asks for {ApiStarRating.PaintingScaleFactor * ApiStarDelegate.MaxStarCount}x"
                + $"{ApiStarRating.PaintingScaleFactor} (got {hint.Width()}x{hint.Height()})");
            #endif
        }

        using (var option = new QStyleOptionViewItem())

        {

            var index = table.Model().Index(0, 0);
            var hint = table.StarDelegate.SizeHint(option, index);
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(hint.Width() > 0 && hint.Height() > 0,
                $"a text cell falls through to QStyledItemDelegate::sizeHint ({hint.Width()}x{hint.Height()})");
            #endif
        }

        // The editor: created by the delegate, filled from the model, committed back into it.
        using (var option = new QStyleOptionViewItem())
        {
            var index = table.Model().Index(1, 3);
            using var editor = table.StarDelegate.CreateEditor(table, option, index);
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(table.StarDelegate.CreateEditorCalls == 1 && editor is ApiStarEditor,
                $"createEditor() returns the managed star editor ({editor?.GetType().Name})");
            #endif

            table.StarDelegate.SetEditorData(editor, index);
            var starEditor = (ApiStarEditor)editor;
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(table.StarDelegate.SetEditorDataCalls == 1 && starEditor.StarRating.StarCount == 4,
                $"setEditorData() handed the row's rating to the editor "
                + $"(got {starEditor.StarRating.StarCount})");
            #endif

            // The official StarEditor turns a mouse position into a star index.
#if QTSHARP_EXAMPLE_SMOKE

            // The official StarEditor turns a mouse position into a star index.
            example.Check(starEditor.StarAtPosition(0) == 1 && starEditor.StarAtPosition(99) == 5
                  && starEditor.StarAtPosition(100) == -1,
                "starAtPosition() maps x to the 1..5 stars and rejects x beyond the widget");
#endif

            // Releasing in the editor reports "editing finished".
            #if QTSHARP_EXAMPLE_SMOKE
            bool finished = false;
            starEditor.EditingFinished = () => finished = true;
            starEditor.ReleaseEditing();
            example.Check(finished, "the editor raises editingFinished on release");
            #endif

            // The editor paints the rating itself (2 stars and 3 diamonds, all in the editable
            // colour): the same ApiStarRating::Paint the delegate calls for a cell.
            starEditor.SetStarRating(new ApiStarRating(2, ApiStarDelegate.MaxStarCount));
            using (var painted = starEditor.Grab())
            using (var image = painted.ToImage())
            {
                uint background = image.Pixel(1, 1);
                int ink = 0;
                for (int y = 0; y < image.Height(); y++)
                    for (int x = 0; x < image.Width(); x++)
                        if (image.Pixel(x, y) != background)
                            ink++;
                #if QTSHARP_EXAMPLE_SMOKE
                example.Check(ink > 0, $"the editor paints its stars ({ink} pixels differ from the background)");
                #endif
            }

            // updateEditorGeometry: the editor takes the cell's rectangle.
            var cellRect = new QRect(10, 20, 100, 20);
            {
                option.Rect = cellRect;
                table.StarDelegate.UpdateEditorGeometry(editor, option, index);
            }
            var geometry = editor.Geometry;
                #if QTSHARP_EXAMPLE_SMOKE
                example.Check(table.StarDelegate.UpdateEditorGeometryCalls == 1
                      && geometry.Width() == 100 && geometry.Height() == 20,
                    $"updateEditorGeometry() gives the editor the cell's rectangle "
                    + $"({geometry.Width()}x{geometry.Height()})");
                #endif

            // Committing: the editor's rating goes back into the model through setModelData().
            starEditor.SetStarRating(new ApiStarRating(2, ApiStarDelegate.MaxStarCount));
            table.StarDelegate.SetModelData(editor, table.Model(), index);
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(table.StarDelegate.SetModelDataCalls == 1 && table.RatingAt(1) == 2,
                $"setModelData() wrote the editor's rating back into the model (row 1 now {table.RatingAt(1)})");
            #endif
        }

        // The commit/close signals of QAbstractItemDelegate are what a view listens to; here they
        // are connected and emitted, which is the same pair the official commitAndCloseEditor emits.
        QWidget committed = null;
        int closed = 0;
        table.StarDelegate.CommitDataSignal.Connect(widget => committed = widget);
        table.StarDelegate.CloseEditorSignal.Connect((widget, hint) => closed++);
        using (var editor2 = new ApiStarEditor())
        {
            table.StarDelegate.CommitAndCloseEditor(editor2);
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(committed != null && committed.Handle == editor2.Handle && closed == 1,
                "commitData/closeEditor carry the editor (the commitAndCloseEditor pair)");
            #endif
        }

        #if QTSHARP_EXAMPLE_SMOKE
        return example.ExitCode;
        #else
        return 0;
        #endif
    }
}

