using System;
using System.Collections.Generic;
using QtSharp.Examples.Hosting;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtOpenGL;
using QtSharp.QtOpenGLWidgets;
using QtSharp.QtWidgets;

// Qt's opengl/hellogl2 example: a logo mesh lit per pixel, drawn by a QOpenGLWidget through
// QOpenGLFunctions. The port keeps the upstream structure - initializeGL builds the program, the
// VAO and the VBO; paintGL sets the uniforms and calls glDrawArrays; resizeGL sets the projection;
// the window's sliders drive the three rotations through the same 1/16-degree units.
//
// Deviations from the C++ original:
//   * the normal matrix uniform is gone: QMatrix4x4::normalMatrix() returns a QMatrix3x3, which the
//     model does not publish, so the vertex shader computes mat3(mvMatrix) * normal - the same
//     rotation for a rotation-only model matrix;
//   * QOpenGLVertexArrayObject::Binder (a nested RAII class) is spelled Bind()/Release() by hand;
//   * mouse-drag rotation is not ported; the sliders remain the way to rotate the logo.
[Example("hellogl2")]
public static class Hellogl2Example
{
    public static int Run(ExampleContext example)
    {
        var window = new Hellogl2Window();
        window.Resize(800, 480);
#if QTSHARP_EXAMPLE_SMOKE
        window.Show();
#else
        example.Show(window);
#endif

        Hellogl2Widget logo = window.GlWidget;
        example.Check(logo.LogicalVertexCount == 2466,
            $"the logo mesh keeps hellogl2's {logo.LogicalVertexCount} vertices");

        logo.SetXRotation(-16);
        example.Check(logo.XRot == 360 * 16 - 16,
            $"rotation wraps into [0, 360*16) (got {logo.XRot})");

        logo.SetXRotation(400 * 16);
        example.Check(logo.XRot == 40 * 16,
            $"rotation wraps the other way too (got {logo.XRot})");

        // No event loop in the smoke run: pump until the widget has a context and initializeGL ran.
        for (int i = 0; i < 100 && !logo.GlReady; i++)
        {
            QCoreApplication.ProcessEvents(QEventLoop.ProcessEventsFlags.AllEvents, 10);
            System.Threading.Thread.Sleep(1);
        }

        // The GL path only runs on a platform with a real context; offscreen reports the skip.
        if (!logo.IsValid() || !logo.GlReady)
        {
            Console.WriteLine("[SKIP] this platform provides no OpenGL context for QOpenGLWidget");
            window.Close();
            return example.ExitCode;
        }

        example.Check(logo.GlReady, "initializeGL built the program, the VAO and the VBO");
        example.Check(logo.LastGlError == 0,
            $"the paint path leaves no GL error (0x{logo.LastGlError:X})");

        window.Close();
        return example.ExitCode;
    }
}

/// <summary>The window from the example: the GL widget plus the three rotation sliders.</summary>
public sealed class Hellogl2Window : QWidget
{
    public Hellogl2Widget GlWidget { get; }

    public Hellogl2Window()
    {
        GlWidget = new Hellogl2Widget(this);

        QSlider xSlider = CreateSlider();
        QSlider ySlider = CreateSlider();
        QSlider zSlider = CreateSlider();

        xSlider.ValueChanged += value => GlWidget.SetXRotation(value);
        ySlider.ValueChanged += value => GlWidget.SetYRotation(value);
        zSlider.ValueChanged += value => GlWidget.SetZRotation(value);

        var mainLayout = new QVBoxLayout(this);
        var row = new QWidget();
        var container = new QHBoxLayout(row);
        container.AddWidget(GlWidget);
        container.AddWidget(xSlider);
        container.AddWidget(ySlider);
        container.AddWidget(zSlider);
        mainLayout.AddWidget(row);

        xSlider.SetValue(15 * 16);
        ySlider.SetValue(345 * 16);
        zSlider.SetValue(0 * 16);

        SetWindowTitle("Hello GL");
    }

    static QSlider CreateSlider()
    {
        var slider = new QSlider(Qt.Orientation.Vertical);
        slider.SetRange(0, 360 * 16);
        slider.SetSingleStep(16);
        slider.SetPageStep(15 * 16);
        slider.SetTickInterval(15 * 16);
        slider.SetTickPosition(QSlider.TickPosition.TicksRight);
        return slider;
    }
}

/// <summary>The QOpenGLWidget from the example, drawing through QOpenGLFunctions.</summary>
public sealed class Hellogl2Widget : QOpenGLWidget
{
    // GL enums, spelled numerically: they are macros in the C++ original, not bound values.
    const uint GlDepthBufferBit = 0x00000100;
    const uint GlColorBufferBit = 0x00004000;
    const uint GlDepthTest = 0x0B71;
    const uint GlCullFace = 0x0B44;
    const uint GlFloat = 0x1406;
    const uint GlTriangles = 0x0004;

    readonly bool core;
    readonly Logo logo = new Logo();
    readonly QOpenGLVertexArrayObject vao = new QOpenGLVertexArrayObject();
    readonly QOpenGLBuffer logoVbo = new QOpenGLBuffer(QOpenGLBuffer.Type.VertexBuffer);
    readonly QMatrix4x4 proj = new QMatrix4x4();
    readonly QMatrix4x4 world = new QMatrix4x4();

    QOpenGLFunctions gl;
    QOpenGLShaderProgram program;
    int projMatrixLoc;
    int mvMatrixLoc;
    int lightPosLoc;
    int xRot;
    int yRot;
    int zRot;

    public Hellogl2Widget(QWidget parent = null) : base(parent)
    {
        core = QSurfaceFormat.DefaultFormat().Profile() == QSurfaceFormat.OpenGLContextProfile.CoreProfile;
    }

    public bool GlReady { get; private set; }
    public uint LastGlError { get; private set; }
    public int LogicalVertexCount => logo.VertexCount;
    public int XRot => xRot;
    public int YRot => yRot;
    public int ZRot => zRot;

    public void SetXRotation(int angle)
    {
        angle = NormalizeAngle(angle);
        if (angle != xRot)
        {
            xRot = angle;
            Update();
        }
    }

    public void SetYRotation(int angle)
    {
        angle = NormalizeAngle(angle);
        if (angle != yRot)
        {
            yRot = angle;
            Update();
        }
    }

    public void SetZRotation(int angle)
    {
        angle = NormalizeAngle(angle);
        if (angle != zRot)
        {
            zRot = angle;
            Update();
        }
    }

    static int NormalizeAngle(int angle)
    {
        while (angle < 0)
            angle += 360 * 16;
        while (angle > 360 * 16)
            angle -= 360 * 16;
        return angle;
    }

    public override void InitializeGL()
    {
        QOpenGLContext context = Context();
        gl = context?.Functions();
        if (gl == null)
            return;
        gl.InitializeOpenGLFunctions();
        gl.GlClearColor(0f, 0f, 0f, 1f);

        program = new QOpenGLShaderProgram();
        program.AddShaderFromSourceCode(QOpenGLShader.ShaderType.Vertex,
            core ? VertexShaderCore : VertexShader);
        program.AddShaderFromSourceCode(QOpenGLShader.ShaderType.Fragment,
            core ? FragmentShaderCore : FragmentShader);
        program.BindAttributeLocation("vertex", 0);
        program.BindAttributeLocation("normal", 1);
        program.Link();

        program.Bind();
        projMatrixLoc = program.UniformLocation("projMatrix");
        mvMatrixLoc = program.UniformLocation("mvMatrix");
        lightPosLoc = program.UniformLocation("lightPos");

        // The C++ Binder is a scoped object; the managed spelling is the pair of calls.
        vao.Create();
        vao.Bind();
        logoVbo.Create();
        logoVbo.Bind();
        float[] mesh = logo.Data;
        unsafe
        {
            fixed (float* data = mesh)
                logoVbo.Allocate((IntPtr)data, mesh.Length * sizeof(float));
        }
        SetupVertexAttribs();

        program.SetUniformValue(lightPosLoc, new QVector3D(0f, 0f, 70f));        program.Release();

        GlReady = true;
    }

    public override void PaintGL()
    {
        if (!GlReady)
            return;

        gl.GlClearColor(0f, 0f, 0f, 1f);
        gl.GlClear(GlColorBufferBit | GlDepthBufferBit);
        gl.GlEnable(GlDepthTest);
        gl.GlEnable(GlCullFace);
        SetupVertexAttribs();

        world.SetToIdentity();
        world.Rotate(180.0f - (xRot / 16.0f), 1f, 0f, 0f);
        world.Rotate(yRot / 16.0f, 0f, 1f, 0f);
        world.Rotate(zRot / 16.0f, 0f, 0f, 1f);

        vao.Bind();
        program.Bind();
        program.SetUniformValue(projMatrixLoc, proj);
        program.SetUniformValue(mvMatrixLoc, world);
        gl.GlDrawArrays(GlTriangles, 0, logo.VertexCount);
        program.Release();
        vao.Release();

        LastGlError = gl.GlGetError();
    }

    public override void ResizeGL(int width, int height)
    {
        proj.SetToIdentity();
        proj.Perspective(45.0f, (float)width / Math.Max(1, height), 0.01f, 100.0f);
    }

    void SetupVertexAttribs()
    {
        logoVbo.Bind();
        gl.GlEnableVertexAttribArray(0);
        gl.GlEnableVertexAttribArray(1);
        gl.GlVertexAttribPointer(0, 3, GlFloat, 0, 6 * sizeof(float), IntPtr.Zero);
        gl.GlVertexAttribPointer(1, 3, GlFloat, 0, 6 * sizeof(float),
            (IntPtr)(3 * sizeof(float)));
        logoVbo.Release();
    }

    const string VertexShaderCore =
        "#version 150\n"
        + "in vec4 vertex;\n"
        + "in vec3 normal;\n"
        + "out vec3 vert;\n"
        + "out vec3 vertNormal;\n"
        + "uniform mat4 projMatrix;\n"
        + "uniform mat4 mvMatrix;\n"
        + "void main() {\n"
        + "   vec4 mvPos = mvMatrix * vertex;\n"
        + "   mvPos.z -= 1.0;\n"
        + "   vert = mvPos.xyz;\n"
        + "   vertNormal = mat3(mvMatrix) * normal;\n"
        + "   gl_Position = projMatrix * mvPos;\n"
        + "}\n";

    const string FragmentShaderCore =
        "#version 150\n"
        + "in highp vec3 vert;\n"
        + "in highp vec3 vertNormal;\n"
        + "out highp vec4 fragColor;\n"
        + "uniform highp vec3 lightPos;\n"
        + "void main() {\n"
        + "   highp vec3 L = normalize(lightPos - vert);\n"
        + "   highp float NL = max(dot(normalize(vertNormal), L), 0.0);\n"
        + "   highp vec3 color = vec3(0.39, 1.0, 0.0);\n"
        + "   highp vec3 col = clamp(color * 0.2 + color * 0.8 * NL, 0.0, 1.0);\n"
        + "   fragColor = vec4(col, 1.0);\n"
        + "}\n";

    const string VertexShader =
        "attribute vec4 vertex;\n"
        + "attribute vec3 normal;\n"
        + "varying vec3 vert;\n"
        + "varying vec3 vertNormal;\n"
        + "uniform mat4 projMatrix;\n"
        + "uniform mat4 mvMatrix;\n"
        + "void main() {\n"
        + "   vec4 mvPos = mvMatrix * vertex;\n"
        + "   mvPos.z -= 1.0;\n"
        + "   vert = mvPos.xyz;\n"
        + "   vertNormal = mat3(mvMatrix) * normal;\n"
        + "   gl_Position = projMatrix * mvPos;\n"
        + "}\n";

    const string FragmentShader =
        "varying highp vec3 vert;\n"
        + "varying highp vec3 vertNormal;\n"
        + "uniform highp vec3 lightPos;\n"
        + "void main() {\n"
        + "   highp vec3 L = normalize(lightPos - vert);\n"
        + "   highp float NL = max(dot(normalize(vertNormal), L), 0.0);\n"
        + "   highp vec3 color = vec3(0.39, 1.0, 0.0);\n"
        + "   highp vec3 col = clamp(color * 0.2 + color * 0.8 * NL, 0.0, 1.0);\n"
        + "   gl_FragColor = vec4(col, 1.0);\n"
        + "}\n";
}

/// <summary>The mesh builder from hellogl2's logo.cpp, ported triangle for triangle.</summary>
public sealed class Logo
{
    readonly List<float> values = new List<float>();
    float[] data;

    public Logo()
    {
        const float x1 = +0.06f;
        const float y1 = -0.14f;
        const float x2 = +0.14f;
        const float y2 = -0.06f;
        const float x3 = +0.08f;
        const float y3 = +0.00f;
        const float x4 = +0.30f;
        const float y4 = +0.22f;

        Quad(x1, y1, x2, y2, y2, x2, y1, x1);
        Quad(x3, y3, x4, y4, y4, x4, y3, x3);

        Extrude(x1, y1, x2, y2);
        Extrude(x2, y2, y2, x2);
        Extrude(y2, x2, y1, x1);
        Extrude(y1, x1, x1, y1);
        Extrude(x3, y3, x4, y4);
        Extrude(x4, y4, y4, x4);
        Extrude(y4, x4, y3, x3);

        const int sectors = 100;
        for (int i = 0; i < sectors; ++i)
        {
            float angle = (i * 2 * MathF.PI) / sectors;
            float sin = MathF.Sin(angle);
            float cos = MathF.Cos(angle);
            float x5 = 0.30f * sin;
            float y5 = 0.30f * cos;
            float x6 = 0.20f * sin;
            float y6 = 0.20f * cos;

            angle = ((i + 1) * 2 * MathF.PI) / sectors;
            sin = MathF.Sin(angle);
            cos = MathF.Cos(angle);
            float x7 = 0.20f * sin;
            float y7 = 0.20f * cos;
            float x8 = 0.30f * sin;
            float y8 = 0.30f * cos;

            Quad(x5, y5, x6, y6, x7, y7, x8, y8);

            Extrude(x6, y6, x7, y7);
            Extrude(x8, y8, x5, y5);
        }
    }

    public int Count => values.Count;

    public int VertexCount => values.Count / 6;

    public float[] Data => data ??= values.ToArray();

    void Add(QVector3D position, QVector3D normal)
    {
        values.Add(position.X());
        values.Add(position.Y());
        values.Add(position.Z());
        values.Add(normal.X());
        values.Add(normal.Y());
        values.Add(normal.Z());
    }

    void Quad(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4)
    {
        QVector3D n = QVector3D.Normal(
            new QVector3D(x4 - x1, y4 - y1, 0f),
            new QVector3D(x2 - x1, y2 - y1, 0f));

        Add(new QVector3D(x1, y1, -0.05f), n);
        Add(new QVector3D(x4, y4, -0.05f), n);
        Add(new QVector3D(x2, y2, -0.05f), n);

        Add(new QVector3D(x3, y3, -0.05f), n);
        Add(new QVector3D(x2, y2, -0.05f), n);
        Add(new QVector3D(x4, y4, -0.05f), n);

        n = QVector3D.Normal(
            new QVector3D(x1 - x4, y1 - y4, 0f),
            new QVector3D(x2 - x4, y2 - y4, 0f));

        Add(new QVector3D(x4, y4, 0.05f), n);
        Add(new QVector3D(x1, y1, 0.05f), n);
        Add(new QVector3D(x2, y2, 0.05f), n);

        Add(new QVector3D(x2, y2, 0.05f), n);
        Add(new QVector3D(x3, y3, 0.05f), n);
        Add(new QVector3D(x4, y4, 0.05f), n);
    }

    void Extrude(float x1, float y1, float x2, float y2)
    {
        QVector3D n = QVector3D.Normal(
            new QVector3D(0f, 0f, -0.1f),
            new QVector3D(x2 - x1, y2 - y1, 0f));

        Add(new QVector3D(x1, y1, +0.05f), n);
        Add(new QVector3D(x1, y1, -0.05f), n);
        Add(new QVector3D(x2, y2, +0.05f), n);

        Add(new QVector3D(x2, y2, -0.05f), n);
        Add(new QVector3D(x2, y2, +0.05f), n);
        Add(new QVector3D(x1, y1, -0.05f), n);
    }
}
