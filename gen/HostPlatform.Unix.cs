using System;

// The non-Windows half of the generator's host-specific bits. The API model is defined by the
// MSVC Qt headers (the clang invocation uses MSVC compatibility flags), so extraction is refused
// with a clear message; codegen and split-core read the checked-in model and work anywhere.
// gen.csproj compiles this file on every non-Windows host; HostPlatform.Windows.cs is its
// counterpart, so nothing in the shared sources needs an OperatingSystem check.
internal static class HostPlatform
{
    public static bool CanExtract => false;

    public static string DefaultKitName => "gcc_64";

    public static void ReportExtractionUnsupported()
    {
        Console.WriteLine("[model2] extracting the API model requires the MSVC Qt headers on Windows.");
        Console.WriteLine("[model2] use the checked-in model instead (qtgen codegen / split-core) - see README.md.");
    }
}
