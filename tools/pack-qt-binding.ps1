param(
    [string]$PackageVersion = '0.23.0',
    [string]$QtRuntimeDir = '',
    [string]$QtLinuxRuntimeDir = '',
    [string[]]$Platforms = @('win-x64'),
    [switch]$RequireLinuxPayload,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root

# The binding is shipped in the PyQt6 shape: a binding runtime package, the Qt runtime package,
# the base binding as one package of ten module assemblies, and an addon package with a companion
# Qt-runtime package for each addon (Charts, Qt3D, Multimedia, Positioning).
#
# Each of those is packed once per platform: the package id carries the RID
# (QtSharp.Api.win-x64 / QtSharp.Api.linux-x64), and only that platform's native payload is
# inside. NuGet selects packages by id, not by RID, so a platform-specific package is the only way
# to keep the other platform's Qt tree out of a consumer's download. The managed assemblies are
# the same in both.
#
# -Platforms picks the variants to pack (default win-x64). The release and CI pass both; the Linux
# one needs the Qt kit built by tools/build-qt-kit.sh and the .so glue from build_api.sh.
# -RequireLinuxPayload is kept for callers that only want to say "both must be present".
if ($RequireLinuxPayload -and -not ($Platforms -contains 'linux-x64')) {
    $Platforms += 'linux-x64'
}

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
    @{ Name = 'QtSharp.WebView';      Args = @(); Platforms = @('win-x64') },
    @{ Name = 'QtSharp.Sql';          Args = @() }
)

# What each package must contain for a platform: the package graph is only useful if the payload
# actually lands where buildTransitive expects it, so every package is inspected rather than
# trusted. The common rows are checked on both platforms; each platform adds its native rows.
$expectedCommon = @{
    'QtSharp.Runtime'    = @('lib/net10.0/QtSharp.Runtime.dll', 'buildTransitive/QtSharp.Runtime.%rid%.targets')
    'QtSharp.Qt6'        = @('buildTransitive/QtSharp.Qt6.%rid%.targets')
    'QtSharp.Api'        = @('lib/net10.0/QtSharp.QtCore.dll', 'lib/net10.0/QtSharp.QtGui.dll',
                             'lib/net10.0/QtSharp.QtWidgets.dll', 'lib/net10.0/QtSharp.QtNetwork.dll',
                             'lib/net10.0/QtSharp.QtSvg.dll', 'lib/net10.0/QtSharp.QtSvgWidgets.dll',
                             'lib/net10.0/QtSharp.QtPrintSupport.dll',
                             'lib/net10.0/QtSharp.QtOpenGL.dll', 'lib/net10.0/QtSharp.QtOpenGLWidgets.dll')
    'QtSharp.Charts.Qt6' = @('buildTransitive/QtSharp.Charts.Qt6.%rid%.targets')
    'QtSharp.Charts'     = @('lib/net10.0/QtSharp.Charts.dll', 'buildTransitive/QtSharp.Charts.%rid%.targets')
    'QtSharp.Qt3D.Qt6'   = @('buildTransitive/QtSharp.Qt3D.Qt6.%rid%.targets')
    'QtSharp.Qt3D'       = @('lib/net10.0/QtSharp.Qt3DCore.dll', 'lib/net10.0/QtSharp.Qt3DExtras.dll')
    'QtSharp.Multimedia.Qt6' = @('buildTransitive/QtSharp.Multimedia.Qt6.%rid%.targets')
    'QtSharp.Multimedia' = @('lib/net10.0/QtSharp.QtMultimedia.dll', 'buildTransitive/QtSharp.Multimedia.%rid%.targets')
    'QtSharp.Positioning.Qt6' = @('buildTransitive/QtSharp.Positioning.Qt6.%rid%.targets')
    'QtSharp.Positioning' = @('lib/net10.0/QtSharp.QtPositioning.dll', 'buildTransitive/QtSharp.Positioning.%rid%.targets')
    'QtSharp.WebView' = @('lib/net10.0/QtSharp.WebView.dll', 'buildTransitive/QtSharp.WebView.%rid%.props')
    'QtSharp.Sql' = @('lib/net10.0/QtSharp.QtSql.dll')
}

$expectedWin = @{
    'QtSharp.Runtime'    = @('tools/native/win-x64/QtSharp.Api.dll')
    'QtSharp.Qt6'        = @('tools/native/win-x64/Qt6Core.dll', 'tools/native/win-x64/Qt6Widgets.dll',
                             'tools/native/win-x64/platforms/qwindows.dll',
                             'tools/native/win-x64/platforms/qoffscreen.dll',
                             'tools/native/win-x64/platforms/qminimal.dll')
    'QtSharp.Charts.Qt6' = @('tools/native/win-x64/Qt6Charts.dll')
    'QtSharp.Charts'     = @('tools/native/win-x64/QtSharp.Charts.Native.dll')
    'QtSharp.Qt3D.Qt6'   = @('tools/native/win-x64/Qt63DCore.dll', 'tools/native/win-x64/Qt6ShaderTools.dll',
                             'tools/native/win-x64/renderers')
    'QtSharp.Qt3D'       = @('tools/native/win-x64/QtSharp.Qt3DCore.Native.dll',
                             'tools/native/win-x64/QtSharp.Qt3DExtras.Native.dll')
    'QtSharp.Multimedia.Qt6' = @('tools/native/win-x64/Qt6Multimedia.dll', 'tools/native/win-x64/Qt6MultimediaWidgets.dll',
                                 'tools/native/win-x64/multimedia')
    'QtSharp.Multimedia' = @('tools/native/win-x64/QtSharp.QtMultimedia.Native.dll')
    'QtSharp.Positioning.Qt6' = @('tools/native/win-x64/Qt6Positioning.dll')
    'QtSharp.Positioning' = @('tools/native/win-x64/QtSharp.QtPositioning.Native.dll')
    'QtSharp.WebView' = @()
}

# Addon Qt-runtime packages deliberately have no Linux payload: the base QtSharp.Qt6 package
# deploys the whole Qt tree on Linux.
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

$outDir = "$root/out/packages"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$produced = 0
foreach ($platform in $Platforms) {
    if ($platform -notin @('win-x64', 'linux-x64')) {
        throw "Unknown platform '$platform'; QtSharp ships win-x64 and linux-x64."
    }
    if ($platform -eq 'linux-x64' -and -not $RequireLinuxPayload -and -not (Test-Path -LiteralPath $QtLinuxRuntimeDir)) {
        Write-Warning "Skipping linux-x64: '$QtLinuxRuntimeDir' is missing (build the Linux kit first, or pass -RequireLinuxPayload to make this an error)."
        continue
    }
    if ($platform -eq 'linux-x64' -and $RequireLinuxPayload -and -not (Test-Path -LiteralPath $QtLinuxRuntimeDir)) {
        throw "linux-x64 packaging was required, but '$QtLinuxRuntimeDir' is missing."
    }

    $payload = if ($platform -eq 'win-x64') { $expectedWin } else { $expectedLinux }
    $otherRid = if ($platform -eq 'win-x64') { 'linux-x64' } else { 'win-x64' }

    foreach ($package in $packages) {
        if ($package.ContainsKey('Platforms') -and $package.Platforms -notcontains $platform) { continue }
        Write-Host "=== Pack $($package.Name).$platform ==="
        $arguments = @('pack', "$root/nuget/$($package.Name)/$($package.Name).csproj",
                       '-c', 'Release', '-o', $outDir, '--nologo', "-p:Version=$PackageVersion",
                       "-p:QtSharpPlatform=$platform") + $package.Args
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw "$($package.Name).$platform pack failed." }
    }

    # QtSharp.WebView's one non-QtSharp dependency is Microsoft.Web.WebView2, and the documented
    # install path is a local feed built from this archive. Put that package in the feed too, or a
    # consumer restoring with only this folder as its source fails with NU1101. The version comes
    # from the packed nuspec, so it cannot drift from the project.
    if ($platform -eq 'win-x64') {
        $webViewPackage = Join-Path $outDir "QtSharp.WebView.win-x64.$PackageVersion.nupkg"
        if (Test-Path -LiteralPath $webViewPackage) {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $zip = [System.IO.Compression.ZipFile]::OpenRead($webViewPackage)
            try {
                $nuspecEntry = $zip.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
                $reader = New-Object System.IO.StreamReader($nuspecEntry.Open())
                try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
            } finally { $zip.Dispose() }
            $dependency = @($nuspec.package.metadata.dependencies.group.dependency |
                Where-Object { $_.id -eq 'Microsoft.Web.WebView2' })[0]
            if ($dependency) {
                $webView2Version = $dependency.version
                $packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
                $source = Join-Path $packageRoot "microsoft.web.webview2/$webView2Version/microsoft.web.webview2.$webView2Version.nupkg"
                if (-not (Test-Path -LiteralPath $source)) {
                    throw "Microsoft.Web.WebView2 $webView2Version is missing from the NuGet cache ('$source'); restore QtSharp.WebView before packing."
                }
                Copy-Item -LiteralPath $source -Destination (Join-Path $outDir "Microsoft.Web.WebView2.$webView2Version.nupkg") -Force
                Write-Host "  added Microsoft.Web.WebView2.$webView2Version.nupkg to the feed (QtSharp.WebView dependency)"
            }
        }
    }

    Write-Host "=== Inspect the $platform packages ==="
    foreach ($package in $packages) {
        if ($package.ContainsKey('Platforms') -and $package.Platforms -notcontains $platform) { continue }
        # The version starts with a digit, which keeps QtSharp.Multimedia.<rid>.* from also
        # matching QtSharp.Multimedia.Qt6.<rid>.* (whose name starts with a letter).
        $pattern = "^$([regex]::Escape($package.Name))\.$([regex]::Escape($platform))\.\d"
        $file = Get-ChildItem "$outDir/$($package.Name).$platform.*.nupkg" -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match $pattern } |
            Sort-Object LastWriteTime | Select-Object -Last 1
        if (-not $file) { throw "No $($package.Name).$platform package was produced." }
        $zip = [System.IO.Compression.ZipFile]::OpenRead($file.FullName)
        try {
            $names = $zip.Entries | ForEach-Object { $_.FullName }
            $required = @($expectedCommon[$package.Name] | ForEach-Object { $_ -replace '%rid%', $platform })
            if ($payload.ContainsKey($package.Name)) { $required += $payload[$package.Name] }
            foreach ($entry in $required) {
                # A directory entry is enough for a plugin folder (renderers/, multimedia/).
                if (-not ($names | Where-Object { $_ -eq $entry -or $_ -like "$entry/*" })) {
                    throw "$($package.Name).$platform is missing '$entry'."
                }
            }
            # The other platform's payload must not have leaked in.
            $leaked = @($names | Where-Object { $_ -like "tools/native/$otherRid/*" })
            if ($leaked.Count -gt 0) {
                throw "$($package.Name).$platform carries $($leaked.Count) $otherRid entr(ies), e.g. '$($leaked[0])'."
            }
            Write-Host ("  {0,-40} {1,8:N1} KB  {2} entries" -f $file.Name, ($file.Length / 1KB), $zip.Entries.Count)
        } finally { $zip.Dispose() }
        $produced++
    }
}
Write-Host "OK: $produced package(s) in $outDir"
