param(
    [string]$PackageVersion = '0.1.0',
    [string]$QtRuntimeDir = '',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root

# What this packs: the managed binding (compiled from the generated source + the runtime), the
# native glue and the Qt runtime DLLs. It needs a machine that can build those - i.e. the Qt kit
# and the MSVC tools from README.md - so the generated artifacts must already exist.
$BindingSource = Join-Path $root 'out\api\generated'
$BindingNative = Join-Path $root 'out\api\QtSharp.Api.dll'
if (-not $QtRuntimeDir) { $QtRuntimeDir = Join-Path $root 'Qt\6.11.0\runtime' }
$QtRuntime = $QtRuntimeDir

if (-not $SkipBuild) {
    foreach ($path in @($BindingSource, $BindingNative, (Join-Path $QtRuntime 'Qt6Core.dll'))) {
        if (-not (Test-Path -LiteralPath $path)) {
            throw "'$path' is missing. Build the binding first: powershell -File tools\regen-api.ps1 (see README.md, section Prerequisites)."
        }
    }
}

Write-Host "=== [1] Pack the binding as QtSharp.Api ==="
dotnet pack (Join-Path $root 'nuget\QtSharp.Api\QtSharp.Api.csproj') -c Release `
    -o (Join-Path $root 'out\packages') "-p:Version=$PackageVersion" "-p:QtRuntimeDir=$QtRuntime" --nologo
if ($LASTEXITCODE -ne 0) { throw 'QtSharp.Api pack failed.' }

$package = Get-ChildItem (Join-Path $root 'out\packages\QtSharp.Api.*.nupkg') |
    Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $package) { throw 'No QtSharp.Api package was produced.' }

Write-Host "=== [2] Inspect the package ==="
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $required = @(
        'lib/net10.0/QtSharp.dll',
        'tools/native/QtSharp.Api.dll',
        'tools/native/Qt6Core.dll',
        'tools/native/Qt6Widgets.dll',
        'tools/native/platforms/qwindows.dll',
        'buildTransitive/QtSharp.Api.props',
        'buildTransitive/QtSharp.Api.targets'
    )
    $names = $zip.Entries | ForEach-Object { $_.FullName }
    foreach ($entry in $required) {
        if ($names -notcontains $entry) { throw "The package is missing '$entry'." }
    }
    Write-Host ("package entries: {0}" -f $zip.Entries.Count)
    Write-Host ("native payload : {0} files" -f ($names | Where-Object { $_ -like 'tools/native/*' }).Count)
} finally { $zip.Dispose() }

Write-Host ("OK  {0}  ({1:N1} KB)" -f $package.Name, ($package.Length / 1KB))
