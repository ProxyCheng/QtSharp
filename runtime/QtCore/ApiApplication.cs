using System;
using QtSharp.Runtime;

// The command-line constructor of QCoreApplication: the model cannot express Qt's (int& argc, char** argv),
// so the runtime hands Qt real native storage. The application object belongs to the process and
// is never disposed; a second construction returns the existing instance, as Qt requires. It
// lives beside the generated QCoreApplication because a partial class cannot span assemblies.
namespace QtSharp.QtCore
{
    public partial class QCoreApplication
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
}
