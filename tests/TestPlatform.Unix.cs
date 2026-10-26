// The platform-dependent bits the tests need, one file per platform. QtSharp.BindingTests.csproj
// compiles exactly one of them, so the tests themselves stay free of OperatingSystem checks.
internal static class TestPlatform
{
    public const string Shell = "/bin/sh";
    public static readonly string[] Exit7Args = { "-c", "exit 7" };
    public static readonly string[] EchoArgs = { "-c", "echo captured" };

    // QOperatingSystemVersion::current() reports the Windows version; Qt returns -1.-1 on Linux,
    // where it has no single OS version to report.
    public const bool OperatingSystemVersionIsKnown = false;
}
