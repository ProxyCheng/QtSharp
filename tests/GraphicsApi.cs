using System;

// The graphics view family on the new pipeline: a scene, a view, three item kinds, the
// QList<QGraphicsItem*> the scene hands back, and an off-screen render of the whole thing.
//
// Items are owned by the scene, so the managed wrappers are non-owning (the model marks the
// item classes owned: false) - this demo only checks that driving them works, not that they are
// collected.
[Test("test-graphics")]
public static class ApiGraphicsDemo
{
    public static int Run(TestContext context)
    {

        var scene = new QGraphicsScene(new QRectF(0, 0, 240, 120));
        var view = new QGraphicsView(scene);
        view.Resize(240, 120);

        var rect = scene.AddRect(new QRectF(10, 10, 50, 30));
        var ellipse = scene.AddEllipse(70.0, 10.0, 40.0, 30.0);
        var text = scene.AddSimpleText("hello");
        text.SetPos(new QPointF(10, 60));

        context.Check(scene.Items().Length == 3, $"scene holds its three items (got {scene.Items().Length})");
        context.Check(rect.Rect().Width() == 50 && rect.Rect().Height() == 30,
            $"item rectangle round-trip (got {rect.Rect().Width()}x{rect.Rect().Height()})");
        context.Check(ellipse.Rect().X() == 70, $"ellipse x (got {ellipse.Rect().X()})");
        context.Check(text.Text() == "hello", $"text item round-trip (got '{text.Text()}')");

        // A scene hands out QGraphicsItem* through a QList; the wrapper is the same object the
        // identity cache already holds.
        var found = scene.ItemAt(new QPointF(20, 20), new QTransform());
        context.Check(found != null && ReferenceEquals(found, rect), "itemAt() returns the item that is there");

        rect.SetZValue(5);
        context.Check(rect.ZValue() == 5, $"z-order round-trip (got {rect.ZValue()})");
        var moved = text.Pos();
        context.Check(moved.X() == 10 && moved.Y() == 60, $"item position round-trip (got {moved.X()},{moved.Y()})");
        context.Check(ReferenceEquals(view.Scene(), scene), "the view hands back the scene it was given");

        // An off-screen render proves the scene actually painted through Qt.
        var shot = view.Grab();
        context.Check(DemoPixels.GrabCovers(shot, 240, 120),
            $"the view rendered off-screen (got {shot.Width()}x{shot.Height()})");

        return context.ExitCode;
    }
}
