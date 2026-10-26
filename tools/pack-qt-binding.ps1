param(
    [string]$PackageVersion = '0.17.0',
    [string]$QtRuntimeDir = '',
    [string]$QtLinuxRuntimeDir = '',
    [switch]$RequireLinuxPayload,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root

# The binding is shipped in the PyQt6 shape: a binding runtime package, the Qt runtime package,
# the base binding as one package of ten module assemblies, and an addon package with a companion
# Qt-runtime package for each addon (Charts, Qt3D, Multimedia, Positioning). This script packs all
# of them; the generated sources and native glues have to exist already (tools/regen-api.ps1).
#
# Every package that carries native code is multi-RID: tools/native/win-x64/ and
# tools/native/linux-x64/. The Windows payload comes from a Windows build (Qt\6.12.0\runtime),
# the Linux one from tools/build-qt-kit.sh plus build_api.sh / build-native-module.sh.
# -RequireLinuxPayload makes the content check insist on both (the CI/release pack job does).
$BindingNative = "$root/out/api/QtSharp.Api.dll"
$BindingNativeLinux = "$root/out/api/libQtSharp.Api.so"
if (-not $QtRuntimeDir) { $QtRuntimeDir = "$root/Qt/6.12.0/runtime" }
if (-not $QtLinuxRuntimeDir) { $QtLinuxRuntimeDir = "$root/out/qt-kit/runtime" }
# The pack projects evaluate Exists() against their own directory, so a relative path from a
# pack job (artifacts downloaded into the workspace) would look inside nuget/QtSharp.Qt6/.
# Resolve both to absolute paths before they reach dotnet pack.
if (Test-Path -LiteralPath $QtRuntimeDir) { $QtRuntimeDir = (Resolve-Path -LiteralPath $QtRuntimeDir).ProviderPath }
if (Test-Path -LiteralPath $QtLinuxRuntimeDir) { $QtLinuxRuntimeDir = (Resolve-Path -LiteralPath $QtLinuxRuntimeDir).ProviderPath }
$QtRuntime = $QtRuntimeDir
$QtLinuxRuntime = $QtLinuxRuntimeDir
$modules = @('qtcore', 'qtgui', 'qtwidgets', 'qtnetwork', 'qtsvg', 'qtsvgwidgets',
             'qtprintsupport', 'qtopengl', 'qtopenglwidgets',
             'charts', 'qt3dcore', 'qt3drender', 'qt3dinput', 'qt3dlogic', 'qt3danimation', 'qt3dextras',
             'qtmultimedia', 'qtpositioning')

if (-not $SkipBuild) {
    if (-not (Test-Path -LiteralPath $BindingNative) -and -not (Test-Path -LiteralPath $BindingNativeLinux)) {
        throw "Neither '$BindingNative' nor '$BindingNativeLinux' exists. Build the binding first: powershell -File tools/regen-api.ps1 (see README.md, section Prerequisites)."
    }
    foreach ($module in $modules) {
        $path = "$root/out/$module/generated"
        if (-not (Test-Path -LiteralPath $path)) {
            throw "'$path' is missing. Build the binding first: powershell -File tools/regen-api.ps1."
        }
    }
}

$packages = @(
    @{ Name = 'QtSharp.Runtime';     Args = @("-p:QtSharpApiDir=$root/out/api", "-p:QtSharpApiLinuxDir=$root/out/api") },
    @{ Name = 'QtSharp.Qt6';         Args = @("-p:QtRuntimeDir=$QtRuntime", "-p:QtLinuxRuntimeDir=$QtLinuxRuntime") },
    @{ Name = 'QtSharp.Api';         Args = @() },
    @{ Name = 'QtSharp.Charts.Qt6';  Args = @("-p:QtChartsRuntimeDir=$QtRuntime") },
    @{ Name = 'QtSharp.Charts';      Args = @() },
    @{ Name = 'QtSharp.Qt3D.Qt6';    Args = @("-p:Qt3DRuntimeDir=$QtRuntime") },
    @{ Name = 'QtSharp.Qt3D';        Args = @() },
    @{ Name = 'QtSharp.Multimedia.Qt6'; Args = @("-p:QtMultimediaRuntimeDir=$QtRuntime") },
    @{ Name = 'QtSharp.Multimedia';  Args = @() },
    @{ Name = 'QtSharp.Positioning.Qt6'; Args = @("-p:QtPositioningRuntimeDir=$QtRuntime") },
    @{ Name = 'QtSharp.Positioning';  Args = @() },
    @{ Name = 'QtSharp.Sql';          Args = @() }
)

$outDir = "$root/out/packages"
foreach ($package in $packages) {
    Write-Host "=== Pack $($package.Name) ==="
    $arguments = @('pack', "$root/nuget/$($package.Name)/$($package.Name).csproj",
                   '-c', 'Release', '-o', $outDir, '--nologo', "-p:Version=$PackageVersion") + $package.Args
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "$($package.Name) pack failed." }
}

# What each package must contain: the package graph is only useful if the payload actually lands
# where buildTransitive expects it, so every package is inspected rather than trusted.
$expected = @{
    'QtSharp.Runtime'    = @('lib/net10.0/QtSharp.Runtime.dll', 'tools/native/win-x64/QtSharp.Api.dll',
                             'buildTransitive/QtSharp.Runtime.targets')
    'QtSharp.Qt6'        = @('tools/native/win-x64/Qt6Core.dll', 'tools/native/win-x64/Qt6Widgets.dll',
                             'tools/native/win-x64/platforms/qwindows.dll', 'buildTransitive/QtSharp.Qt6.targets')
    'QtSharp.Api'        = @('lib/net10.0/QtSharp.QtCore.dll', 'lib/net10.0/QtSharp.QtGui.dll',
                             'lib/net10.0/QtSharp.QtWidgets.dll', 'lib/net10.0/QtSharp.QtNetwork.dll',
                             'lib/net10.0/QtSharp.QtSvg.dll', 'lib/net10.0/QtSharp.QtSvgWidgets.dll',
                             'lib/net10.0/QtSharp.QtPrintSupport.dll',
                             'lib/net10.0/QtSharp.QtOpenGL.dll', 'lib/net10.0/QtSharp.QtOpenGLWidgets.dll')
    'QtSharp.Charts.Qt6' = @('tools/native/win-x64/Qt6Charts.dll', 'buildTransitive/QtSharp.Charts.Qt6.targets')
    'QtSharp.Charts'     = @('lib/net10.0/QtSharp.Charts.dll', 'tools/native/win-x64/QtSharp.Charts.Native.dll',
                             'buildTransitive/QtSharp.Charts.targets')
    'QtSharp.Qt3D.Qt6'   = @('tools/native/win-x64/Qt63DCore.dll', 'tools/native/win-x64/Qt6ShaderTools.dll',
                             'tools/native/win-x64/renderers', 'buildTransitive/QtSharp.Qt3D.Qt6.targets')
    'QtSharp.Qt3D'       = @('lib/net10.0/QtSharp.Qt3DCore.dll', 'lib/net10.0/QtSharp.Qt3DExtras.dll',
                             'tools/native/win-x64/QtSharp.Qt3DCore.Native.dll',
                             'tools/native/win-x64/QtSharp.Qt3DExtras.Native.dll')
    'QtSharp.Multimedia.Qt6' = @('tools/native/win-x64/Qt6Multimedia.dll', 'tools/native/win-x64/Qt6MultimediaWidgets.dll',
                                 'tools/native/win-x64/multimedia', 'buildTransitive/QtSharp.Multimedia.Qt6.targets')
    'QtSharp.Multimedia' = @('lib/net10.0/QtSharp.QtMultimedia.dll',
                             'tools/native/win-x64/QtSharp.QtMultimedia.Native.dll',
                             'buildTransitive/QtSharp.Multimedia.targets')
    'QtSharp.Positioning.Qt6' = @('tools/native/win-x64/Qt6Positioning.dll',
                                  'buildTransitive/QtSharp.Positioning.Qt6.targets')
    'QtSharp.Positioning' = @('lib/net10.0/QtSharp.QtPositioning.dll',
                              'tools/native/win-x64/QtSharp.QtPositioning.Native.dll',
                              'buildTransitive/QtSharp.Positioning.targets')
    'QtSharp.Sql' = @('lib/net10.0/QtSharp.QtSql.dll')
}

# The Linux payload each package must carry when -RequireLinuxPayload is set. Addon Qt-runtime
# packages deliberately have none: the base QtSharp.Qt6 package deploys the whole Qt tree.
$expectedLinux = @{
    'QtSharp.Runtime'    = @('tools/native/linux-x64/libQtSharp.Api.so')
    'QtSharp.Qt6'        = @('tools/native/linux-x64/lib/libQt6Core.so.6',
                             'tools/native/linux-x64/lib/libQt6Widgets.so.6',
                             'tools/native/linux-x64/qt.conf',
                             'tools/native/linux-x64/plugins/platforms/libqxcb.so')
    'QtSharp.Charts'     = @('tools/native/linux-x64/libQtSharp.Charts.Native.so')
    'QtSharp.Qt3D'       = @('tools/native/linux-x64/libQtSharp.Qt3DCore.Native.so',
                             'tools/native/linux-x64/libQtSharp.Qt3DRender.Native.so',
                             'tools/native/linux-x64/libQtSharp.Qt3DInput.Native.so',
                             'tools/native/linux-x64/libQtSharp.Qt3DLogic.Native.so',
                             'tools/native/linux-x64/libQtSharp.Qt3DAnimation.Native.so',
                             'tools/native/linux-x64/libQtSharp.Qt3DExtras.Native.so')
    'QtSharp.Multimedia' = @('tools/native/linux-x64/libQtSharp.QtMultimedia.Native.so')
    'QtSharp.Positioning' = @('tools/native/linux-x64/libQtSharp.QtPositioning.Native.so')
}

Write-Host "=== Inspect the packages ==="
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($package in $packages) {
    # The version starts with a digit, which keeps QtSharp.Multimedia.* from also matching
    # QtSharp.Multimedia.Qt6.* (whose name starts with a letter).
    $file = Get-ChildItem "$outDir/$($package.Name).*.nupkg" |
        Where-Object { $_.Name -match "^$([regex]::Escape($package.Name))\.\d" } |
        Sort-Object LastWriteTime | Select-Object -Last 1
    if (-not $file) { throw "No $($package.Name) package was produced." }
    $zip = [System.IO.Compression.ZipFile]::OpenRead($file.FullName)
    try {
        $names = $zip.Entries | ForEach-Object { $_.FullName }
        $required = @($expected[$package.Name])
        if ($RequireLinuxPayload -and $expectedLinux.ContainsKey($package.Name)) {
            $required += $expectedLinux[$package.Name]
        }
        foreach ($entry in $required) {
            # A directory entry is enough for a plugin folder (renderers/, multimedia/).
            if (-not ($names | Where-Object { $_ -eq $entry -or $_ -like "$entry/*" })) {
                throw "$($package.Name) is missing '$entry'."
            }
        }
        Write-Host ("  {0,-22} {1,8:N1} KB  {2} entries" -f $file.Name, ($file.Length / 1KB), $zip.Entries.Count)
    } finally { $zip.Dispose() }
}
Write-Host "OK: 12 packages in $outDir"
