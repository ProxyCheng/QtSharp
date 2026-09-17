using System;
using QtSharp.Api;

// The graphics view family on the new pipeline: a scene, a view, three item kinds, the
// QList<QGraphicsItem*> the scene hands back, and an off-screen render of the whole thing.
//
// Items are owned by the scene, so the managed wrappers are non-owning (the model marks the
// item classes owned: false) - this demo only checks that driving them works, not that they are
// collected.
public static class ApiGraphicsDemo
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

        var scene = new QGraphicsScene(new QRectF(0, 0, 240, 120));
        var view = new QGraphicsView(scene);
        view.Resize(240, 120);

        var rect = scene.AddRect(new QRectF(10, 10, 50, 30));
        var ellipse = scene.AddEllipse(70.0, 10.0, 40.0, 30.0);
        var text = scene.AddSimpleText("hello");
        text.SetPos(new QPointF(10, 60));

        Check(scene.Items().Length == 3, $"scene holds its three items (got {scene.Items().Length})");
        Check(rect.Rect().Width() == 50 && rect.Rect().Height() == 30,
            $"item rectangle round-trip (got {rect.Rect().Width()}x{rect.Rect().Height()})");
        Check(ellipse.Rect().X() == 70, $"ellipse x (got {ellipse.Rect().X()})");
        Check(text.Text() == "hello", $"text item round-trip (got '{text.Text()}')");

        // A scene hands out QGraphicsItem* through a QList; the wrapper is the same object the
        // identity cache already holds.
        var found = scene.ItemAt(new QPointF(20, 20), new QTransform());
        Check(found != null && ReferenceEquals(found, rect), "itemAt() returns the item that is there");

        rect.SetZValue(5);
        Check(rect.ZValue() == 5, $"z-order round-trip (got {rect.ZValue()})");
        var moved = text.Pos();
        Check(moved.X() == 10 && moved.Y() == 60, $"item position round-trip (got {moved.X()},{moved.Y()})");
        Check(ReferenceEquals(view.Scene(), scene), "the view hands back the scene it was given");

        // An off-screen render proves the scene actually painted through Qt.
        var shot = view.Grab();
        Check(shot.Width() == 240 && shot.Height() == 120,
            $"the view rendered off-screen (got {shot.Width()}x{shot.Height()})");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
