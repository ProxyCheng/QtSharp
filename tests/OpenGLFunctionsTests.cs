using System;
using System.Threading;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtOpenGLWidgets;

// QOpenGLFunctions is the GL entry-point table Qt hands to a widget; the hellogl2 port calls
// glClear/glDrawArrays through it. Whether a context exists at all is the platform's call - the
// offscreen QPA plugin may have no GL implementation - so the probe skips like the WebView2 test
// instead of failing on the host's plugin.
[Test("test-opengl-functions")]
public static class OpenGLFunctionsTests
{
    sealed class ProbeWidget : QOpenGLWidget
    {
        public bool Initialized;
        public uint ErrorAfterInit = uint.MaxValue;

        public override void InitializeGL()
        {
            QOpenGLContext context = Context();
            if (context == null)
                return;
            QOpenGLFunctions functions = context.Functions();
            if (functions == null)
                return;

            functions.InitializeOpenGLFunctions();
            functions.GlClearColor(0f, 0f, 0f, 1f);
            functions.GlClear(0x00004000u);   // GL_COLOR_BUFFER_BIT
            functions.GlEnable(0x0B71u);      // GL_DEPTH_TEST
            ErrorAfterInit = functions.GlGetError();
            Initialized = true;
        }
    }

    public static int Run(TestContext context)
    {
        using var probe = new ProbeWidget();
        probe.Resize(64, 64);
        probe.Show();

        for (int i = 0; i < 100 && !probe.Initialized; i++)
        {
            QCoreApplication.ProcessEvents(QEventLoop.ProcessEventsFlags.AllEvents, 10);
            Thread.Sleep(1);
        }

        if (!probe.IsValid() || !probe.Initialized)
        {
            Console.WriteLine(
                $"[SKIP] the {QGuiApplication.platformName()} platform provides no OpenGL context "
                + "for QOpenGLWidget; run with QTSHARP_EXAMPLE_PLATFORM=windows");
            return context.ExitCode;
        }

        context.Check(probe.Initialized, "initializeGL ran and the GL table was initialized");
        context.Check(probe.ErrorAfterInit == 0,
            $"glClearColor/glClear/glEnable leave no GL error (0x{probe.ErrorAfterInit:X})");

        QOpenGLFunctions functions = probe.Context().Functions();
        context.Check(functions != null && functions.GlGetError() == 0,
            "the context exposes the same GL table, still error-free");

        // QSet<QByteArray> is marshalled as a HashSet<QByteArray>, one element at a time. The
        // context is current, so a real GL implementation has at least some extensions; every
        // element the set carries has to be one QOpenGLContext::hasExtension agrees with.
        var extensions = probe.Context().Extensions();
        context.Check(extensions != null && extensions.Count > 0,
            $"QOpenGLContext::extensions returns a set (got {extensions?.Count ?? -1})");
        QByteArray firstExtension = null;
        if (extensions != null)
            foreach (var extension in extensions)
            {
                firstExtension = extension;
                break;
            }
        context.Check(firstExtension != null
                      && firstExtension.ConstData().Length > 0
                      && probe.Context().HasExtension(firstExtension),
            $"a set element is a QByteArray hasExtension confirms ('{firstExtension?.ConstData()}')");

        probe.Close();
        return context.ExitCode;
    }
}
