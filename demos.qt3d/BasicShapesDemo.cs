using System;
using QtSharp.Qt3DCore;
using QtSharp.Qt3DRender;
using QtSharp.Qt3DExtras;
using QTransform3D = QtSharp.Qt3DCore.QTransform;

namespace QtSharp.Qt3D.Demos
{
    // Qt's examples/qt3d/basicshapes-cpp, ported onto the binding. The C++ example embeds the
    // Qt3DWindow in a QWidget and exposes a checkbox per shape; the scene itself (six ready-made
    // meshes, their materials and transforms, the point light and the camera) is what this test
    // builds. The checkbox toggles are reproduced by SetEnabled/IsEnabled assertions.
    public static class BasicShapesDemo
    {
        sealed class Shape
        {
            public QEntity Entity;
            public QTorusMesh Torus;
            public QConeMesh Cone;
            public QCylinderMesh Cylinder;
            public QCuboidMesh Cuboid;
            public QPlaneMesh Plane;
            public QSphereMesh Sphere;
            public QPhongMaterial Material;
            public QTransform3D Transform;
        }

        static Shape AddShape(QEntity root, QColor color)
        {
            var shape = new Shape { Entity = new QEntity(root) };
            shape.Material = new QPhongMaterial();
            shape.Material.SetDiffuse(color);
            return shape;
        }

        public static int Run(QApplication app)
        {
            Assert.Reset();

            var view = new Qt3DWindow();
            view.SetTitle("Basic shapes");
            view.Resize(1200, 800);
            view.DefaultFrameGraph().SetClearColor(new QColor(0x4d4d4fu));

            var root = new QEntity();

            var camera = view.Camera();
            camera.Lens.SetPerspectiveProjection(45f, 16f / 9f, 0.1f, 1000f);
            camera.SetPosition(new QVector3D(0f, 0f, 20f));
            camera.SetUpVector(new QVector3D(0f, 1f, 0f));
            camera.SetViewCenter(new QVector3D(0f, 0f, 0f));

            // Point light, translated to the camera position (as in basicshapes-cpp).
            var lightEntity = new QEntity(root);
            var light = new QPointLight(lightEntity);
            light.SetColor(new QColor("white"));
            light.SetIntensity(1f);
            lightEntity.AddComponent(light);
            var lightTransform = new QTransform3D(lightEntity);
            lightTransform.Translation = camera.Position;
            lightEntity.AddComponent(lightTransform);

            var cameraController = new QFirstPersonCameraController(root);
            cameraController.SetCamera(camera);

            // Torus.
            var torus = AddShape(root, new QColor(0xbeb32bu));
            torus.Torus = new QTorusMesh { Radius = 1f, MinorRadius = 0.4f, Rings = 100, Slices = 20 };
            torus.Transform = new QTransform3D
            {
                Scale = 2f,
                Rotation = QQuaternion.FromAxisAndAngle(new QVector3D(0f, 1f, 0f), 25f),
                Translation = new QVector3D(5f, 4f, 0f),
            };
            torus.Entity.AddComponent(torus.Torus);
            torus.Entity.AddComponent(torus.Material);
            torus.Entity.AddComponent(torus.Transform);

            // Cone.
            var cone = AddShape(root, new QColor(0x928327u));
            cone.Cone = new QConeMesh { TopRadius = 0.5f, BottomRadius = 1f, Length = 3f, Rings = 50, Slices = 20 };
            cone.Transform = new QTransform3D
            {
                Scale = 1.5f,
                Rotation = QQuaternion.FromAxisAndAngle(new QVector3D(1f, 0f, 0f), 45f),
                Translation = new QVector3D(0f, 4f, -1.5f),
            };
            cone.Entity.AddComponent(cone.Cone);
            cone.Entity.AddComponent(cone.Material);
            cone.Entity.AddComponent(cone.Transform);

            // Cylinder.
            var cylinder = AddShape(root, new QColor(0x928327u));
            cylinder.Cylinder = new QCylinderMesh { Radius = 1f, Length = 3f, Rings = 100, Slices = 20 };
            cylinder.Transform = new QTransform3D
            {
                Scale = 1.5f,
                Rotation = QQuaternion.FromAxisAndAngle(new QVector3D(1f, 0f, 0f), 45f),
                Translation = new QVector3D(-5f, 4f, -1.5f),
            };
            cylinder.Entity.AddComponent(cylinder.Cylinder);
            cylinder.Entity.AddComponent(cylinder.Material);
            cylinder.Entity.AddComponent(cylinder.Transform);

            // Cuboid.
            var cuboid = AddShape(root, new QColor(0x665423u));
            cuboid.Cuboid = new QCuboidMesh();
            cuboid.Transform = new QTransform3D { Scale = 4f, Translation = new QVector3D(5f, -4f, 0f) };
            cuboid.Entity.AddComponent(cuboid.Cuboid);
            cuboid.Entity.AddComponent(cuboid.Material);
            cuboid.Entity.AddComponent(cuboid.Transform);

            // Plane.
            var plane = AddShape(root, new QColor(0xa69929u));
            plane.Plane = new QPlaneMesh { Width = 2f, Height = 2f };
            plane.Transform = new QTransform3D
            {
                Scale = 1.3f,
                Rotation = QQuaternion.FromAxisAndAngle(new QVector3D(1f, 0f, 0f), 45f),
                Translation = new QVector3D(0f, -4f, 0f),
            };
            plane.Entity.AddComponent(plane.Plane);
            plane.Entity.AddComponent(plane.Material);
            plane.Entity.AddComponent(plane.Transform);

            // Sphere.
            var sphere = AddShape(root, new QColor(0xa69929u));
            sphere.Sphere = new QSphereMesh { Rings = 20, Slices = 20, Radius = 2f };
            sphere.Transform = new QTransform3D { Scale = 1.3f, Translation = new QVector3D(-5f, -4f, 0f) };
            sphere.Entity.AddComponent(sphere.Sphere);
            sphere.Entity.AddComponent(sphere.Material);
            sphere.Entity.AddComponent(sphere.Transform);

            view.SetRootEntity(root);

            Assert.Check(Assert.Near(torus.Torus.Radius, 1f) && Assert.Near(torus.Torus.MinorRadius, 0.4f)
                         && torus.Torus.Rings == 100 && torus.Torus.Slices == 20,
                         "torus parameters");
            Assert.Check(Assert.Near(cone.Cone.TopRadius, 0.5f) && Assert.Near(cone.Cone.BottomRadius, 1f)
                         && Assert.Near(cone.Cone.Length, 3f) && cone.Cone.Rings == 50 && cone.Cone.Slices == 20,
                         "cone parameters");
            Assert.Check(Assert.Near(cylinder.Cylinder.Radius, 1f) && Assert.Near(cylinder.Cylinder.Length, 3f)
                         && cylinder.Cylinder.Rings == 100 && cylinder.Cylinder.Slices == 20,
                         "cylinder parameters");
            Assert.Check(Assert.Near(plane.Plane.Width, 2f) && Assert.Near(plane.Plane.Height, 2f),
                         "plane parameters");
            Assert.Check(sphere.Sphere.Rings == 20 && sphere.Sphere.Slices == 20
                         && Assert.Near(sphere.Sphere.Radius, 2f),
                         "sphere parameters");

            Assert.Check(torus.Material.Diffuse.Red() == 0xbe && torus.Material.Diffuse.Green() == 0xb3
                         && torus.Material.Diffuse.Blue() == 0x2b,
                         "torus material diffuse is QColor(0xbeb32b)");
            Assert.Check(cone.Material.Diffuse.Red() == 0x92 && cone.Material.Diffuse.Green() == 0x83
                         && cone.Material.Diffuse.Blue() == 0x27,
                         "cone/cylinder material diffuse is QColor(0x928327)");
            Assert.Check(cuboid.Material.Diffuse.Red() == 0x66 && cuboid.Material.Diffuse.Green() == 0x54
                         && cuboid.Material.Diffuse.Blue() == 0x23,
                         "cuboid material diffuse is QColor(0x665423)");
            Assert.Check(plane.Material.Diffuse.Red() == 0xa6 && plane.Material.Diffuse.Green() == 0x99
                         && plane.Material.Diffuse.Blue() == 0x29,
                         "plane/sphere material diffuse is QColor(0xa69929)");

            Assert.Check(Assert.Near(camera.Position, new QVector3D(0f, 0f, 20f))
                         && Assert.Near(camera.UpVector, new QVector3D(0f, 1f, 0f))
                         && Assert.Near(camera.ViewCenter, new QVector3D(0f, 0f, 0f)),
                         "camera position, up vector and view center");
            Assert.Check(cameraController.Camera?.Handle == camera.Handle,
                         "QFirstPersonCameraController is attached to the camera");
            Assert.Check(light.Color.Red() == 255 && light.Color.Green() == 255 && light.Color.Blue() == 255
                         && Assert.Near(light.Intensity, 1f),
                         "white point light with intensity 1");
            Assert.Check(Assert.Near(lightTransform.Translation, camera.Position),
                         "the light sits at the camera");

            // The official checkboxes call SceneModifier::enableX, which is SetEnabled on the entity.
            foreach (var shape in new[] { torus, cone, cylinder, cuboid, plane, sphere })
            {
                shape.Entity.SetEnabled(false);
                bool off = !shape.Entity.IsEnabled();
                shape.Entity.SetEnabled(true);
                Assert.Check(off && shape.Entity.IsEnabled(), $"shape entity toggles enabled");
            }

            DemoWindow.ShowIfRequested(view, app);
            return Assert.Finish("QT3D BASICSHAPES");
        }
    }
}
