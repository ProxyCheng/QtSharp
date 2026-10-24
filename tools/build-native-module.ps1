param(
    [Parameter(Mandatory = $true)][string]$Module,
    [string]$QtDir = '',
    [string]$OutDir = ''
)
# Builds the native glue of an *extension module* - QtSharp.Charts and whatever follows it - into
# its own DLL, linked against that module's Qt library. This is the parameterised twin of
# build_api.bat, which does the same for the core binding.
#
# Everything it needs about the module comes from the generator (`qtgen module <name>`), so adding a
# module stays "a row in the module table + a row in build-qt-kit.ps1's list": no build script edits.
#
#   powershell -File tools/build-native-module.ps1 -Module charts
#
# Prerequisites, same as build_api.bat: a Qt kit with the module's dev files (headers + .lib) and a
# Visual Studio with the C++ tools.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root

if (-not $QtDir) { $QtDir = Join-Path $root 'Qt\6.11.0\msvc2022_64' }
$qtInc = Join-Path $QtDir 'include'
$qtLib = Join-Path $QtDir 'lib'

$genExe = Join-Path $root 'gen\bin\Release\net10.0\win-x64\qtgen.exe'
if (-not (Test-Path -LiteralPath $genExe)) {
    throw "The generator is missing - build it first: dotnet build gen/gen.csproj -c Release"
}

# ---- the descriptor -----------------------------------------------------------------
$descriptor = @{}
foreach ($line in (& $genExe module $Module)) {
    $parts = $line.Split('=', 2)
    if ($parts.Count -eq 2) { $descriptor[$parts[0].Trim()] = $parts[1].Trim() }
}
if (-not $descriptor.ContainsKey('nativeLibrary')) { throw "Could not read the '$Module' module descriptor." }
$nativeLibrary = $descriptor['nativeLibrary']
$ns = $descriptor['namespace']
$qtModules = $descriptor['qtModules'].Split(',')
$qtLibs = $descriptor['qtLibs'].Split(',')
Write-Host "[module] $Module -> $ns ($($descriptor['classes']) classes), modules $($qtModules -join ', '), links $($qtLibs -join ', ')"

$source = Join-Path $root "out\$Module\$ns.native.cpp"
if (-not (Test-Path -LiteralPath $source)) {
    throw "'$source' is missing - generate it first: qtgen codegen model/qtsharp.$Module.json out/$Module --module $Module"
}
foreach ($qtModule in $qtModules) {
    if (-not (Test-Path -LiteralPath (Join-Path $qtInc $qtModule))) {
        throw "The Qt module '$qtModule' is not in the kit at $qtInc - see README.md (Prerequisites)."
    }
}

if (-not $OutDir) { $OutDir = Join-Path $root "out\api-modules\$Module" }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

# ---- a Visual Studio environment ----------------------------------------------------
# Same probing as build_api.bat: VCVARS overrides, otherwise the usual install roots and editions.
$vcvars = $env:VCVARS
if (-not $vcvars) {
    foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, 'D:\Program Files', 'E:\Program Files')) {
        foreach ($vs in @('2022', '2019', '18')) {
            foreach ($edition in @('Community', 'Professional', 'Enterprise', 'BuildTools', 'Insiders', 'Preview')) {
                if ($vcvars) { continue }
                $candidate = Join-Path $base "Microsoft Visual Studio\$vs\$edition\VC\Auxiliary\Build\vcvarsall.bat"
                if (Test-Path -LiteralPath $candidate) { $vcvars = $candidate }
            }
        }
    }
}
if (-not $vcvars -or -not (Test-Path -LiteralPath $vcvars)) {
    throw 'vcvarsall.bat was not found. Set VCVARS, or see README.md (Prerequisites).'
}

Write-Host "[module] entering the Visual Studio environment from $vcvars"
$toolset = if ($env:VCVARS_VER) { "-vcvars_ver=$($env:VCVARS_VER)" } else { '' }
$environment = cmd /c "`"$vcvars`" x64 $toolset >nul 2>&1 && set"
foreach ($line in $environment) {
    $parts = $line.Split('=', 2)
    if ($parts.Count -eq 2) { Set-Item -Path "env:$($parts[0])" -Value $parts[1] }
}

# ---- compile ------------------------------------------------------------------------
# The module's own header directory plus the ones its headers pull in (QtCharts derives from
# QtWidgets, and every Qt module needs QtCore/QtGui).
$includes = @(
    'QtCore', 'QtGui', 'QtWidgets',
    'QtNetwork', 'QtSvg', 'QtSvgWidgets', 'QtPrintSupport', 'QtXml', 'QtOpenGLWidgets', 'QtOpenGL'
) + $qtModules | ForEach-Object { "/I$qtInc\$_" }

$versionDir = Get-ChildItem (Join-Path $qtInc 'QtCore') -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^6\.' } | Select-Object -First 1
$privateIncludes = @()
if ($versionDir) {
    $privateIncludes = @("/I$($versionDir.FullName)", "/I$($versionDir.FullName)\QtCore")
}

$out = Join-Path $OutDir "$nativeLibrary.dll"
Write-Host "[module] compiling $source"
& cl /nologo /LD /EHsc /permissive- /std:c++17 /Zc:__cplusplus /utf-8 `
    /DUNICODE /D_UNICODE /DQT_NO_DEBUG /MD /O2 `
    "/Fe$out" "/Fo$OutDir\" `
    $includes $privateIncludes "/I$qtInc" "/Inative" `
    $source `
    "/link" "/LIBPATH:$qtLib" ($qtLibs | ForEach-Object { "$_.lib" }) Qt6Widgets.lib Qt6Gui.lib Qt6Core.lib
if ($LASTEXITCODE -ne 0) { throw 'The module native build failed.' }

$size = [math]::Round((Get-Item -LiteralPath $out).Length / 1KB, 0)
Write-Host "[module] wrote $out ($size KB)"
