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

function Get-RemoteFile([string]$Url, [string]$OutFile) {
    # curl first (it reports progress for the large archives); Invoke-WebRequest as a fallback for
    # environments where curl's schannel setup cannot validate the certificate chain.
    & curl.exe -fsSL --ssl-no-revoke --retry 3 --retry-delay 5 -o $OutFile $Url 2>$null
    if ($LASTEXITCODE -eq 0) { return }
    $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 300
    $content = $response.Content
    if ($content -is [byte[]]) { [System.IO.File]::WriteAllBytes($OutFile, $content) }
    else { [System.IO.File]::WriteAllText($OutFile, [string]$content) }
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

    New-Item -ItemType Directory -Path $cache -Force | Out-Null

    # Fetch the listing with curl and read it as text: Invoke-WebRequest's .Content is not always a
    # string, which silently turned the archive pattern match into a null reference on CI.
    Write-Host "listing $packageDir"
    $listingFile = Join-Path $cache 'listing.html'
    Get-RemoteFile "$packageDir/" $listingFile
    $listing = Get-Content -LiteralPath $listingFile -Raw
    $archives = @()
    foreach ($pkg in @('qtbase', 'qtsvg')) {
        $match = [regex]::Match($listing, "href=`"([^`"]*$pkg-[^`"]*-X86_64\.7z)`"")
        if (-not $match.Success) {
            throw "No $pkg archive is published for Qt $QtVersion under $packageDir (the listing was $($listing.Length) characters)."
        }
        $archives += $match.Groups[1].Value
    }

    New-Item -ItemType Directory -Path $kitOut -Force | Out-Null
    foreach ($archive in $archives) {
        $file = Join-Path $cache $archive
        if ($Force -or -not (Test-Path -LiteralPath $file)) {
            Write-Host "downloading $archive"
            Get-RemoteFile "$packageDir/$archive" $file
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
# Only the modules the native glue links (build_api.bat): copying the kit's whole bin would add
# tens of megabytes of Qt modules the binding never touches. Keep this list in step with the
# Qt6*.lib entries in build_api.bat.
$modules = @(
    'Qt6Core', 'Qt6Gui', 'Qt6Widgets', 'Qt6Network', 'Qt6Svg',
    'Qt6SvgWidgets', 'Qt6PrintSupport', 'Qt6Xml', 'Qt6OpenGL', 'Qt6OpenGLWidgets'
)
foreach ($module in $modules) {
    $dll = Join-Path $binDir "$module.dll"
    if (-not (Test-Path -LiteralPath $dll)) {
        throw "$module.dll is missing from $binDir - the kit does not provide a module the binding links."
    }
    Copy-Item -LiteralPath $dll -Destination $runtimeOut -Force
}
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

# Under GitHub Actions, hand the two paths to the following steps through the environment. Exporting
# them here is deliberate: Write-Host writes to the host stream, so a caller cannot capture it by
# assigning the script's output.
if ($env:GITHUB_ENV) {
    "QT_DIR=$kitRoot" >> $env:GITHUB_ENV
    "QT_RUNTIME_DIR=$runtimeOut" >> $env:GITHUB_ENV
}
