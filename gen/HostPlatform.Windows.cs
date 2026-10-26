// The Windows half of the generator's host-specific bits: the API model is extracted from the
// MSVC Qt kit, and a clone's kit is looked up by the MSVC folder name. gen.csproj compiles this
// file only on Windows; HostPlatform.Unix.cs is its counterpart, so nothing in the shared sources
// needs an OperatingSystem check.
internal static class HostPlatform
{
    public static bool CanExtract => true;

    public static string DefaultKitName => "msvc2022_64";

    public static void ReportExtractionUnsupported()
    {
    }
}
