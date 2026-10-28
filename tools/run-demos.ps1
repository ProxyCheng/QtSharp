param(
    [string]$Configuration = 'Release',
    [int]$Jobs = 0,
    [string]$QtRuntimeDir = ''
)
$ErrorActionPreference = "Continue"

# Builds every module's demo executable and runs its demos through run-window-tests.ps1. One
# executable per module: each holds the module's Main, its single QApplication and the
# reflection-discovered demos, so the gate, CI and the release build all iterate the same table.
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
New-Item -ItemType Directory -Path "$root/out" -Force | Out-Null

# Windows PowerShell 5.1 has no $IsWindows; the executable suffix follows the host, not the RID.
$onWindows = $PSVersionTable.PSVersion.Major -lt 6 -or $IsWindows
$exeSuffix = if ($onWindows) { '.exe' } else { '' }

$demos = @(
    @{ Name = 'QtCore';         Project = 'demos/QtCore/QtSharp.Demos.QtCore.csproj';             Exe = 'QtSharp.Demos.QtCore' }
    @{ Name = 'QtGui';          Project = 'demos/QtGui/QtSharp.Demos.QtGui.csproj';               Exe = 'QtSharp.Demos.QtGui' }
    @{ Name = 'QtWidgets';      Project = 'demos/QtWidgets/QtSharp.Demos.QtWidgets.csproj';       Exe = 'QtSharp.Demos.QtWidgets' }
    @{ Name = 'QtNetwork';      Project = 'demos/QtNetwork/QtSharp.Demos.QtNetwork.csproj';       Exe = 'QtSharp.Demos.QtNetwork' }
    @{ Name = 'QtSvg';          Project = 'demos/QtSvg/QtSharp.Demos.QtSvg.csproj';               Exe = 'QtSharp.Demos.QtSvg' }
    @{ Name = 'QtSvgWidgets';   Project = 'demos/QtSvgWidgets/QtSharp.Demos.QtSvgWidgets.csproj'; Exe = 'QtSharp.Demos.QtSvgWidgets' }
    @{ Name = 'QtPrintSupport'; Project = 'demos/QtPrintSupport/QtSharp.Demos.QtPrintSupport.csproj'; Exe = 'QtSharp.Demos.QtPrintSupport' }
    @{ Name = 'QtMultimedia';   Project = 'demos/QtMultimedia/QtSharp.Demos.QtMultimedia.csproj'; Exe = 'QtSharp.Demos.QtMultimedia' }
    @{ Name = 'Charts';         Project = 'demos/Charts/QtSharp.Charts.Demos.csproj';             Exe = 'QtSharp.Charts.Demos' }
    @{ Name = 'Qt3D';           Project = 'demos/Qt3D/QtSharp.Qt3D.Demos.csproj';                 Exe = 'QtSharp.Qt3D.Demos' }
)

$buildArgs = @('--nologo')
if ($QtRuntimeDir) { $buildArgs += "-p:QtRuntimeDir=$QtRuntimeDir" }

foreach ($demo in $demos) {
    dotnet build "$root/$($demo.Project)" -c $Configuration @buildArgs *> "$root/out/demo-$($demo.Name)-build.log"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "$($demo.Name) demo build failed"
        Select-String -Path "$root/out/demo-$($demo.Name)-build.log" -Pattern "error CS" |
            Select-Object -First 8 | ForEach-Object { Write-Host $_.Line.Trim() }
        exit 1
    }
    & "$root/tools/run-window-tests.ps1" `
        -Exe "$root/demos/$($demo.Name)/bin/$Configuration/net10.0/$($demo.Exe)$exeSuffix" `
        -Label "$($demo.Name) demos" -Jobs $Jobs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

exit 0
