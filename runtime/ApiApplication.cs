using System;
using System.Runtime.InteropServices;

// The command-line constructors of the application classes: `new QApplication(args)`.
//
// The real signature is QApplication(int &argc, char **argv, int flags) - an int the callee
// rewrites and an array of pointers it holds for the rest of the process. The model's vocabulary
// cannot express either (`char**` has no managed spelling, and a temporary argv would dangle as
// soon as the call returned), which is why the generator publishes these classes without a
// constructor at all and leaves the command line to the runtime. sip and shiboken hand-write the
// very same conversion for the very same reason; this file is the C# side of it.
//
// Three consequences follow from Qt's own rules, and they are all deliberate here:
//   * the application object belongs to the *process*, not to the GC - it has no Dispose, so it
//     can never be deleted;
//   * a second construction returns the instance that already exists (Qt aborts otherwise), so
//     mixing this with ApiRuntime.StartApplication() is safe;
//   * ApiRuntime.CreateApplication copies the strings into native storage, so the managed array
//     may be collected straight afterwards.
namespace QtSharp.Api
{
    // The three classes cannot share this body through a base: Handle's setter is protected on the
    // generated root, so only the class itself may assign it.
    public unsafe partial class QCoreApplication
    {
        public QCoreApplication(string[] args, int flags = 0) : this(IntPtr.Zero)
            => BindApplication(args, flags, 0);

        void BindApplication(string[] args, int flags, int kind)
        {
            Handle = ApiRuntime.CreateApplication(args, flags, kind);
            ApiRuntime.Register(Handle, this);
            ApiRuntime.Watch(Handle);
            ApiRuntime.AttachMetaObject(this);
        }
    }

    public unsafe partial class QGuiApplication
    {
        public QGuiApplication(string[] args, int flags = 0) : this(IntPtr.Zero)
            => BindApplication(args, flags, 1);

        void BindApplication(string[] args, int flags, int kind)
        {
            Handle = ApiRuntime.CreateApplication(args, flags, kind);
            ApiRuntime.Register(Handle, this);
            ApiRuntime.Watch(Handle);
            ApiRuntime.AttachMetaObject(this);
        }
    }

    public unsafe partial class QApplication
    {
        public QApplication(string[] args, int flags = 0) : this(IntPtr.Zero)
            => BindApplication(args, flags, 2);

        void BindApplication(string[] args, int flags, int kind)
        {
            Handle = ApiRuntime.CreateApplication(args, flags, kind);
            ApiRuntime.Register(Handle, this);
            ApiRuntime.Watch(Handle);
            ApiRuntime.AttachMetaObject(this);
        }
    }
}
