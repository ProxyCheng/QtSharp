param(
    [string]$QtVersion = '6.11.0',
    [string]$KitDir = '',
    [string]$Dest = '',
    [switch]$Force
)
# Makes the Qt pieces the binding needs, on a machine that has nothing Qt installed:
#   <Dest>\kit      dev files (include\ + lib\)                 -> QT_DIR / QTSHARP_QT_INCLUDE
#   <Dest>\runtime  the DLLs a built app loads, plus platforms\ -> QtRuntimeDir
#
# The archives come from Qt's own online repository, the same ones the official installer fetches.
# They are layout-specific, so the kit root is *discovered* (the directory holding
# include\QtCore\qobject.h) instead of assumed. tools/regen-api.ps1 only needs the kit for the
# native build; the API model is checked in, so no clang is required here.
#
# If -KitDir points at an existing Qt kit, its dev files are used as-is and only the runtime DLLs
# are looked for under <KitDir>\bin.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root

if (-not $Dest) { $Dest = Join-Path $root 'out\qt-kit' }
$kitOut = Join-Path $Dest 'kit'
$runtimeOut = Join-Path $Dest 'runtime'
$cache = Join-Path $root 'out\qt-cache'

function Get-SevenZip {
    foreach ($candidate in @("$env:ProgramFiles\7-Zip\7z.exe", "${env:ProgramFiles(x86)}\7-Zip\7z.exe")) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    $cmd = Get-Command 7z -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw '7-Zip was not found. Install it, or pass -KitDir pointing at an existing Qt kit.'
}

function Find-One([string]$Base, [string]$Relative) {
    $hit = Get-ChildItem -Path $Base -Recurse -Filter (Split-Path -Leaf $Relative) -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName.Replace('\', '/') -like "*/$($Relative.Replace('\', '/'))" } |
        Select-Object -First 1
    return $hit
}

# ---- 1. The dev kit ----------------------------------------------------------------
$kitRoot = $null
if ($KitDir) {
    $kitRoot = (Resolve-Path -LiteralPath $KitDir).Path
    Write-Host "using the dev files already at $kitRoot"
} else {
    $sevenZip = Get-SevenZip
    $short = $QtVersion.Replace('.', '')
    $repoBase = "https://download.qt.io/online/qtsdkrepository/windows_x86/desktop/qt6_$short/qt6_${short}_msvc2022_64"
    $packageDir = "$repoBase/qt.qt6.$short.win64_msvc2022_64"

    Write-Host "listing $packageDir"
    $listing = (Invoke-WebRequest -Uri "$packageDir/" -Headers @{ 'User-Agent' = 'qtsharp-build' } -TimeoutSec 120).Content
    $archives = @()
    foreach ($pkg in @('qtbase', 'qtsvg')) {
        $match = [regex]::Matches($listing, "href=`"([^`"]*$pkg-[^`"]*-X86_64\.7z)`"") | Select-Object -First 1
        if (-not $match) { throw "No $pkg archive is published for Qt $QtVersion under $packageDir." }
        $archives += $match.Groups[1].Value
    }

    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    New-Item -ItemType Directory -Path $kitOut -Force | Out-Null
    foreach ($archive in $archives) {
        $file = Join-Path $cache $archive
        if ($Force -or -not (Test-Path -LiteralPath $file)) {
            Write-Host "downloading $archive"
            & curl.exe -fSL --retry 3 --retry-delay 5 -o $file "$packageDir/$archive"
            if ($LASTEXITCODE -ne 0) { throw "Download failed: $archive" }
        }
        Write-Host "extracting $archive"
        & $sevenZip x $file "-o$kitOut" -y -bso0 -bsp0
        if ($LASTEXITCODE -ne 0) { throw "Extraction failed: $archive" }
    }

    $qobject = Find-One $kitOut 'include\QtCore\qobject.h'
    if (-not $qobject) { throw "The extracted archives do not contain include\QtCore\qobject.h under $kitOut." }
    $kitRoot = $qobject.Directory.Parent.Parent.FullName
    Write-Host "discovered the Qt kit root: $kitRoot"
}

$privateHeader = Get-ChildItem -Path (Join-Path $kitRoot 'include\QtCore') -Recurse -Filter 'qobject_p.h' -File -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $privateHeader) { throw "The kit at $kitRoot has no QtCore private headers - the runtime needs them (see README.md)." }

# ---- 2. The runtime DLLs the built app loads ----------------------------------------
$binDir = Join-Path $kitRoot 'bin'
if (-not (Test-Path -LiteralPath (Join-Path $binDir 'Qt6Core.dll'))) {
    throw "No Qt6Core.dll under $binDir. Point -KitDir at a kit that ships the runtime DLLs, or add them to <Dest>\runtime by hand."
}
New-Item -ItemType Directory -Path $runtimeOut -Force | Out-Null
Copy-Item -Path (Join-Path $binDir 'Qt6*.dll') -Destination $runtimeOut -Force
foreach ($crt in @('msvcp140*.dll', 'vcruntime140*.dll', 'concrt140.dll')) {
    Get-ChildItem -Path (Join-Path $binDir $crt) -File -ErrorAction SilentlyContinue | Copy-Item -Destination $runtimeOut -Force
}
$plugin = Get-ChildItem -Path (Join-Path $kitRoot 'plugins\platforms') -Filter 'qwindows.dll' -File -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $plugin) {
    Get-ChildItem -Path $kitRoot -Recurse -Filter 'qwindows.dll' -File -ErrorAction SilentlyContinue |
        Select-Object -First 1 | ForEach-Object { $plugin = $_ }
}
if (-not $plugin) { throw "qwindows.dll was not found under $kitRoot - the built app cannot show a window without it." }
New-Item -ItemType Directory -Path (Join-Path $runtimeOut 'platforms') -Force | Out-Null
Copy-Item -LiteralPath $plugin.FullName -Destination (Join-Path $runtimeOut 'platforms') -Force

# ---- 3. What the caller needs ------------------------------------------------------
$core = (Get-Item (Join-Path $runtimeOut 'Qt6Core.dll')).VersionInfo.FileVersion
Write-Host "QT_DIR=$kitRoot"
Write-Host "QT_RUNTIME_DIR=$runtimeOut"
Write-Host "Qt runtime version: $core"
