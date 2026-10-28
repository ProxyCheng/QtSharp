using System;
using System.Runtime.InteropServices;

namespace QtSharp.Tests
{
    /// <summary>
    /// The QPA platform reaches Qt through the C library's environment, not through .NET's copy of
    /// it: Qt reads the variable when the QApplication is constructed, while .NET on Unix keeps an
    /// environment table of its own that <see cref="Environment.SetEnvironmentVariable"/> writes to.
    /// Before the hosts learned to call <c>setenv</c>, the Linux release runner picked xcb and
    /// aborted with "could not connect to display" after the host had printed offscreen. The hosts
    /// write the platform before the application exists, so this checks the half a Windows run
    /// cannot see: that the C environment carries the same value.
    /// </summary>
    [Test("test-platformenv")]
    public static class PlatformEnvironmentApi
    {
        public static int Run(TestContext context)
        {
            string selected = Environment.GetEnvironmentVariable("QT_QPA_PLATFORM") ?? "";
            string native = NativeGet("QT_QPA_PLATFORM") ?? "";
            context.Check(selected.Length > 0, $"the host selected a platform ('{selected}')");
            context.Check(native.Length > 0, $"the C environment carries a platform ('{native}')");
            context.Check(native == selected,
                $"the C environment and the managed copy agree ('{native}' vs '{selected}')");
            return context.ExitCode;
        }

        // Windows reads back through the same CRT table the host wrote with _wputenv_s, so the
        // wide entry point and its encoding; Unix Qt reads the char-based getenv of libc.
        [DllImport("ucrtbase", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern IntPtr _wgetenv(string name);

        [DllImport("libc", EntryPoint = "getenv")]
        static extern IntPtr getenv([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        static string NativeGet(string name)
        {
            IntPtr value = OperatingSystem.IsWindows() ? _wgetenv(name) : getenv(name);
            if (value == IntPtr.Zero)
                return null;
            return OperatingSystem.IsWindows()
                ? Marshal.PtrToStringUni(value)
                : Marshal.PtrToStringUTF8(value);
        }
    }
}
