param(
    [string]$Configuration = 'Release',
    [int]$Jobs = 0,
    [string]$QtRuntimeDir = ''
)
$ErrorActionPreference = "Continue"

# Builds every module's example executable and runs its examples through run-window-tests.ps1. One
# executable per module: each holds the module's Main, its single QApplication and the
# reflection-discovered examples, so the gate, CI and the release build all iterate the same table.
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
New-Item -ItemType Directory -Path "$root/out" -Force | Out-Null

# Windows PowerShell 5.1 has no $IsWindows; the executable suffix follows the host, not the RID.
$onWindows = $PSVersionTable.PSVersion.Major -lt 6 -or $IsWindows
$exeSuffix = if ($onWindows) { '.exe' } else { '' }

$examples = @(
    @{ Name = 'QtCore';         Project = 'examples/QtCore/QtSharp.Examples.QtCore.csproj';             Exe = 'QtSharp.Examples.QtCore' }
    @{ Name = 'QtGui';          Project = 'examples/QtGui/QtSharp.Examples.QtGui.csproj';               Exe = 'QtSharp.Examples.QtGui' }
    @{ Name = 'QtWidgets';      Project = 'examples/QtWidgets/QtSharp.Examples.QtWidgets.csproj';       Exe = 'QtSharp.Examples.QtWidgets' }
    @{ Name = 'QtNetwork';      Project = 'examples/QtNetwork/QtSharp.Examples.QtNetwork.csproj';       Exe = 'QtSharp.Examples.QtNetwork' }
    @{ Name = 'QtSvg';          Project = 'examples/QtSvg/QtSharp.Examples.QtSvg.csproj';               Exe = 'QtSharp.Examples.QtSvg' }
    @{ Name = 'QtSvgWidgets';   Project = 'examples/QtSvgWidgets/QtSharp.Examples.QtSvgWidgets.csproj'; Exe = 'QtSharp.Examples.QtSvgWidgets' }
    @{ Name = 'QtPrintSupport'; Project = 'examples/QtPrintSupport/QtSharp.Examples.QtPrintSupport.csproj'; Exe = 'QtSharp.Examples.QtPrintSupport' }
    @{ Name = 'QtMultimedia';   Project = 'examples/QtMultimedia/QtSharp.Examples.QtMultimedia.csproj'; Exe = 'QtSharp.Examples.QtMultimedia' }
    @{ Name = 'Charts';         Project = 'examples/Charts/QtSharp.Charts.Examples.csproj';             Exe = 'QtSharp.Charts.Examples' }
    @{ Name = 'Qt3D';           Project = 'examples/Qt3D/QtSharp.Qt3D.Examples.csproj';                 Exe = 'QtSharp.Qt3D.Examples' }
)

$buildArgs = @('--nologo')
if ($QtRuntimeDir) { $buildArgs += "-p:QtRuntimeDir=$QtRuntimeDir" }

foreach ($example in $examples) {
    dotnet build "$root/$($example.Project)" -c $Configuration @buildArgs *> "$root/out/example-$($example.Name)-build.log"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "$($example.Name) example build failed"
        Select-String -Path "$root/out/example-$($example.Name)-build.log" -Pattern "error CS" |
            Select-Object -First 8 | ForEach-Object { Write-Host $_.Line.Trim() }
        exit 1
    }
    & "$root/tools/run-window-tests.ps1" `
        -Exe "$root/examples/$($example.Name)/bin/$Configuration/net10.0/$($example.Exe)$exeSuffix" `
        -Label "$($example.Name) examples" -Jobs $Jobs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

exit 0
