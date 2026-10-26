param(
    [string]$PackageVersion = '0.12.0',
    [string]$PackagesDir = '',
    [string]$WorkDir = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $PackagesDir) { $PackagesDir = Join-Path $root 'out\packages' }
if (-not $WorkDir) { $WorkDir = Join-Path $root 'out\package-consumer' }
$PackagesDir = (Resolve-Path -LiteralPath $PackagesDir).ProviderPath

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
  <ItemGroup>
    <PackageReference Include="QtSharp.Api" Version="$PackageVersion" />
    <PackageReference Include="QtSharp.Charts" Version="$PackageVersion" />
    <PackageReference Include="QtSharp.Qt3D" Version="$PackageVersion" />
    <PackageReference Include="QtSharp.WebSockets" Version="$PackageVersion" />
    <PackageReference Include="QtSharp.Multimedia" Version="$PackageVersion" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $WorkDir 'PackageConsumer.csproj') -Encoding utf8

@"
using System;
using QtSharp.Charts;
using QtSharp.Qt3DCore;
using QtSharp.QtMultimedia;
using QtSharp.QtWebSockets;
using QtSharp.QtWidgets;

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
        using var server = new QWebSocketServer("consumer", QWebSocketServer.SslMode.NonSecureMode);
        using var player = new QMediaPlayer();
        Console.WriteLine(`$"[PASS] {app.GetType().FullName}, {chart.GetType().FullName}, {entity.GetType().FullName}, {server.GetType().FullName}, {player.GetType().FullName}");
        return 0;
    }
}
"@ | Set-Content -LiteralPath (Join-Path $WorkDir 'Program.cs') -Encoding utf8

# A private global-packages folder, so a cached package with the same version from an earlier run
# cannot stand in for the one this run just built.
$env:NUGET_PACKAGES = Join-Path $WorkDir 'packages'
try {
    $output = & dotnet run --project (Join-Path $WorkDir 'PackageConsumer.csproj') -c Release --nologo 2>&1
    $code = $LASTEXITCODE
} finally {
    Remove-Item Env:\NUGET_PACKAGES -ErrorAction SilentlyContinue
}
$output | ForEach-Object { Write-Host $_ }
if ($code -ne 0) { throw "The package consumer failed with exit code $code." }
if (-not ($output -match '\[PASS\]')) { throw 'The package consumer ran but printed no [PASS] line.' }
Write-Host "OK: QtSharp.Api + QtSharp.Charts + QtSharp.Qt3D + QtSharp.WebSockets + QtSharp.Multimedia restored from $PackagesDir and ran."
