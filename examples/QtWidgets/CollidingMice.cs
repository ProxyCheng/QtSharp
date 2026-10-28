﻿using System;

// The official examples/widgets/graphicsview/collidingmice example, ported onto the generated
// binding. It is the conformance case for writing a *QGraphicsItem* in C#: MouseItem derives from
// QGraphicsItem and implements the pure virtuals Qt calls while it draws and advances the scene
// (boundingRect, paint), plus shape() and advance().
//
// The official cheese.jpg background comes from a .qrc this repository does not ship, so the view
// gets a plain light background; everything else - the scene rectangle, the seven mice, the
// collision/steering maths and the paint calls - is the upstream code.
[Example("qtwidgets-collidingmice")]
public static class CollidingMice
{
    sealed class MouseItem : QGraphicsItem
    {
        const double Pi = 3.14159265358979323846;
        const double TwoPi = 2 * Pi;

        readonly QColor color;
        double angle;
        double speed;
        double eyeDirection;

        public int PaintCalls;
        public int AdvanceCalls;

        public MouseItem()
        {
            var random = QRandomGenerator.Global();
            color = new QColor(random.Bounded(256), random.Bounded(256), random.Bounded(256));
            SetRotation(random.Bounded(360 * 16));
        }

        public override QRectF BoundingRect()
        {
            const double adjust = 0.5;
            return new QRectF(-18 - adjust, -22 - adjust, 36 + adjust, 60 + adjust);
        }

        public override QPainterPath Shape()
        {
            var path = new QPainterPath();
            path.AddRect(new QRectF(-10, -20, 20, 40));
            return path;
        }

        public override void Paint(QPainter painter, QStyleOptionGraphicsItem option, QWidget widget)
        {
            PaintCalls++;

            // Body
            painter.SetBrush(color);
            painter.DrawEllipse(-10, -20, 20, 40);

            // Eyes
            painter.SetBrush(Qt.GlobalColor.white);
            painter.DrawEllipse(-10, -17, 8, 8);
            painter.DrawEllipse(2, -17, 8, 8);

            // Nose
            painter.SetBrush(Qt.GlobalColor.black);
            painter.DrawEllipse(new QRectF(-2, -22, 4, 4));

            // Pupils
            painter.DrawEllipse(new QRectF(-8.0 + eyeDirection, -17, 4, 4));
            painter.DrawEllipse(new QRectF(4.0 + eyeDirection, -17, 4, 4));

            // Ears
            painter.SetBrush(Scene().CollidingItems(this).Length == 0
                ? Qt.GlobalColor.darkYellow
                : Qt.GlobalColor.red);
            painter.DrawEllipse(-17, -12, 16, 16);
            painter.DrawEllipse(1, -12, 16, 16);

            // Tail
            using var tail = new QPainterPath(new QPointF(0, 20));
            tail.CubicTo(-5, 22, -5, 22, 0, 25);
            tail.CubicTo(5, 27, 5, 32, 0, 30);
            tail.CubicTo(-5, 32, -5, 42, 0, 35);
            painter.SetBrush(Qt.BrushStyle.NoBrush);
            painter.DrawPath(tail);
        }

        public override void Advance(int step)
        {
            if (step == 0)
                return;
            AdvanceCalls++;

            var lineToCenter = new QLineF(new QPointF(0, 0), MapFromScene(new QPointF(0, 0)));
            if (lineToCenter.Length() > 150)
            {
                double angleToCenter = Math.Atan2(lineToCenter.Dy(), lineToCenter.Dx());
                angleToCenter = NormalizeAngle((Pi - angleToCenter) + Pi / 2);
                if (angleToCenter < Pi && angleToCenter > Pi / 4)
                {
                    angle += (angle < -Pi / 2) ? 0.25 : -0.25;
                }
                else if (angleToCenter >= Pi && angleToCenter < (Pi + Pi / 2 + Pi / 4))
                {
                    angle += (angle < Pi / 2) ? 0.25 : -0.25;
                }
            }
            else if (Math.Sin(angle) < 0)
            {
                angle += 0.25;
            }
            else if (Math.Sin(angle) > 0)
            {
                angle -= 0.25;
            }

            using var dangerArea = new QPolygonF(new[]
            {
                MapToScene(0, 0), MapToScene(-30, -50), MapToScene(30, -50),
            });
            var dangerMice = Scene().Items(dangerArea);
            foreach (var item in dangerMice)
            {
                if (item.Handle == Handle)
                    continue;
                var lineToMouse = new QLineF(new QPointF(0, 0), MapFromItem(item, 0, 0));
                double angleToMouse = Math.Atan2(lineToMouse.Dy(), lineToMouse.Dx());
                angleToMouse = NormalizeAngle((Pi - angleToMouse) + Pi / 2);
                if (angleToMouse >= 0 && angleToMouse < Pi / 2)
                    angle += 0.5;
                else if (angleToMouse <= TwoPi && angleToMouse > (TwoPi - Pi / 2))
                    angle -= 0.5;
            }

            var random = QRandomGenerator.Global();
            // The upstream `bounded(1)` is always 0, so this only ever subtracts.
            if (dangerMice.Length > 1 && random.Bounded(10) == 0)
                angle -= random.Bounded(1.0 / 500.0);

            speed += (-50 + random.Bounded(100)) / 100.0;

            double dx = Math.Sin(angle) * 10;
            eyeDirection = Math.Abs(dx / 5) < 1 ? 0 : dx / 5;

            SetRotation(Rotation() + dx);
            SetPos(MapToParent(0, -(3 + Math.Sin(speed) * 3)));
        }

        static double NormalizeAngle(double angle)
        {
            while (angle < 0)
                angle += TwoPi;
            while (angle > TwoPi)
                angle -= TwoPi;
            return angle;
        }
    }

    public static int Run(ExampleContext example)
    {

        var scene = new QGraphicsScene();
        scene.SetSceneRect(new QRectF(-300, -300, 600, 600));
        scene.SetItemIndexMethod(QGraphicsScene.ItemIndexMethod.NoIndex);

        const int mouseCount = 7;
        var mice = new MouseItem[mouseCount];
        for (int i = 0; i < mouseCount; i++)
        {
            var mouse = new MouseItem();
            mouse.SetPos(Math.Sin((i * 6.28) / mouseCount) * 200,
                         Math.Cos((i * 6.28) / mouseCount) * 200);
            scene.AddItem(mouse);
            mice[i] = mouse;
        }

        var view = new QGraphicsView(scene);
        view.SetWindowTitle("Colliding Mice");
        view.SetRenderHint(QPainter.RenderHint.Antialiasing);
        view.SetViewportUpdateMode(QGraphicsView.ViewportUpdateMode.BoundingRectViewportUpdate);
        view.SetDragMode(QGraphicsView.DragMode.ScrollHandDrag);
        // The upstream background is :/images/cheese.jpg; a plain colour keeps the example
        // self-contained.
        using (var background = new QBrush(new QColor(250, 245, 235)))
            view.SetBackgroundBrush(background);
        view.Resize(400, 400);
        #if QTSHARP_EXAMPLE_SMOKE
            view.Show();
        #else
            example.Show(view);
        #endif

#if QTSHARP_EXAMPLE_SMOKE

        example.Check(scene.Items().Length == mouseCount,
            $"the scene holds the seven mice ({scene.Items().Length})");
#endif

        // Qt's scene advance calls every item's advance(1) through the vtable.
        var before = ((MouseItem)mice[0]).Pos();
        for (int frame = 0; frame < 10; frame++)
            scene.Advance();

        int advanced = 0;
        for (int i = 0; i < mouseCount; i++)
            if (mice[i].AdvanceCalls >= 10)
                advanced++;
        #if QTSHARP_EXAMPLE_SMOKE
        example.Check(advanced == mouseCount,
            $"Qt advanced every managed mouse ({advanced}/{mouseCount} reached ten frames)");
        #endif

        var after = ((MouseItem)mice[0]).Pos();
        #if QTSHARP_EXAMPLE_SMOKE
        example.Check(Math.Abs(after.X() - before.X()) + Math.Abs(after.Y() - before.Y()) > 0.01,
            $"the managed advance() moved the mouse ({before.X():F1},{before.Y():F1} -> {after.X():F1},{after.Y():F1})");
        #endif

        // ItemsBoundingRect asks each item for boundingRect(); grabbing the view paints every item.
        var bounds = scene.ItemsBoundingRect();
        #if QTSHARP_EXAMPLE_SMOKE
        example.Check(bounds.Width() > 0 && bounds.Height() > 0,
            $"Qt asked the managed items for their bounding rectangles ({bounds.Width():F0}x{bounds.Height():F0})");
        #endif

        using var shot = view.Grab();
        int painted = 0;
        for (int i = 0; i < mouseCount; i++)
            if (mice[i].PaintCalls > 0)
                painted++;
        #if QTSHARP_EXAMPLE_SMOKE
        example.Check(painted == mouseCount,
            $"Qt asked every managed mouse to paint ({painted}/{mouseCount})");
        #endif
        #if QTSHARP_EXAMPLE_SMOKE
        example.Check(ExamplePixels.GrabCovers(shot, 400, 400),
            $"the scene rendered off-screen ({shot.Width()}x{shot.Height()})");
        #endif

        #if QTSHARP_EXAMPLE_SMOKE
        view.Close();
        scene.Dispose();
        view.Dispose();
        #endif

        #if QTSHARP_EXAMPLE_SMOKE
        return example.ExitCode;
        #else
        return 0;
        #endif
    }
}
