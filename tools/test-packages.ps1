param(
    [string]$PackageVersion = '0.23.0',
    [string]$PackagesDir = '',
    [string]$WorkDir = '',
    [string]$Platform = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $PackagesDir) { $PackagesDir = Join-Path $root 'out\packages' }
if (-not $WorkDir) { $WorkDir = Join-Path $root 'out\package-consumer' }
$PackagesDir = (Resolve-Path -LiteralPath $PackagesDir).ProviderPath
# The packages are platform-specific (QtSharp.Api.win-x64 / QtSharp.Api.linux-x64): a consumer
# restores the one matching the host it builds and runs on.
if (-not $Platform) {
    $isWindowsHost = $PSVersionTable.PSVersion.Major -lt 6 -or $IsWindows
    $Platform = if ($isWindowsHost) { 'win-x64' } else { 'linux-x64' }
}
if ($Platform -notin @('win-x64', 'linux-x64')) { throw "Unknown platform '$Platform'; QtSharp ships win-x64 and linux-x64." }

# The consumer path, not the source path: a throw-away project restores the *packages* from a local
# feed and uses the base binding, Charts and Qt3D through their public namespaces. This is what
# proves that the NuGet dependency graph and every buildTransitive payload are complete - a source
# build can hide a missing package dependency.
if (Test-Path -LiteralPath $WorkDir) { Remove-Item -LiteralPath $WorkDir -Recurse -Force }
New-Item -ItemType Directory -Path $WorkDir | Out-Null

@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$PackagesDir" />
  </packageSources>
</configuration>
"@ | Set-Content -LiteralPath (Join-Path $WorkDir 'NuGet.config') -Encoding utf8

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <PlatformTarget>x64</PlatformTarget>
    <AssemblyName>PackageConsumer</AssemblyName>
  </PropertyGroup>
  <PropertyGroup Condition="`$([MSBuild]::IsOSPlatform('Windows'))">
    <DefineConstants>WEBVIEW</DefineConstants>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="QtSharp.Api.$Platform" Version="$PackageVersion" />
    <PackageReference Include="QtSharp.Charts.$Platform" Version="$PackageVersion" />
    <PackageReference Include="QtSharp.Qt3D.$Platform" Version="$PackageVersion" />
    <PackageReference Include="QtSharp.Multimedia.$Platform" Version="$PackageVersion" />
    <PackageReference Include="QtSharp.Positioning.$Platform" Version="$PackageVersion" />
    <PackageReference Include="QtSharp.Sql.$Platform" Version="$PackageVersion" />
    <PackageReference Include="QtSharp.WebView.win-x64" Version="$PackageVersion" Condition="`$([MSBuild]::IsOSPlatform('Windows'))" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $WorkDir 'PackageConsumer.csproj') -Encoding utf8

@"
using System;
using QtSharp.Charts;
using QtSharp.Qt3DCore;
using QtSharp.QtMultimedia;
using QtSharp.QtPositioning;
using QtSharp.QtSql;
using QtSharp.QtWidgets;
#if WEBVIEW
using QtSharp.WebView;
#endif

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var app = new QApplication(args);
        using var window = new QWidget();
        window.WindowTitle = "package consumer";
        using var chart = new QChart();
        chart.Title = "package consumer";
        using var entity = new QEntity();
        using var player = new QMediaPlayer();
        using var coordinate = new QGeoCoordinate(52.52, 13.405);
        using var sql = new QSqlQueryModel();
        Console.WriteLine(`$"[PASS] {app.GetType().FullName}, {chart.GetType().FullName}, {entity.GetType().FullName}, {player.GetType().FullName}, {coordinate.GetType().FullName}, {sql.GetType().FullName}");
#if WEBVIEW
        // Not constructed: that would start a browser, and the dependency graph is what this
        // consumer proves. The loader deployment is checked below.
        Console.WriteLine(`$"[PASS] {typeof(WebView2Widget).FullName}");
#endif
        return 0;
    }
}
"@ | Set-Content -LiteralPath (Join-Path $WorkDir 'Program.cs') -Encoding utf8

# A private global-packages folder, so a cached package with the same version from an earlier run
# cannot stand in for the one this run just built.
$env:NUGET_PACKAGES = Join-Path $WorkDir 'packages'
# The consumer constructs a QApplication; on a Linux CI runner there is no display, and the
# packages deploy the offscreen platform plugin, so use it unless the caller set a platform.
$onWindows = $PSVersionTable.PSVersion.Major -lt 6 -or $IsWindows
if (-not $onWindows -and -not $env:QT_QPA_PLATFORM) { $env:QT_QPA_PLATFORM = 'offscreen' }
try {
    $output = & dotnet run --project (Join-Path $WorkDir 'PackageConsumer.csproj') -c Release --nologo 2>&1
    $code = $LASTEXITCODE
} finally {
    Remove-Item Env:\NUGET_PACKAGES -ErrorAction SilentlyContinue
}
$output | ForEach-Object { Write-Host $_ }
if ($code -ne 0) { throw "The package consumer failed with exit code $code." }
if (-not ($output -match '\[PASS\]')) { throw 'The package consumer ran but printed no [PASS] line.' }
# QtSharp.WebView depends on Microsoft.Web.WebView2, whose native asset is the loader the Core
# assembly P/Invokes. It is resolved from the consumer's runtimes/ folder, not the package root.
if ($PSVersionTable.PSVersion.Major -lt 6 -or $IsWindows) {
    $loader = Join-Path $WorkDir 'bin\Release\net10.0\runtimes\win-x64\native\WebView2Loader.dll'
    if (-not (Test-Path -LiteralPath $loader)) {
        throw "QtSharp.WebView did not deploy the WebView2 loader ('$loader')."
    }
    Write-Host "OK: QtSharp.WebView.win-x64 restored and deployed WebView2Loader.dll."
}
Write-Host "OK: QtSharp.Api.$Platform + QtSharp.Charts.$Platform + QtSharp.Qt3D.$Platform + QtSharp.Multimedia.$Platform + QtSharp.Positioning.$Platform + QtSharp.Sql.$Platform restored from $PackagesDir and ran."
