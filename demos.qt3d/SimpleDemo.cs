using System;
using QtSharp.Qt3DCore;
using QtSharp.Qt3DRender;
using QtSharp.Qt3DExtras;
using QTransform3D = QtSharp.Qt3DCore.QTransform;

namespace QtSharp.Qt3D.Demos
{
    // Qt's examples/qt3d/simple-cpp, ported onto the binding:
    //   * a Qt3DWindow with a torus and a sphere sharing one QPhongMaterial,
    //   * the camera and QOrbitCameraController set up exactly like the C++ example,
    //   * the sphere's orbit. The C++ example drives a custom OrbitTransformController with a
    //     QPropertyAnimation; QPropertyAnimation is not bound (it needs the QVariant animation
    //     machinery), so the matrix is computed here with the same QMatrix4x4 operations and the
    //     result is asserted instead of animated.
    [Demo("qt3d-simple")]
    public static class SimpleDemo
    {
        public static int Run(QApplication app)
        {
            Assert.Reset();

            var view = new Qt3DWindow();
            view.SetTitle("Simple Qt3D");
            view.Resize(800, 600);

            var root = new QEntity();
            var material = new QPhongMaterial(root);

            // Torus, exactly the official parameters.
            var torus = new QTorusMesh
            {
                Radius = 5f,
                MinorRadius = 1f,
                Rings = 100,
                Slices = 20,
            };
            var torusTransform = new QTransform3D
            {
                Scale3D = new QVector3D(1.5f, 1f, 0.5f),
                Rotation = QQuaternion.FromAxisAndAngle(new QVector3D(1f, 0f, 0f), 45f),
            };
            var torusEntity = new QEntity(root);
            torusEntity.AddComponent(torus);
            torusEntity.AddComponent(torusTransform);
            torusEntity.AddComponent(material);

            // Sphere with the animated orbit transform.
            var sphere = new QSphereMesh
            {
                Radius = 3f,
                GenerateTangents = true,
            };
            var sphereTransform = new QTransform3D();
            var sphereEntity = new QEntity(root);
            sphereEntity.AddComponent(sphere);
            sphereEntity.AddComponent(sphereTransform);
            sphereEntity.AddComponent(material);

            // Camera, as in simple-cpp.
            var camera = view.Camera();
            camera.Lens.SetPerspectiveProjection(45f, 16f / 9f, 0.1f, 1000f);
            camera.SetPosition(new QVector3D(0f, 0f, 40f));
            camera.SetViewCenter(new QVector3D(0f, 0f, 0f));

            var cameraController = new QOrbitCameraController(root);
            cameraController.SetLinearSpeed(50f);
            cameraController.SetLookSpeed(180f);
            cameraController.SetCamera(camera);

            view.SetRootEntity(root);

            // The official OrbitTransformController::updateMatrix, computed for a 90-degree angle.
            const float radius = 20f;
            const float angle = 90f;
            var orbit = new QMatrix4x4();
            orbit.SetToIdentity();
            orbit.Rotate(angle, new QVector3D(0f, 1f, 0f));
            orbit.Translate(radius, 0f, 0f);
            sphereTransform.SetMatrix(orbit);
            var orbitPoint = sphereTransform.Matrix.Map(new QVector3D(0f, 0f, 0f));

            Assert.Check(Assert.Near(torus.Radius, 5f) && Assert.Near(torus.MinorRadius, 1f)
                         && torus.Rings == 100 && torus.Slices == 20,
                         "QTorusMesh carries the official torus parameters");
            Assert.Check(Assert.Near(torusTransform.Scale3D, new QVector3D(1.5f, 1f, 0.5f)),
                         "QTransform.scale3D round-trips");
            Assert.Check(Assert.Near(torusTransform.Rotation,
                                     QQuaternion.FromAxisAndAngle(new QVector3D(1f, 0f, 0f), 45f)),
                         "QTransform.rotation is the official 45-degree X rotation");
            Assert.Check(Assert.Near(sphere.Radius, 3f) && sphere.GenerateTangents,
                         "QSphereMesh radius and generateTangents round-trip");
            Assert.Check(Assert.Near(camera.Position, new QVector3D(0f, 0f, 40f))
                         && Assert.Near(camera.ViewCenter, new QVector3D(0f, 0f, 0f)),
                         "camera position and view center");
            Assert.Check(Assert.Near(camera.Lens.FieldOfView, 45f)
                         && Assert.Near(camera.Lens.NearPlane, 0.1f)
                         && Assert.Near(camera.Lens.FarPlane, 1000f),
                         "perspective projection parameters");
            Assert.Check(cameraController.Camera?.Handle == camera.Handle,
                         "QOrbitCameraController is attached to the window camera");
            Assert.Check(torusEntity.Components().Length == 3 && sphereEntity.Components().Length == 3,
                         "each shape entity has three components (mesh, transform, material)");
            Assert.Check(Assert.Near(orbitPoint, new QVector3D(0f, 0f, -radius)),
                         "orbit matrix places the sphere at 90 degrees");

            DemoWindow.ShowIfRequested(view, app);
            return Assert.Finish("QT3D SIMPLE");
        }
    }
}
