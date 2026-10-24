using System;
using QtSharp.Qt3DCore;
using QtSharp.Qt3DRender;
using QtSharp.Qt3DExtras;
using QTransform3D = QtSharp.Qt3DCore.QTransform;

namespace QtSharp.Qt3D.Demos
{
    // Qt's examples/qt3d/3d-text, ported onto the binding. The C++ example creates one extruded
    // text entity per installed font family; the test uses the first two families (the QFont and
    // QExtrudedTextMesh paths are the point, not the font count) and asserts the glyph properties.
    [Demo("qt3d-text")]
    public static class TextDemo
    {
        public static int Run(QApplication app)
        {
            Assert.Reset();

            var view = new Qt3DWindow();
            view.SetTitle("3D Text CPP");
            view.DefaultFrameGraph().SetClearColor(new QColor(210, 210, 220));

            var root = new QEntity();

            // Plane the text stands on.
            var planeEntity = new QEntity(root);
            var planeMesh = new QPlaneMesh { Width = 10f, Height = 10f };
            var planeTransform = new QTransform3D { Translation = new QVector3D(0f, 0f, 0f) };
            var planeMaterial = new QPhongMaterial(root);
            planeMaterial.SetDiffuse(new QColor(150, 150, 150));
            planeEntity.AddComponent(planeMaterial);
            planeEntity.AddComponent(planeMesh);
            planeEntity.AddComponent(planeTransform);

            // Extruded text, one entity per family.
            var textMaterial = new QPhongMaterial(root);
            textMaterial.SetDiffuse(new QColor(111, 150, 255));
            var families = QFontDatabase.Families();
            if (families.Length == 0)
                families = new[] { "Arial" };
            int count = Math.Min(2, families.Length);
            var textMeshes = new QExtrudedTextMesh[count];
            var textTransforms = new QTransform3D[count];
            for (int i = 0; i < count; i++)
            {
                string family = families[i];
                var textEntity = new QEntity(root);
                var textMesh = new QExtrudedTextMesh();
                var font = new QFont(family, 32, -1, false);
                textMesh.SetDepth(0.45f);
                textMesh.SetFont(font);
                textMesh.SetText(family);

                var textTransform = new QTransform3D
                {
                    Translation = new QVector3D(-2.45f, i * 0.5f, 0f),
                    Scale = 0.2f,
                };
                textEntity.AddComponent(textMaterial);
                textEntity.AddComponent(textMesh);
                textEntity.AddComponent(textTransform);
                textMeshes[i] = textMesh;
                textTransforms[i] = textTransform;
            }

            // Camera, with the aspect ratio of the window's screen.
            var screenSize = view.Screen().Size;
            float aspect = screenSize.Height() > 0
                ? (float)screenSize.Width() / screenSize.Height()
                : 16f / 9f;
            var camera = view.Camera();
            camera.Lens.SetPerspectiveProjection(65f, aspect, 0.1f, 100f);
            camera.SetPosition(new QVector3D(0f, 5f, 3f));
            camera.SetViewCenter(new QVector3D(0f, 5f, 0f));

            var cameraController = new QOrbitCameraController(root);
            cameraController.SetCamera(camera);

            view.SetRootEntity(root);

            Assert.Check(Assert.Near(planeMesh.Width, 10f) && Assert.Near(planeMesh.Height, 10f),
                         "10x10 plane");
            Assert.Check(count > 0 && textMeshes[0].Text == families[0],
                         "QExtrudedTextMesh.text is the font family");
            Assert.Check(Assert.Near(textMeshes[0].Depth, 0.45f), "QExtrudedTextMesh.depth");
            Assert.Check(textMeshes[0].Font.Family() == families[0]
                         && textMeshes[0].Font.PointSize() == 32,
                         "QExtrudedTextMesh.font round-trips family and point size");
            Assert.Check(Assert.Near(textTransforms[0].Translation, new QVector3D(-2.45f, 0f, 0f))
                         && Assert.Near(textTransforms[0].Scale, 0.2f),
                         "text transform translation and scale");
            Assert.Check(textMaterial.Diffuse.Red() == 111 && textMaterial.Diffuse.Green() == 150
                         && textMaterial.Diffuse.Blue() == 255,
                         "text material diffuse is QColor(111, 150, 255)");
            Assert.Check(planeMaterial.Diffuse.Red() == 150 && planeMaterial.Diffuse.Green() == 150
                         && planeMaterial.Diffuse.Blue() == 150,
                         "plane material diffuse is QColor(150, 150, 150)");
            Assert.Check(Assert.Near(camera.Position, new QVector3D(0f, 5f, 3f))
                         && Assert.Near(camera.ViewCenter, new QVector3D(0f, 5f, 0f))
                         && Assert.Near(camera.Lens.FieldOfView, 65f),
                         "camera position, view center and field of view");
            Assert.Check(cameraController.Camera?.Handle == camera.Handle,
                         "QOrbitCameraController is attached to the camera");

            DemoWindow.ShowIfRequested(view, app);
            return Assert.Finish("QT3D TEXT");
        }
    }
}
