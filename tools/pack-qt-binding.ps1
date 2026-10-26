param(
    [string]$PackageVersion = '0.3.0',
    [string]$QtRuntimeDir = '',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root

# The binding is shipped in the PyQt6 shape: a binding runtime package, the Qt runtime package,
# the base binding as one package of ten module assemblies, and an addon package with a companion
# Qt-runtime package for each addon (Charts, Qt3D). This script packs all of them; the generated
# sources and native glues have to exist already (tools\regen-api.ps1).
$BindingNative = Join-Path $root 'out\api\QtSharp.Api.dll'
if (-not $QtRuntimeDir) { $QtRuntimeDir = Join-Path $root 'Qt\6.12.0\runtime' }
$QtRuntime = $QtRuntimeDir
$modules = @('qtcore', 'qtgui', 'qtwidgets', 'qtnetwork', 'qtsvg', 'qtsvgwidgets',
             'qtprintsupport', 'qtxml', 'qtopengl', 'qtopenglwidgets',
             'charts', 'qt3dcore', 'qt3drender', 'qt3dinput', 'qt3dlogic', 'qt3danimation', 'qt3dextras')

if (-not $SkipBuild) {
    $required = @($BindingNative, (Join-Path $QtRuntime 'Qt6Core.dll'))
    foreach ($module in $modules) { $required += (Join-Path $root "out\$module\generated") }
    foreach ($path in $required) {
        if (-not (Test-Path -LiteralPath $path)) {
            throw "'$path' is missing. Build the binding first: powershell -File tools\regen-api.ps1 (see README.md, section Prerequisites)."
        }
    }
}

$packages = @(
    @{ Name = 'QtSharp.Runtime';     Args = @("-p:QtSharpApiDir=$(Join-Path $root 'out\api')") },
    @{ Name = 'QtSharp.Qt6';         Args = @("-p:QtRuntimeDir=$QtRuntime") },
    @{ Name = 'QtSharp.Api';         Args = @() },
    @{ Name = 'QtSharp.Charts.Qt6';  Args = @("-p:QtChartsRuntimeDir=$QtRuntime") },
    @{ Name = 'QtSharp.Charts';      Args = @() },
    @{ Name = 'QtSharp.Qt3D.Qt6';    Args = @("-p:Qt3DRuntimeDir=$QtRuntime") },
    @{ Name = 'QtSharp.Qt3D';        Args = @() }
)

$outDir = Join-Path $root 'out\packages'
foreach ($package in $packages) {
    Write-Host "=== Pack $($package.Name) ==="
    $arguments = @('pack', (Join-Path $root "nuget\$($package.Name)\$($package.Name).csproj"),
                   '-c', 'Release', '-o', $outDir, '--nologo', "-p:Version=$PackageVersion") + $package.Args
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "$($package.Name) pack failed." }
}

# What each package must contain: the package graph is only useful if the payload actually lands
# where buildTransitive expects it, so every package is inspected rather than trusted.
$expected = @{
    'QtSharp.Runtime'    = @('lib/net10.0/QtSharp.Runtime.dll', 'tools/native/QtSharp.Api.dll',
                             'buildTransitive/QtSharp.Runtime.targets')
    'QtSharp.Qt6'        = @('tools/native/Qt6Core.dll', 'tools/native/Qt6Widgets.dll',
                             'tools/native/platforms/qwindows.dll', 'buildTransitive/QtSharp.Qt6.targets')
    'QtSharp.Api'        = @('lib/net10.0/QtSharp.QtCore.dll', 'lib/net10.0/QtSharp.QtGui.dll',
                             'lib/net10.0/QtSharp.QtWidgets.dll', 'lib/net10.0/QtSharp.QtNetwork.dll',
                             'lib/net10.0/QtSharp.QtSvg.dll', 'lib/net10.0/QtSharp.QtSvgWidgets.dll',
                             'lib/net10.0/QtSharp.QtPrintSupport.dll', 'lib/net10.0/QtSharp.QtXml.dll',
                             'lib/net10.0/QtSharp.QtOpenGL.dll', 'lib/net10.0/QtSharp.QtOpenGLWidgets.dll')
    'QtSharp.Charts.Qt6' = @('tools/native/Qt6Charts.dll', 'buildTransitive/QtSharp.Charts.Qt6.targets')
    'QtSharp.Charts'     = @('lib/net10.0/QtSharp.Charts.dll', 'tools/native/QtSharp.Charts.Native.dll',
                             'buildTransitive/QtSharp.Charts.targets')
    'QtSharp.Qt3D.Qt6'   = @('tools/native/Qt63DCore.dll', 'tools/native/Qt6ShaderTools.dll',
                             'tools/native/renderers', 'buildTransitive/QtSharp.Qt3D.Qt6.targets')
    'QtSharp.Qt3D'       = @('lib/net10.0/QtSharp.Qt3DCore.dll', 'lib/net10.0/QtSharp.Qt3DExtras.dll',
                             'tools/native/QtSharp.Qt3DCore.Native.dll',
                             'tools/native/QtSharp.Qt3DExtras.Native.dll')
}

Write-Host "=== Inspect the packages ==="
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($package in $packages) {
    $file = Get-ChildItem (Join-Path $outDir "$($package.Name).*.nupkg") |
        Sort-Object LastWriteTime | Select-Object -Last 1
    if (-not $file) { throw "No $($package.Name) package was produced." }
    $zip = [System.IO.Compression.ZipFile]::OpenRead($file.FullName)
    try {
        $names = $zip.Entries | ForEach-Object { $_.FullName }
        foreach ($entry in $expected[$package.Name]) {
            # A directory entry is enough for a plugin folder (tools/native/renderers).
            if (-not ($names | Where-Object { $_ -eq $entry -or $_ -like "$entry/*" })) {
                throw "$($package.Name) is missing '$entry'."
            }
        }
        Write-Host ("  {0,-22} {1,8:N1} KB  {2} entries" -f $file.Name, ($file.Length / 1KB), $zip.Entries.Count)
    } finally { $zip.Dispose() }
}
Write-Host "OK: 7 packages in $outDir"
